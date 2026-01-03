using OneAI.Constants;
using OneAI.Data;
using OneAI.Entities;
using OneAI.Extensions;
using OneAI.Services.AI.Models.Gemini.Input;
using OneAI.Services.Logging;
using System.Net;
using System.Text.Json;
using OneAI.Services.AI.Gemini;
using OneAI.Services.GeminiOAuth;

namespace OneAI.Services.AI;

/// <summary>
/// Gemini API 服务 - 处理 Gemini 原生 API 请求
/// </summary>
public class GeminiAPIService(
    AccountQuotaCacheService quotaCache,
    AIRequestLogService requestLogService,
    ILogger<GeminiAPIService> logger,
    IConfiguration configuration,
    GeminiOAuthService geminiOAuthService)
{
    // 静态HttpClient实例 - 避免套接字耗尽，提高性能
    private static readonly HttpClient HttpClient = new();

    private static readonly string[] ClientErrorKeywords =
    [
        "invalid_argument",
        "permission_denied",
        "resource_exhausted",
        "\"INVALID_ARGUMENT\""
    ];

    /// <summary>
    /// 执行 Gemini 内容生成请求（非流式）
    /// </summary>
    public async Task ExecuteGenerateContent(
        HttpContext context,
        GeminiInput input,
        string model,
        string? conversationId,
        AIAccountService aiAccountService)
    {
        // 重置AIProviderIds（每次尝试时清空）
        AIProviderAsyncLocal.AIProviderIds = new List<int>();
        const int maxRetries = 15;
        string? lastErrorMessage = null;
        HttpStatusCode? lastStatusCode = null;

        // 创建请求日志
        var (logId, stopwatch) = await requestLogService.CreateRequestLog(
            context,
            model,
            false,
            null,
            false);

        bool sessionStickinessUsed = false;

        try
        {
            for (int attempt = 1; attempt <= maxRetries; attempt++)
            {
                if (context.Response.HasStarted)
                {
                    logger.LogWarning("响应已开始写入，无法继续重试");
                    await requestLogService.RecordFailure(
                        logId,
                        stopwatch,
                        context.Response.StatusCode,
                        "响应已开始写入，无法继续重试");
                    return;
                }

                logger.LogDebug("尝试第 {Attempt}/{MaxRetries} 次获取 Gemini 账户", attempt, maxRetries);

                // 会话粘性
                AIAccount? account = null;

                if (!string.IsNullOrEmpty(conversationId))
                {
                    var lastAccountId = quotaCache.GetConversationAccount(conversationId);
                    if (lastAccountId.HasValue)
                    {
                        account = await aiAccountService.TryGetAccountById(lastAccountId.Value);
                        if (account is { Provider: AIProviders.Gemini })
                        {
                            sessionStickinessUsed = true;
                            logger.LogInformation(
                                "会话粘性成功：会话 {ConversationId} 复用 Gemini 账户 {AccountId}",
                                conversationId,
                                lastAccountId.Value);
                        }
                        else
                        {
                            account = null;
                        }
                    }
                }

                // 智能选择 Gemini 账户
                if (account == null)
                {
                    account = await aiAccountService.GetAIAccountByProvider(AIProviders.Gemini);
                }

                if (account == null)
                {
                    if (!string.IsNullOrWhiteSpace(lastErrorMessage))
                    {
                        logger.LogWarning(
                            "无可用的 Gemini 账户，返回上一次错误 (状态码: {StatusCode}): {ErrorMessage}",
                            (int)(lastStatusCode ?? HttpStatusCode.ServiceUnavailable),
                            lastErrorMessage);
                        break;
                    }

                    lastErrorMessage = $"无可用的 Gemini 账户（尝试 {attempt}/{maxRetries}）";
                    lastStatusCode = HttpStatusCode.ServiceUnavailable;

                    logger.LogWarning(lastErrorMessage);
                    break;
                }

                AIProviderAsyncLocal.AIProviderIds.Add(account.Id);

                if (account.Provider == AIProviders.Gemini)
                {
                    var geminiOAuth = await GetValidGeminiOAuthAsync(account, aiAccountService);
                    if (geminiOAuth == null)
                    {
                        lastErrorMessage = $"账户 {account.Id} Gemini Token 无效或已过期且刷新失败";
                        lastStatusCode = HttpStatusCode.Unauthorized;

                        logger.LogWarning(lastErrorMessage);

                        if (attempt < maxRetries)
                        {
                            continue;
                        }

                        break;
                    }

                    // 发送请求
                    try
                    {
                        var codeAssistEndpoint = configuration["Gemini:CodeAssistEndpoint"] ??
                                                 throw new InvalidOperationException(
                                                     "Gemini CodeAssistEndpoint is not configured");
                        var action = "generateContent";
                        var url = $"{codeAssistEndpoint}/v1internal:{action}";
                        var userAgent = GetUserAgent();

                        using var response = await SendGeminiRequest(
                            url,
                            input,
                            geminiOAuth.Token,
                            geminiOAuth.ProjectId,
                            model,
                            isStream: false,
                            userAgent: userAgent);

                        var timeToFirstByteMs = stopwatch.ElapsedMilliseconds;

                        await requestLogService.UpdateRetry(logId, attempt, account.Id);

                        // 处理响应
                        if (response.StatusCode >= HttpStatusCode.BadRequest)
                        {
                            var error = await response.Content.ReadAsStringAsync();

                            lastErrorMessage = error;
                            lastStatusCode = response.StatusCode;

                            logger.LogError(
                                "Gemini 请求失败 (账户: {AccountId}, 状态码: {StatusCode}, 尝试: {Attempt}/{MaxRetries}): {Error}",
                                account.Id,
                                response.StatusCode,
                                attempt,
                                maxRetries,
                                error);

                            // 检查是否是客户端错误
                            bool isClientError = ClientErrorKeywords.Any(keyword => error.Contains(keyword));

                            if (response.StatusCode == HttpStatusCode.Unauthorized ||
                                response.StatusCode == HttpStatusCode.Forbidden)
                            {
                                await aiAccountService.DisableAccount(account.Id);

                                if (response.StatusCode == HttpStatusCode.Unauthorized || isClientError)
                                {
                                    if (attempt < maxRetries)
                                    {
                                        continue;
                                    }

                                    await requestLogService.RecordFailure(
                                        logId,
                                        stopwatch,
                                        (int)response.StatusCode,
                                        error);

                                    break;
                                }
                            }

                            if (isClientError)
                            {
                                await requestLogService.RecordFailure(
                                    logId,
                                    stopwatch,
                                    (int)response.StatusCode,
                                    error);

                                context.Response.ContentType = "application/json";
                                context.Response.StatusCode = (int)response.StatusCode;
                                await context.Response.WriteAsync(error, context.RequestAborted);
                                return;
                            }

                            if (attempt < maxRetries)
                            {
                                continue;
                            }

                            await requestLogService.RecordFailure(
                                logId,
                                stopwatch,
                                (int)response.StatusCode,
                                error);

                            break;
                        }

                        // 成功响应
                        logger.LogInformation(
                            "成功处理 Gemini 请求 (账户: {AccountId}, 模型: {Model}, 尝试: {Attempt}/{MaxRetries})",
                            account.Id,
                            model,
                            attempt, maxRetries);

                        if (!string.IsNullOrEmpty(conversationId))
                        {
                            quotaCache.SetConversationAccount(conversationId, account.Id);
                        }

                        var responseText = await response.Content.ReadAsStringAsync(context.RequestAborted);
                        if (!TryParseJson(responseText, out var doc))
                        {
                            lastErrorMessage = "Gemini 响应不是有效的 JSON";
                            lastStatusCode = HttpStatusCode.BadGateway;

                            logger.LogError(
                                "Gemini 响应不是有效的 JSON (账户: {AccountId}, 尝试: {Attempt}/{MaxRetries}): {Body}",
                                account.Id,
                                attempt,
                                maxRetries,
                                responseText);

                            if (attempt < maxRetries)
                            {
                                continue;
                            }

                            await requestLogService.RecordFailure(
                                logId,
                                stopwatch,
                                (int)lastStatusCode,
                                lastErrorMessage);

                            break;
                        }

                        using (doc)
                        {
                            if (!TryGetResponseAndCandidate(doc.RootElement, out var responseElement,
                                    out var candidate))
                            {
                                lastErrorMessage = "Gemini 响应缺少 response/candidates 字段";
                                lastStatusCode = HttpStatusCode.BadGateway;

                                logger.LogError(
                                    "Gemini 响应缺少 response/candidates 字段 (账户: {AccountId}, 尝试: {Attempt}/{MaxRetries}): {Body}",
                                    account.Id,
                                    attempt,
                                    maxRetries,
                                    responseText);

                                if (attempt < maxRetries)
                                {
                                    continue;
                                }

                                await requestLogService.RecordFailure(
                                    logId,
                                    stopwatch,
                                    (int)lastStatusCode,
                                    lastErrorMessage);

                                break;
                            }

                            _ = TryGetUsageMetadata(
                                responseElement,
                                candidate,
                                out var promptTokens,
                                out var completionTokens,
                                out var totalTokens);

                            await requestLogService.RecordSuccess(
                                logId,
                                stopwatch,
                                (int)response.StatusCode,
                                timeToFirstByteMs: timeToFirstByteMs,
                                promptTokens: promptTokens,
                                completionTokens: completionTokens,
                                totalTokens: totalTokens);

                            context.Response.ContentType = "application/json";
                            context.Response.StatusCode = (int)response.StatusCode;
                            await context.Response.WriteAsync(responseElement.GetRawText(), context.RequestAborted);
                            return;
                        }

                        // 走到这里说明解析失败但不再重试，交由外层统一失败处理
                    }
                    catch (Exception ex)
                    {
                        lastErrorMessage = $"请求异常 (账户: {account.Id}): {ex.Message}";
                        lastStatusCode = HttpStatusCode.InternalServerError;

                        logger.LogError(ex, "Gemini 请求异常（尝试 {Attempt}/{MaxRetries}）", attempt, maxRetries);

                        if (attempt < maxRetries)
                        {
                            continue;
                        }

                        await requestLogService.RecordFailure(
                            logId,
                            stopwatch,
                            (int)lastStatusCode,
                            lastErrorMessage);

                        break;
                    }
                }
                else if (account.Provider == AIProviders.Factory)
                {
                    
                }
            }

            // 所有重试都失败
            context.Response.StatusCode = (int)(lastStatusCode ?? HttpStatusCode.ServiceUnavailable);

            var finalErrorMessage = lastErrorMessage ?? $"所有 {maxRetries} 次重试均失败，无法完成请求";

            logger.LogError("Gemini 请求失败: {ErrorMessage}", finalErrorMessage);

            await requestLogService.RecordFailure(
                logId,
                stopwatch,
                context.Response.StatusCode,
                finalErrorMessage);

            await context.Response.WriteAsync(finalErrorMessage);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Gemini 请求处理过程中发生未捕获的异常");

            try
            {
                await requestLogService.RecordFailure(
                    logId,
                    stopwatch,
                    500,
                    $"未捕获的异常: {ex.Message}");
            }
            catch (Exception logEx)
            {
                logger.LogError(logEx, "记录失败日志时发生异常");
            }

            if (!context.Response.HasStarted)
            {
                context.Response.StatusCode = 500;
                await context.Response.WriteAsync($"服务器内部错误: {ex.Message}");
            }
        }
    }

    /// <summary>
    /// 执行 Gemini 流式内容生成请求
    /// </summary>
    public async Task ExecuteStreamGenerateContent(
        HttpContext context,
        GeminiInput input,
        string model,
        string? conversationId,
        AIAccountService aiAccountService)
    {
        AIProviderAsyncLocal.AIProviderIds = new List<int>();
        const int maxRetries = 15;
        string? lastErrorMessage = null;
        HttpStatusCode? lastStatusCode = null;

        // 创建请求日志
        var (logId, stopwatch) = await requestLogService.CreateRequestLog(
            context,
            model,
            false,
            null,
            false);

        bool sessionStickinessUsed = false;

        try
        {
            for (int attempt = 1; attempt <= maxRetries; attempt++)
            {
                if (context.Response.HasStarted)
                {
                    logger.LogWarning("响应已开始写入，无法继续重试");
                    return;
                }

                logger.LogDebug("尝试第 {Attempt}/{MaxRetries} 次获取 Gemini 账户（流式）", attempt, maxRetries);

                // 会话粘性
                AIAccount? account = null;

                if (!string.IsNullOrEmpty(conversationId))
                {
                    var lastAccountId = quotaCache.GetConversationAccount(conversationId);
                    if (lastAccountId.HasValue)
                    {
                        account = await aiAccountService.TryGetAccountById(lastAccountId.Value);
                        if (account != null
                            && account.Provider is AIProviders.Gemini)
                        {
                            sessionStickinessUsed = true;
                            logger.LogInformation(
                                "会话粘性成功：会话 {ConversationId} 复用 Gemini 账户 {AccountId}",
                                conversationId,
                                lastAccountId.Value);
                        }
                        else
                        {
                            account = null;
                        }
                    }
                }

                // 智能选择 Gemini 账户
                if (account == null)
                {
                    account = await aiAccountService.GetAIAccountByProvider(AIProviders.Gemini);
                }

                if (account == null)
                {
                    if (!string.IsNullOrWhiteSpace(lastErrorMessage))
                    {
                        logger.LogWarning(
                            "无可用的 Gemini 账户，返回上一次错误 (状态码: {StatusCode}): {ErrorMessage}",
                            (int)(lastStatusCode ?? HttpStatusCode.ServiceUnavailable),
                            lastErrorMessage);
                        break;
                    }

                    lastErrorMessage = $"无可用的 Gemini 账户（尝试 {attempt}/{maxRetries}）";
                    lastStatusCode = HttpStatusCode.ServiceUnavailable;

                    logger.LogWarning(lastErrorMessage);
                    break;
                }

                AIProviderAsyncLocal.AIProviderIds.Add(account.Id);

                var geminiOAuth = await GetValidGeminiOAuthAsync(account, aiAccountService);
                if (geminiOAuth == null)
                {
                    lastErrorMessage = $"账户 {account.Id} Gemini Token 无效或已过期且刷新失败";
                    lastStatusCode = HttpStatusCode.Unauthorized;

                    logger.LogWarning(lastErrorMessage);

                    if (attempt < maxRetries)
                    {
                        continue;
                    }

                    break;
                }

                // 发送流式请求
                try
                {
                    var codeAssistEndpoint = configuration["Gemini:CodeAssistEndpoint"] ??
                                             throw new InvalidOperationException(
                                                 "Gemini CodeAssistEndpoint is not configured");
                    var action = "streamGenerateContent";
                    var url = $"{codeAssistEndpoint}/v1internal:{action}?alt=sse";
                    var userAgent = GetUserAgent();

                    using var response = await SendGeminiRequest(
                        url,
                        input,
                        geminiOAuth.Token,
                        geminiOAuth.ProjectId,
                        model,
                        isStream: true,
                        userAgent: userAgent);

                    await requestLogService.UpdateRetry(logId, attempt, account.Id);

                    // 处理响应
                    if (response.StatusCode >= HttpStatusCode.BadRequest)
                    {
                        var error = await response.Content.ReadAsStringAsync();

                        lastErrorMessage = error;
                        lastStatusCode = response.StatusCode;

                        logger.LogError(
                            "Gemini 流式请求失败 (账户: {AccountId}, 状态码: {StatusCode}): {Error}",
                            account.Id,
                            response.StatusCode,
                            error);

                        if (attempt < maxRetries)
                        {
                            continue;
                        }

                        await requestLogService.RecordFailure(
                            logId,
                            stopwatch,
                            (int)response.StatusCode,
                            error);

                        break;
                    }

                    // 成功响应
                    logger.LogInformation(
                        "成功处理 Gemini 流式请求 (账户: {AccountId}, 模型: {Model})",
                        account.Id,
                        model);

                    if (!string.IsNullOrEmpty(conversationId))
                    {
                        quotaCache.SetConversationAccount(conversationId, account.Id);
                    }

                    await requestLogService.RecordSuccess(
                        logId,
                        stopwatch,
                        (int)response.StatusCode,
                        timeToFirstByteMs: stopwatch.ElapsedMilliseconds);

                    // 流式传输响应
                    context.Response.ContentType = "text/event-stream;charset=utf-8";
                    context.Response.Headers.TryAdd("Cache-Control", "no-cache");
                    context.Response.Headers.TryAdd("Connection", "keep-alive");
                    context.Response.StatusCode = (int)response.StatusCode;

                    try
                    {
                        await using var stream = await response.Content.ReadAsStreamAsync(context.RequestAborted);
                        using var reader = new StreamReader(stream);

                        while (true)
                        {
                            context.RequestAborted.ThrowIfCancellationRequested();

                            var line = await reader.ReadLineAsync();
                            if (line == null)
                            {
                                break;
                            }

                            if (!TryParseSseDataLine(line, out var prefix, out var data))
                            {
                                await WriteSseLineAsync(context, line, context.RequestAborted);
                                continue;
                            }

                            if (string.Equals(data, "[DONE]", StringComparison.OrdinalIgnoreCase))
                            {
                                await WriteSseLineAsync(context, line, context.RequestAborted);
                                await WriteSseLineAsync(context, string.Empty, context.RequestAborted);
                                break;
                            }

                            if (string.IsNullOrWhiteSpace(data) || !TryParseJson(data, out var doc))
                            {
                                await WriteSseLineAsync(context, line, context.RequestAborted);
                                continue;
                            }

                            using (doc)
                            {
                                if (TryGetResponseAndCandidate(doc.RootElement, out var responseElement, out _))
                                {
                                    await WriteSseLineAsync(
                                        context,
                                        prefix + responseElement.GetRawText(),
                                        context.RequestAborted);
                                }
                                else
                                {
                                    await WriteSseLineAsync(context, line, context.RequestAborted);
                                }
                            }
                        }
                    }
                    catch (OperationCanceledException)
                    {
                        logger.LogInformation("客户端取消 Gemini 流式请求 (账户: {AccountId})", account.Id);
                        return;
                    }
                    catch (Exception streamEx)
                    {
                        logger.LogError(streamEx,
                            "Gemini 流式传输过程中发生异常 (账户: {AccountId})",
                            account.Id);
                        return;
                    }

                    return;
                }
                catch (Exception ex)
                {
                    lastErrorMessage = $"请求异常 (账户: {account.Id}): {ex.Message}";
                    lastStatusCode = HttpStatusCode.InternalServerError;

                    logger.LogError(ex, "Gemini 流式请求异常（尝试 {Attempt}/{MaxRetries}）", attempt, maxRetries);

                    if (attempt < maxRetries)
                    {
                        continue;
                    }

                    await requestLogService.RecordFailure(
                        logId,
                        stopwatch,
                        (int)lastStatusCode,
                        lastErrorMessage);

                    break;
                }
            }

            // 所有重试都失败
            context.Response.StatusCode = (int)(lastStatusCode ?? HttpStatusCode.ServiceUnavailable);

            var finalErrorMessage = lastErrorMessage ?? $"所有 {maxRetries} 次重试均失败，无法完成请求";

            logger.LogError("Gemini 流式请求失败: {ErrorMessage}", finalErrorMessage);

            await requestLogService.RecordFailure(
                logId,
                stopwatch,
                context.Response.StatusCode,
                finalErrorMessage);

            if (!context.Response.HasStarted)
            {
                await context.Response.WriteAsync(finalErrorMessage);
            }
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Gemini 流式请求处理过程中发生未捕获的异常");

            try
            {
                await requestLogService.RecordFailure(
                    logId,
                    stopwatch,
                    500,
                    $"未捕获的异常: {ex.Message}");
            }
            catch (Exception logEx)
            {
                logger.LogError(logEx, "记录失败日志时发生异常");
            }

            if (!context.Response.HasStarted)
            {
                context.Response.StatusCode = 500;
                await context.Response.WriteAsync($"服务器内部错误: {ex.Message}");
            }
        }
    }

    /// <summary>
    /// 发送 Gemini API 请求
    /// </summary>
    private static async Task<HttpResponseMessage> SendGeminiRequest(
        string url,
        GeminiInput input,
        string accessToken,
        string? projectId,
        string model,
        bool isStream,
        string userAgent)
    {
        // 构造符合Gemini内部API格式的请求体
        var geminiPayload = new
        {
            model = model,
            project = projectId ?? throw new InvalidOperationException("Project ID is required for Gemini API"),
            request = input
        };

        // 序列化为JSON
        var jsonPayload = JsonSerializer.Serialize(geminiPayload);
        var content = new StringContent(jsonPayload, System.Text.Encoding.UTF8, "application/json");

        var requestMessage = new HttpRequestMessage(HttpMethod.Post, url)
        {
            Content = content
        };

        // 添加请求头
        requestMessage.Headers.Add("Authorization", $"Bearer {accessToken}");
        requestMessage.Headers.Add("User-Agent", userAgent);

        return await HttpClient.SendAsync(requestMessage,
            isStream ? HttpCompletionOption.ResponseHeadersRead : HttpCompletionOption.ResponseContentRead);
    }

    private async Task<GeminiOAuthCredentialsDto?> GetValidGeminiOAuthAsync(
        AIAccount account,
        AIAccountService aiAccountService)
    {
        var geminiOAuth = account.GetGeminiOauth();
        if (geminiOAuth == null)
        {
            return null;
        }

        if (!IsGeminiTokenExpired(geminiOAuth))
        {
            return geminiOAuth;
        }

        try
        {
            await RefreshGeminiOAuthTokenAsync(account);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Gemini 账户 {AccountId} Token 刷新失败，已禁用账户", account.Id);
            await aiAccountService.DisableAccount(account.Id);
            return null;
        }

        geminiOAuth = account.GetGeminiOauth();
        if (geminiOAuth == null || string.IsNullOrWhiteSpace(geminiOAuth.Token))
        {
            logger.LogWarning("Gemini 账户 {AccountId} Token 刷新后仍无效，已禁用账户", account.Id);
            await aiAccountService.DisableAccount(account.Id);
            return null;
        }

        return geminiOAuth;
    }

    private Task RefreshGeminiOAuthTokenAsync(AIAccount account)
    {
        return account.Provider switch
        {
            AIProviders.Gemini => geminiOAuthService.RefreshGeminiOAuthTokenAsync(account),
            _ => throw new InvalidOperationException($"Unsupported Gemini provider: {account.Provider}")
        };
    }

    private static bool IsGeminiTokenExpired(GeminiOAuthCredentialsDto geminiOAuth)
    {
        if (string.IsNullOrWhiteSpace(geminiOAuth.Expiry))
        {
            return false;
        }

        return DateTime.TryParse(geminiOAuth.Expiry, out var expiryUtc)
               && expiryUtc.ToUniversalTime() <= DateTime.UtcNow;
    }

    private static bool TryGetResponseAndCandidate(
        JsonElement root,
        out JsonElement response,
        out JsonElement candidate)
    {
        response = default;
        candidate = default;

        if (!root.TryGetProperty("response", out response) || response.ValueKind != JsonValueKind.Object)
        {
            return false;
        }

        if (!response.TryGetProperty("candidates", out var candidates)
            || candidates.ValueKind != JsonValueKind.Array)
        {
            return false;
        }

        var first = candidates.EnumerateArray().FirstOrDefault();
        if (first.ValueKind != JsonValueKind.Object)
        {
            return false;
        }

        candidate = first;
        return true;
    }

    private static bool TryGetUsageMetadata(
        JsonElement response,
        JsonElement candidate,
        out int? promptTokens,
        out int? completionTokens,
        out int? totalTokens)
    {
        promptTokens = null;
        completionTokens = null;
        totalTokens = null;

        var responseUsage = GetUsageMetadata(response, "usageMetadata");
        var candidateUsage = GetUsageMetadata(candidate, "usageMetadata");

        var responseScore = GetUsageScore(responseUsage);
        var candidateScore = GetUsageScore(candidateUsage);

        var usage = candidateScore > responseScore ? candidateUsage : responseUsage;
        if (usage == null)
        {
            return false;
        }

        var usageValue = usage.Value;

        if (usageValue.TryGetProperty("promptTokenCount", out var promptProp))
        {
            promptTokens = promptProp.GetInt32();
        }

        if (usageValue.TryGetProperty("candidatesTokenCount", out var completionProp))
        {
            completionTokens = completionProp.GetInt32();
        }

        if (usageValue.TryGetProperty("totalTokenCount", out var totalProp))
        {
            totalTokens = totalProp.GetInt32();
        }

        return promptTokens.HasValue || completionTokens.HasValue || totalTokens.HasValue;
    }

    private static JsonElement? GetUsageMetadata(JsonElement element, string name)
    {
        if (element.ValueKind == JsonValueKind.Object
            && element.TryGetProperty(name, out var usage)
            && usage.ValueKind == JsonValueKind.Object)
        {
            return usage;
        }

        return null;
    }

    private static int GetUsageScore(JsonElement? element)
    {
        if (element == null)
        {
            return 0;
        }

        var score = 0;
        if (element.Value.TryGetProperty("promptTokenCount", out _))
        {
            score++;
        }

        if (element.Value.TryGetProperty("candidatesTokenCount", out _))
        {
            score++;
        }

        if (element.Value.TryGetProperty("totalTokenCount", out _))
        {
            score++;
        }

        return score;
    }

    private static bool TryParseJson(string payload, out JsonDocument doc)
    {
        try
        {
            doc = JsonDocument.Parse(payload);
            return true;
        }
        catch (JsonException)
        {
            doc = default!;
            return false;
        }
    }

    private static bool TryParseSseDataLine(string line, out string prefix, out string data)
    {
        prefix = string.Empty;
        data = string.Empty;

        if (string.IsNullOrEmpty(line))
        {
            return false;
        }

        var trimmed = line.TrimStart();
        if (!trimmed.StartsWith("data:", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        var leadingLength = line.Length - trimmed.Length;
        var dataIndex = 5;
        while (dataIndex < trimmed.Length && char.IsWhiteSpace(trimmed[dataIndex]))
        {
            dataIndex++;
        }

        prefix = line[..leadingLength] + trimmed[..dataIndex];
        data = trimmed.Length <= dataIndex ? string.Empty : trimmed[dataIndex..];
        return true;
    }

    private static async Task WriteSseLineAsync(HttpContext context, string line, CancellationToken ct)
    {
        await context.Response.WriteAsync(line, ct);
        await context.Response.WriteAsync("\n", ct);
        await context.Response.Body.FlushAsync(ct);
    }

    /// <summary>
    /// 获取与 Gemini CLI 一致的 User-Agent
    /// </summary>
    private static string GetUserAgent()
    {
        const string cliVersion = "0.1.5";
        var system = System.Runtime.InteropServices.RuntimeInformation.IsOSPlatform(
                System.Runtime.InteropServices.OSPlatform.Windows) ? "Windows" :
            System.Runtime.InteropServices.RuntimeInformation.IsOSPlatform(
                System.Runtime.InteropServices.OSPlatform.Linux) ? "Linux" :
            System.Runtime.InteropServices.RuntimeInformation.IsOSPlatform(
                System.Runtime.InteropServices.OSPlatform.OSX) ? "Darwin" : "Unknown";

        var arch = System.Runtime.InteropServices.RuntimeInformation.ProcessArchitecture.ToString();
        return $"GeminiCLI/{cliVersion} ({system}; {arch})";
    }
}
