using System.Net;
using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.AspNetCore.Authorization;
using OneAI.Constants;
using OneAI.Core;
using OneAI.Data;
using OneAI.Entities;
using OneAI.Extensions;
using OneAI.Services.AI.Models.Responses.Input;
using OneAI.Services.Logging;
using OneAI.Services.OpenAIOAuth;
using Thor.Abstractions.Responses;

namespace OneAI.Services.AI;

public class ResponsesService
{
    private static readonly string[] ClientErrorKeywords =
    [
        "invalid_request_error",
        "missing_required_parameter"
    ];

    private readonly AccountQuotaCacheService _quotaCache;
    private readonly AIRequestLogService _requestLogService;
    private readonly OpenAIOAuthService _openAiOAuthService;
    private readonly ILogger<ResponsesService> _logger;

    public ResponsesService(
        AccountQuotaCacheService quotaCache,
        AIRequestLogService requestLogService,
        OpenAIOAuthService openAiOAuthService,
        ILogger<ResponsesService> logger)
    {
        _quotaCache = quotaCache;
        _requestLogService = requestLogService;
        _openAiOAuthService = openAiOAuthService;
        _logger = logger;
    }

    public async Task Execute(
        HttpContext context,
        ResponsesInput request,
        AIAccountService aiAccountService)
    {
        const int maxRetries = 15;
        string? lastErrorMessage = null;
        HttpStatusCode? lastStatusCode = null;

        // ==================== 📊 创建请求日志 ====================
        // 在请求开始时创建日志记录，初始不关联账户
        var (logId, stopwatch) = await _requestLogService.CreateRequestLog(
            context,
            request,
            account: null, // 初始时还未选择账户
            sessionStickinessUsed: false);

        bool sessionStickinessUsed = false;
        var expiredRateLimitAccountIds = new HashSet<int>();

        try
        {
            // 每个请求只初始化一次：用于在重试链路中排除已尝试过的账户
            AIProviderAsyncLocal.AIProviderIds = new List<int>(maxRetries);

            for (int attempt = 1; attempt <= maxRetries; attempt++)
            {
                // 检查响应是否已经开始写入（一旦开始写入Body就不能重试）
                if (context.Response.HasStarted)
                {
                    _logger.LogWarning("响应已开始写入，无法继续重试");

                    // 📊 记录警告：响应已开始但需要重试（可能发生了异常）
                    await _requestLogService.RecordFailure(
                        logId,
                        stopwatch,
                        context.Response.StatusCode,
                        "响应已开始写入，无法继续重试");

                    return;
                }

                _logger.LogDebug("尝试第 {Attempt}/{MaxRetries} 次获取账户", attempt, maxRetries);

                // 尝试会话粘性：如果有 conversationId，优先使用上次的账户
                AIAccount? account = null;
                string? conversationId = request.PromptCacheKey;

                if (!string.IsNullOrEmpty(conversationId))
                {
                    var lastAccountId = _quotaCache.GetConversationAccount(conversationId);
                    if (lastAccountId.HasValue)
                    {
                        _logger.LogDebug(
                            "会话 {ConversationId} 上次使用账户 {AccountId}，尝试复用",
                            conversationId,
                            lastAccountId.Value);

                        account = await aiAccountService.TryGetAccountById(lastAccountId.Value);

                        if (account != null && account.Provider == AIProviders.OpenAI)
                        {
                            sessionStickinessUsed = true;
                            _logger.LogInformation(
                                "会话粘性成功：会话 {ConversationId} 复用账户 {AccountId}",
                                conversationId,
                                lastAccountId.Value);
                        }
                        else
                        {
                            _logger.LogDebug(
                                "会话 {ConversationId} 的上次账户 {AccountId} 不可用，将智能选择新账户",
                                conversationId,
                                lastAccountId.Value);
                            account = null;
                        }
                    }
                }

                // 如果会话粘性失败，使用智能选择
                if (account == null)
                {
                    account = await aiAccountService.GetAIAccount(request.Model, AIProviders.OpenAI);
                }

                if (account == null)
                {
                    const string noAccountMessage = "账户池都无可用";
                    _logger.LogWarning(noAccountMessage);

                    await _requestLogService.RecordFailure(
                        logId,
                        stopwatch,
                        StatusCodes.Status503ServiceUnavailable,
                        noAccountMessage);

                    await WriteOpenAiErrorResponse(
                        context,
                        noAccountMessage,
                        StatusCodes.Status503ServiceUnavailable);
                    return;
                }

                if (account.IsRateLimitExpired())
                {
                    expiredRateLimitAccountIds.Add(account.Id);
                }

                AIProviderAsyncLocal.AIProviderIds.Add(account.Id);

                await _requestLogService.UpdateRetry(logId, attempt, account.Id).ConfigureAwait(false);

                if (account.Provider == AIProviders.OpenAI)
                {
                    var oauth = account.GetOpenAiOauth();
                    if (oauth == null || string.IsNullOrWhiteSpace(oauth.AccessToken))
                    {
                        _logger.LogWarning("OpenAI 账户 {AccountId} 缺少 OAuth AccessToken，已禁用该账户", account.Id);
                        await aiAccountService.DisableAccount(account.Id);
                        lastErrorMessage = $"账户 {account.Id} 缺少 OAuth AccessToken（尝试 {attempt}/{maxRetries}）";
                        lastStatusCode = HttpStatusCode.Unauthorized;

                        if (attempt < maxRetries)
                        {
                            continue;
                        }

                        await _requestLogService.RecordFailure(
                            logId,
                            stopwatch,
                            (int)lastStatusCode,
                            lastErrorMessage);
                        break;
                    }

                    if (IsOpenAiTokenExpired(oauth))
                    {
                        _logger.LogInformation("OpenAI 账户 {AccountId} AccessToken 已过期，尝试刷新后再请求", account.Id);
                        try
                        {
                            await _openAiOAuthService.RefreshOpenAiOAuthTokenAsync(account);
                        }
                        catch (Exception ex)
                        {
                            _logger.LogWarning(ex, "OpenAI 账户 {AccountId} Token 刷新失败，已禁用该账户", account.Id);
                            await aiAccountService.DisableAccount(account.Id);
                            lastErrorMessage = $"账户 {account.Id} Token 刷新失败（尝试 {attempt}/{maxRetries}）";
                            lastStatusCode = HttpStatusCode.Unauthorized;

                            if (attempt < maxRetries)
                            {
                                continue;
                            }

                            await _requestLogService.RecordFailure(
                                logId,
                                stopwatch,
                                (int)lastStatusCode,
                                lastErrorMessage);
                            break;
                        }

                        oauth = account.GetOpenAiOauth();
                        if (oauth == null || string.IsNullOrWhiteSpace(oauth.AccessToken))
                        {
                            _logger.LogWarning("OpenAI 账户 {AccountId} Token 刷新后仍无效，已禁用该账户", account.Id);
                            await aiAccountService.DisableAccount(account.Id);
                            lastErrorMessage = $"账户 {account.Id} Token 刷新后仍无效（尝试 {attempt}/{maxRetries}）";
                            lastStatusCode = HttpStatusCode.Unauthorized;

                            if (attempt < maxRetries)
                            {
                                continue;
                            }

                            await _requestLogService.RecordFailure(
                                logId,
                                stopwatch,
                                (int)lastStatusCode,
                                lastErrorMessage);
                            break;
                        }
                    }

                    var accessToken = oauth.AccessToken;

                    // 准备请求头和代理配置
                    var headers = new Dictionary<string, string>
                    {
                        { "Authorization", "Bearer " + accessToken },
                        { "User-Agent", "codex_cli_rs/0.76.0 (Windows 10.0.26200; x86_64) vscode/1.105.1" },
                        { "openai-beta", "responses=experimental" },
                        { "originator", "codex_cli_rs" }
                    };

                    if (context.Request.Headers.TryGetValue("conversation_id", out var convIdHeader))
                    {
                        headers["conversation_id"] = convIdHeader;
                    }

                    if (context.Request.Headers.TryGetValue("session_id", out var session_id))
                    {
                        headers["session_id"] = session_id;
                    }

                    var address = "https://chatgpt.com/backend-api/codex";

                    if (!string.IsNullOrEmpty(account?.BaseUrl))
                    {
                        address = account?.BaseUrl;
                    }

                    if (request.Stream == true)
                    {
                        // 判断当前请求是否包含了Codex的提示词
                        if (string.IsNullOrEmpty(request.Instructions))
                        {
                            request.Instructions = AIPrompt.CodeXPrompt;
                        }

                        request.Store = false;
                        request.ServiceTier = null;

                        HttpResponseMessage response;
                        try
                        {
                            response = await HttpClientFactory.GetHttpClient(address, null)
                                .HttpRequestRaw(address.TrimEnd('/') + "/responses", request, headers);
                        }
                        catch (Exception ex)
                        {
                            lastErrorMessage = $"请求异常 (账户: {account.Id}): {ex.Message}";
                            lastStatusCode = HttpStatusCode.InternalServerError;

                            _logger.LogError(ex, "账户 {AccountId} 请求异常（尝试 {Attempt}/{MaxRetries}）",
                                account.Id, attempt, maxRetries);

                            // 如果还有重试机会，继续下一次尝试
                            if (attempt < maxRetries)
                            {
                                continue;
                            }

                            // 📊 记录失败（最后一次尝试）
                            await _requestLogService.RecordFailure(
                                logId,
                                stopwatch,
                                (int)lastStatusCode,
                                lastErrorMessage);

                            break;
                        }

                        // ✅ 提取并更新配额信息（无论响应状态如何都尝试提取）
                        var quotaInfo = AccountQuotaCacheService.ExtractFromHeaders(account.Id, response.Headers);
                        if (quotaInfo != null)
                        {
                            _quotaCache.UpdateQuota(quotaInfo);

                            // 如果达到配额上限，更新数据库
                            if (quotaInfo.IsQuotaExhausted())
                            {
                                _logger.LogWarning(
                                    "账户 {AccountId} 配额已耗尽: {Status}",
                                    account.Id,
                                    quotaInfo.GetStatusDescription());

                                await aiAccountService.MarkAccountAsRateLimited(
                                    account.Id,
                                    quotaInfo.PrimaryResetAfterSeconds);
                            }
                        }

                        // ✅ 处理不同的HTTP状态码
                        switch (response.StatusCode)
                        {
                            case HttpStatusCode.Unauthorized:
                            case HttpStatusCode.Forbidden:
                                _logger.LogError("账户 {AccountId} 认证失败 (401)，正在禁用该账户", account.Id);
                                await aiAccountService.DisableAccount(account.Id);

                                lastErrorMessage = $"账户 {account.Id} 认证失败（尝试 {attempt}/{maxRetries}）";
                                lastStatusCode = HttpStatusCode.Unauthorized;

                                // 如果还有重试机会，尝试其他账户
                                if (attempt < maxRetries)
                                {
                                    continue;
                                }

                                // 📊 记录失败（最后一次尝试）
                                await _requestLogService.RecordFailure(
                                    logId,
                                    stopwatch,
                                    (int)response.StatusCode,
                                    lastErrorMessage);

                                break;

                            case HttpStatusCode.TooManyRequests:
                                _logger.LogWarning("账户 {AccountId} 触发限流 (429)", account.Id);

                                // 从响应头获取重试时间
                                var retryAfterSeconds = 300; // 默认5分钟
                                if (response.Headers.TryGetValues("Retry-After", out var retryAfter))
                                {
                                    if (int.TryParse(retryAfter.First(), out var parsedSeconds))
                                    {
                                        retryAfterSeconds = parsedSeconds;
                                    }
                                }

                                _quotaCache.MarkAsExhausted(account.Id, retryAfterSeconds);
                                await aiAccountService.MarkAccountAsRateLimited(account.Id, retryAfterSeconds);

                                lastErrorMessage =
                                    $"账户 {account.Id} 限流（重置时间: {retryAfterSeconds}秒，尝试 {attempt}/{maxRetries}）";
                                lastStatusCode = HttpStatusCode.TooManyRequests;

                                // 如果还有重试机会，尝试其他账户
                                if (attempt < maxRetries)
                                {
                                    continue;
                                }

                                // 📊 记录失败（最后一次尝试 - 限流）
                                await _requestLogService.RecordFailure(
                                    logId,
                                    stopwatch,
                                    (int)response.StatusCode,
                                    lastErrorMessage,
                                    isRateLimited: true,
                                    rateLimitResetSeconds: retryAfterSeconds,
                                    quotaInfo: System.Text.Json.JsonSerializer.Serialize(quotaInfo));

                                break;
                        }

                        // 大于等于400的状态码都认为是异常
                        if (response.StatusCode >= HttpStatusCode.BadRequest)
                        {
                            var error = await response.Content.ReadAsStringAsync().ConfigureAwait(false);

                            lastErrorMessage = error;
                            lastStatusCode = response.StatusCode;

                            _logger.LogError(
                                "请求失败 (账户: {AccountId}, 状态码: {StatusCode}, 尝试: {Attempt}/{MaxRetries}): {Error}",
                                account.Id,
                                response.StatusCode,
                                attempt,
                                maxRetries,
                                error);

                            // 检查是否是客户端参数错误，这类错误不应该重试
                            bool isClientError = ClientErrorKeywords.Any(keyword => error.Contains(keyword));

                            if (isClientError)
                            {
                                _logger.LogWarning(
                                    "检测到客户端请求参数错误，停止重试 (账户: {AccountId}): {Error}",
                                    account.Id,
                                    error);

                                // 📊 记录失败（客户端参数错误 - 不重试）
                                await _requestLogService.RecordFailure(
                                    logId,
                                    stopwatch,
                                    (int)response.StatusCode,
                                    error);

                                // 直接返回错误，不再重试
                                await WriteOpenAiErrorResponse(context, error, (int)response.StatusCode);
                                return;
                            }

                            // 如果还有重试机会，尝试其他账户
                            if (attempt < maxRetries)
                            {
                                continue;
                            }

                            // 📊 记录失败（最后一次尝试 - 其他错误）
                            await _requestLogService.RecordFailure(
                                logId,
                                stopwatch,
                                (int)response.StatusCode,
                                error);

                            break;
                        }

                        // ✅ 成功响应：流式传输内容
                        _logger.LogInformation(
                            "成功处理请求 (账户: {AccountId}, 模型: {Model}, 尝试: {Attempt}/{MaxRetries})",
                            account.Id,
                            request.Model,
                            attempt,
                            maxRetries);

                        // 更新会话粘性映射（如果有 conversationId）
                        if (!string.IsNullOrEmpty(conversationId))
                        {
                            _quotaCache.SetConversationAccount(conversationId, account.Id);
                        }

                        // 📊 记录成功（在开始流式传输前记录）
                        await _requestLogService.RecordSuccess(
                            logId,
                            stopwatch,
                            (int)response.StatusCode,
                            timeToFirstByteMs: stopwatch.ElapsedMilliseconds, // 首字节时间
                            quotaInfo: System.Text.Json.JsonSerializer.Serialize(quotaInfo));

                        // 开始写入响应Body（此后不能重试）
                        try
                        {
                            await using var stream = await response.Content.ReadAsStreamAsync(context.RequestAborted);

                            // 设置 SSE 响应头
                            context.Response.StatusCode = 200;
                            context.Response.ContentType = "text/event-stream;charset=utf-8;";
                            context.Response.Headers.TryAdd("Cache-Control", "no-cache");
                            context.Response.Headers.TryAdd("Connection", "keep-alive");
                            context.Response.Headers.TryAdd("X-Accel-Buffering", "no");

                            // 立即刷新响应头，确保客户端收到 SSE 连接确认
                            await context.Response.Body.FlushAsync(context.RequestAborted);

                            // 逐块读取和写入数据（使用 ArrayPool 避免重复分配）
                            var bufferSize = 8192; // 8KB 缓冲区
                            var buffer = System.Buffers.ArrayPool<byte>.Shared.Rent(bufferSize);
                            try
                            {
                                int bytesRead;
                                while ((bytesRead = await stream.ReadAsync(buffer.AsMemory(0, bufferSize))) > 0)
                                {
                                    await context.Response.Body.WriteAsync(buffer.AsMemory(0, bytesRead));
                                    await context.Response.Body.FlushAsync();
                                }
                            }
                            finally
                            {
                                System.Buffers.ArrayPool<byte>.Shared.Return(buffer);
                            }
                        }
                        catch (OperationCanceledException)
                        {
                            // 客户端取消请求（正常情况）
                            _logger.LogInformation("客户端取消请求 (账户: {AccountId})", account.Id);
                            return;
                        }
                        catch (Exception streamEx)
                        {
                            // 流式传输过程中的异常
                            _logger.LogError(streamEx,
                                "流式传输过程中发生异常 (账户: {AccountId})，客户端可能已断开连接",
                                account.Id);

                            // 注意：此时日志已经记录为"成功"，因为响应头已发送
                            // 流式传输中断通常是客户端断开，不需要更新日志状态
                            // 如果需要记录传输失败，可以在这里添加额外的日志
                            return;
                        }

                        // 成功返回
                        return;
                    }
                    else
                    {
                        HttpResponseMessage response;
                        try
                        {
                            response = await HttpClientFactory.GetHttpClient(address, null)
                                .HttpRequestRaw(address.TrimEnd('/') + "/responses", request, headers);
                        }
                        catch (Exception ex)
                        {
                            lastErrorMessage = $"请求异常 (账户: {account.Id}): {ex.Message}";
                            lastStatusCode = HttpStatusCode.InternalServerError;

                            _logger.LogError(ex, "账户 {AccountId} 请求异常（尝试 {Attempt}/{MaxRetries}）",
                                account.Id, attempt, maxRetries);

                            // 如果还有重试机会，继续下一次尝试
                            if (attempt < maxRetries)
                            {
                                continue;
                            }

                            // 📊 记录失败（最后一次尝试）
                            await _requestLogService.RecordFailure(
                                logId,
                                stopwatch,
                                (int)lastStatusCode,
                                lastErrorMessage);

                            break;
                        }

                        // ✅ 提取并更新配额信息（无论响应状态如何都尝试提取）
                        var quotaInfo = AccountQuotaCacheService.ExtractFromHeaders(account.Id, response.Headers);
                        if (quotaInfo != null)
                        {
                            _quotaCache.UpdateQuota(quotaInfo);

                            // 如果达到配额上限，更新数据库
                            if (quotaInfo.IsQuotaExhausted())
                            {
                                _logger.LogWarning(
                                    "账户 {AccountId} 配额已耗尽: {Status}",
                                    account.Id,
                                    quotaInfo.GetStatusDescription());

                                await aiAccountService.MarkAccountAsRateLimited(
                                    account.Id,
                                    quotaInfo.PrimaryResetAfterSeconds);
                            }
                        }

                        // ✅ 处理不同的HTTP状态码
                        switch (response.StatusCode)
                        {
                            case HttpStatusCode.Unauthorized:
                            case HttpStatusCode.Forbidden:
                                _logger.LogError("账户 {AccountId} 认证失败 (401)，正在禁用该账户", account.Id);
                                await aiAccountService.DisableAccount(account.Id);

                                lastErrorMessage = $"账户 {account.Id} 认证失败（尝试 {attempt}/{maxRetries}）";
                                lastStatusCode = HttpStatusCode.Unauthorized;

                                // 如果还有重试机会，尝试其他账户
                                if (attempt < maxRetries)
                                {
                                    continue;
                                }

                                // 📊 记录失败（最后一次尝试）
                                await _requestLogService.RecordFailure(
                                    logId,
                                    stopwatch,
                                    (int)response.StatusCode,
                                    lastErrorMessage);

                                break;

                            case HttpStatusCode.TooManyRequests:
                                _logger.LogWarning("账户 {AccountId} 触发限流 (429)", account.Id);

                                // 从响应头获取重试时间
                                var retryAfterSeconds = 300; // 默认5分钟
                                if (response.Headers.TryGetValues("Retry-After", out var retryAfter))
                                {
                                    if (int.TryParse(retryAfter.First(), out var parsedSeconds))
                                    {
                                        retryAfterSeconds = parsedSeconds;
                                    }
                                }

                                _quotaCache.MarkAsExhausted(account.Id, retryAfterSeconds);
                                await aiAccountService.MarkAccountAsRateLimited(account.Id, retryAfterSeconds);

                                lastErrorMessage =
                                    $"账户 {account.Id} 限流（重置时间: {retryAfterSeconds}秒，尝试 {attempt}/{maxRetries}）";
                                lastStatusCode = HttpStatusCode.TooManyRequests;

                                // 如果还有重试机会，尝试其他账户
                                if (attempt < maxRetries)
                                {
                                    continue;
                                }

                                // 📊 记录失败（最后一次尝试 - 限流）
                                await _requestLogService.RecordFailure(
                                    logId,
                                    stopwatch,
                                    (int)response.StatusCode,
                                    lastErrorMessage,
                                    isRateLimited: true,
                                    rateLimitResetSeconds: retryAfterSeconds,
                                    quotaInfo: System.Text.Json.JsonSerializer.Serialize(quotaInfo));

                                break;
                        }

                        // 大于等于400的状态码都认为是异常
                        if (response.StatusCode >= HttpStatusCode.BadRequest)
                        {
                            var error = await response.Content.ReadAsStringAsync().ConfigureAwait(false);

                            lastErrorMessage = error;
                            lastStatusCode = response.StatusCode;

                            _logger.LogError(
                                "请求失败 (账户: {AccountId}, 状态码: {StatusCode}, 尝试: {Attempt}/{MaxRetries}): {Error}",
                                account.Id,
                                response.StatusCode,
                                attempt,
                                maxRetries,
                                error);

                            // 检查是否是客户端参数错误，这类错误不应该重试
                            bool isClientError = ClientErrorKeywords.Any(keyword => error.Contains(keyword));

                            if (isClientError)
                            {
                                _logger.LogWarning(
                                    "检测到客户端请求参数错误，停止重试 (账户: {AccountId}): {Error}",
                                    account.Id,
                                    error);

                                // 📊 记录失败（客户端参数错误 - 不重试）
                                await _requestLogService.RecordFailure(
                                    logId,
                                    stopwatch,
                                    (int)response.StatusCode,
                                    error);

                                // 直接返回错误，不再重试
                                await WriteOpenAiErrorResponse(context, error, (int)response.StatusCode);
                                return;
                            }

                            // 如果还有重试机会，尝试其他账户
                            if (attempt < maxRetries)
                            {
                                continue;
                            }

                            // 📊 记录失败（最后一次尝试 - 其他错误）
                            await _requestLogService.RecordFailure(
                                logId,
                                stopwatch,
                                (int)response.StatusCode,
                                error);

                            break;
                        }

                        // ✅ 成功响应：流式传输内容
                        _logger.LogInformation(
                            "成功处理请求 (账户: {AccountId}, 模型: {Model}, 尝试: {Attempt}/{MaxRetries})",
                            account.Id,
                            request.Model,
                            attempt,
                            maxRetries);

                        // 更新会话粘性映射（如果有 conversationId）
                        if (!string.IsNullOrEmpty(conversationId))
                        {
                            _quotaCache.SetConversationAccount(conversationId, account.Id);
                        }

                        // 📊 记录成功（在开始流式传输前记录）
                        await _requestLogService.RecordSuccess(
                            logId,
                            stopwatch,
                            (int)response.StatusCode,
                            timeToFirstByteMs: stopwatch.ElapsedMilliseconds, // 首字节时间
                            quotaInfo: System.Text.Json.JsonSerializer.Serialize(quotaInfo));

                        // 开始写入响应Body（此后不能重试）
                        try
                        {
                            await using var stream = await response.Content.ReadAsStreamAsync(context.RequestAborted);

                            await stream.CopyToAsync(context.Response.Body, context.RequestAborted);
                            await context.Response.Body.FlushAsync();
                        }
                        catch (OperationCanceledException)
                        {
                            // 客户端取消请求（正常情况）
                            _logger.LogInformation("客户端取消请求 (账户: {AccountId})", account.Id);
                            return;
                        }
                        catch (Exception streamEx)
                        {
                            // 流式传输过程中的异常
                            _logger.LogError(streamEx,
                                "流式传输过程中发生异常 (账户: {AccountId})，客户端可能已断开连接",
                                account.Id);
                        }

                        // 成功返回
                        return;
                    }
                }
            }

            // 所有重试都失败，返回最后一次的错误信息
            context.Response.StatusCode = (int)(lastStatusCode ?? HttpStatusCode.ServiceUnavailable);

            var finalErrorMessage = lastErrorMessage ??
                                    $"所有 {maxRetries} 次重试均失败，无法完成请求";

            _logger.LogError("所有重试失败: {ErrorMessage}", finalErrorMessage);

            // 📊 记录最终失败（所有重试都失败）
            await _requestLogService.RecordFailure(
                logId,
                stopwatch,
                context.Response.StatusCode,
                finalErrorMessage);

            await WriteOpenAiErrorResponse(context, finalErrorMessage, context.Response.StatusCode);
        }
        catch (Exception ex)
        {
            // 🔥 最外层兜底异常处理：捕获所有未处理的异常
            _logger.LogError(ex, "请求处理过程中发生未捕获的异常");

            // 📊 记录异常失败
            try
            {
                await _requestLogService.RecordFailure(
                    logId,
                    stopwatch,
                    500,
                    $"未捕获的异常: {ex.Message}\n堆栈跟踪: {ex.StackTrace}");
            }
            catch (Exception logEx)
            {
                // 记录日志失败也不应该影响异常处理
                _logger.LogError(logEx, "记录失败日志时发生异常");
            }

            // 如果响应还未开始，返回错误信息给客户端
            if (!context.Response.HasStarted)
            {
                await WriteOpenAiErrorResponse(
                    context,
                    $"服务器内部错误: {ex.Message}",
                    StatusCodes.Status500InternalServerError);
            }
        }
        finally
        {
            try
            {
                if (expiredRateLimitAccountIds.Count > 0)
                {
                    foreach (var accountId in expiredRateLimitAccountIds)
                    {
                        await aiAccountService.ClearExpiredRateLimit(accountId);
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "清理过期限流状态时发生异常");
            }

            // 确保每次请求结束后清理 AsyncLocal，避免污染后续异步链路
            AIProviderAsyncLocal.AIProviderIds.Clear();
        }
    }

    private static bool IsOpenAiTokenExpired(OpenAiOauth oauth)
    {
        if (oauth.ExpiresAt <= 0)
        {
            return false;
        }

        return oauth.ExpiresAt <= DateTimeOffset.UtcNow.ToUnixTimeSeconds();
    }

    private static bool IsOpenAiErrorJson(string payload)
    {
        if (string.IsNullOrWhiteSpace(payload))
        {
            return false;
        }

        try
        {
            using var doc = JsonDocument.Parse(payload);
            if (doc.RootElement.ValueKind != JsonValueKind.Object)
            {
                return false;
            }

            return doc.RootElement.TryGetProperty("error", out var error)
                   && error.ValueKind == JsonValueKind.Object;
        }
        catch (JsonException)
        {
            return false;
        }
    }

    private static bool TryExtractMessageFromJson(string payload, out string message)
    {
        message = string.Empty;

        if (string.IsNullOrWhiteSpace(payload))
        {
            return false;
        }

        try
        {
            using var doc = JsonDocument.Parse(payload);
            var root = doc.RootElement;

            if (root.ValueKind == JsonValueKind.String)
            {
                message = root.GetString() ?? string.Empty;
                return !string.IsNullOrWhiteSpace(message);
            }

            if (root.ValueKind != JsonValueKind.Object)
            {
                return false;
            }

            if (root.TryGetProperty("message", out var topMessage) && topMessage.ValueKind == JsonValueKind.String)
            {
                message = topMessage.GetString() ?? string.Empty;
                return !string.IsNullOrWhiteSpace(message);
            }

            if (root.TryGetProperty("detail", out var detail) && detail.ValueKind == JsonValueKind.String)
            {
                message = detail.GetString() ?? string.Empty;
                return !string.IsNullOrWhiteSpace(message);
            }

            if (!root.TryGetProperty("error", out var error))
            {
                return false;
            }

            if (error.ValueKind == JsonValueKind.String)
            {
                message = error.GetString() ?? string.Empty;
                return !string.IsNullOrWhiteSpace(message);
            }

            if (error.ValueKind != JsonValueKind.Object)
            {
                return false;
            }

            if (error.TryGetProperty("message", out var errorMessage) && errorMessage.ValueKind == JsonValueKind.String)
            {
                message = errorMessage.GetString() ?? string.Empty;
                return !string.IsNullOrWhiteSpace(message);
            }

            return false;
        }
        catch (JsonException)
        {
            return false;
        }
    }

    private static async Task WriteOpenAiErrorResponse(HttpContext context, string message, int statusCode)
    {
        context.Response.ContentType = "application/json; charset=utf-8";
        context.Response.StatusCode = statusCode;

        var trimmed = message?.Trim();
        if (!string.IsNullOrEmpty(trimmed) && IsOpenAiErrorJson(trimmed))
        {
            await context.Response.WriteAsync(trimmed);
            return;
        }

        if (!string.IsNullOrEmpty(trimmed) && TryExtractMessageFromJson(trimmed, out var extracted))
        {
            message = extracted;
        }

        var payload = new JsonObject
        {
            ["error"] = new JsonObject
            {
                ["message"] = message,
                ["type"] = "api_error",
                ["param"] = null,
                ["code"] = statusCode
            }
        };

        await context.Response.WriteAsync(payload.ToJsonString());
    }
}
