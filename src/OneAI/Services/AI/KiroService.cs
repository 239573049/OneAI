using System.Net;
using System.Diagnostics;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using System.Net.Http.Headers;
using System.Runtime.CompilerServices;
using OneAI.Constants;
using OneAI.Entities;
using OneAI.Extensions;
using OneAI.Services;
using OneAI.Services.AI.Anthropic;
using OneAI.Services.AI.Models.Dtos;
using OneAI.Services.Logging;
using OneAI.Services.KiroOAuth;
using Thor.Abstractions.Chats.Dtos;

namespace OneAI.Services.AI;

/// <summary>
/// Kiro usage limits response DTO
/// </summary>
public class KiroUsageLimitsResponse
{
    [JsonPropertyName("usageBreakdownList")]
    public List<KiroUsageBreakdown>? UsageBreakdownList { get; set; }
}

/// <summary>
/// Kiro usage breakdown item
/// </summary>
public class KiroUsageBreakdown
{
    [JsonPropertyName("displayName")] public string? DisplayName { get; set; }

    [JsonPropertyName("displayNamePlural")]
    public string? DisplayNamePlural { get; set; }

    [JsonPropertyName("currentUsage")] public double CurrentUsage { get; set; }

    [JsonPropertyName("currentUsageWithPrecision")]
    public double CurrentUsageWithPrecision { get; set; }

    [JsonPropertyName("usageLimit")] public double UsageLimit { get; set; }

    [JsonPropertyName("usageLimitWithPrecision")]
    public double UsageLimitWithPrecision { get; set; }

    [JsonPropertyName("nextDateReset")] public double NextDateReset { get; set; }

    [JsonPropertyName("freeTrialInfo")] public KiroFreeTrialInfo? FreeTrialInfo { get; set; }
}

/// <summary>
/// Kiro free trial info
/// </summary>
public class KiroFreeTrialInfo
{
    [JsonPropertyName("freeTrialStatus")] public string? FreeTrialStatus { get; set; }

    [JsonPropertyName("currentUsage")] public double CurrentUsage { get; set; }

    [JsonPropertyName("currentUsageWithPrecision")]
    public double CurrentUsageWithPrecision { get; set; }

    [JsonPropertyName("usageLimit")] public double UsageLimit { get; set; }

    [JsonPropertyName("usageLimitWithPrecision")]
    public double UsageLimitWithPrecision { get; set; }

    [JsonPropertyName("freeTrialExpiry")] public double FreeTrialExpiry { get; set; }
}

/// <summary>
/// Kiro Reverse API service (Amazon CodeWhisperer via Kiro)
/// 集成了类似 Claude API 的 Prompt Caching (cache_control -> cachePoint) 功能
/// </summary>
public sealed class KiroService(
    ILogger<KiroService> logger,
    KiroOAuthService kiroOAuthService,
    AIRequestLogService requestLogService)
{
    private static readonly HttpClient HttpClient = CreateHttpClient();

    private const int MaxRetries = 3;
    private const string DefaultModelName = "claude-opus-4-5";
    private const string AuthMethodSocial = "social";
    private const string ChatTriggerManual = "MANUAL";
    private const string OriginAiEditor = "AI_EDITOR";
    private const string KiroVersion = "0.7.5";

    private static readonly Dictionary<string, string> ModelMapping = new(StringComparer.OrdinalIgnoreCase)
    {
        ["claude-opus-4-5"] = "claude-opus-4.5",
        ["claude-opus-4-5-20251101"] = "claude-opus-4.5",
        ["claude-haiku-4-5"] = "claude-haiku-4.5",
        ["claude-haiku-4-5-20251001"] = "claude-haiku-4.5",
        ["claude-sonnet-4-5"] = "CLAUDE_SONNET_4_5_20250929_V1_0",
        ["claude-sonnet-4-5-20250929"] = "CLAUDE_SONNET_4_5_20250929_V1_0",
        ["claude-sonnet-4-20250514"] = "CLAUDE_SONNET_4_20250514_V1_0",
        ["claude-3-7-sonnet-20250219"] = "CLAUDE_3_7_SONNET_20250219_V1_0"
    };

    public async Task ExecuteMessagesAsync(
        HttpContext context,
        AnthropicInput input,
        AIAccountService aiAccountService)
    {
        if (string.IsNullOrWhiteSpace(input.Model) || input.MaxTokens == null || input.Messages == null)
        {
            await WriteAnthropicError(context, StatusCodes.Status400BadRequest,
                "缺少必填字段：model / max_tokens / messages", "invalid_request_error");
            return;
        }

        AIProviderAsyncLocal.AIProviderIds = [];
        var (logId, stopwatch) = await requestLogService.CreateRequestLog(
            context,
            input.Model,
            input.Stream,
            null,
            false);

        var tools = BuildToolSpecificationsFromAnthropic(input.Tools);
        var systemPrompt = BuildAnthropicSystemPrompt(input);
        var messages = ConvertAnthropicMessages(input);
        var requestModel = ResolveKiroModel(input.Model);
        var inputTokens = EstimateInputTokens(systemPrompt, messages, tools);
        HttpStatusCode? lastStatusCode = null;
        string? lastErrorMessage = null;

        try
        {
            for (var attempt = 1; attempt <= MaxRetries; attempt++)
            {
                var account = await aiAccountService.GetAIAccountByProvider(AIProviders.Kiro);
                if (account == null)
                {
                    var statusCode = (int)(lastStatusCode ?? HttpStatusCode.ServiceUnavailable);
                    var message = lastErrorMessage ?? "Kiro 账户池暂无可用账户";
                    await requestLogService.RecordFailure(
                        logId,
                        stopwatch,
                        statusCode,
                        message);
                    await WriteAnthropicError(context, statusCode, message, "api_error");
                    return;
                }

                AIProviderAsyncLocal.AIProviderIds.Add(account.Id);
                await requestLogService.UpdateRetry(logId, attempt, account.Id);

                var credentials = account.GetKiroOauth();
                if (credentials == null)
                {
                    lastErrorMessage = $"账户 {account.Id} 缺少有效凭证";
                    lastStatusCode = HttpStatusCode.Unauthorized;
                    await aiAccountService.DisableAccount(account.Id);
                    if (attempt < MaxRetries)
                    {
                        continue;
                    }

                    break;
                }

                if (IsExpiryDateNear(credentials))
                {
                    var (refreshed, refreshError) = await TryRefreshKiroTokenAsync(account, credentials);
                    if (refreshed)
                    {
                        credentials = account.GetKiroOauth();
                    }
                    else if (!string.IsNullOrWhiteSpace(refreshError))
                    {
                        logger.LogWarning(
                            "Kiro Token 预刷新失败 (账户: {AccountId}): {Error}",
                            account.Id,
                            refreshError);
                    }
                }

                if (string.IsNullOrWhiteSpace(credentials?.AccessToken))
                {
                    lastErrorMessage = $"账户 {account.Id} 缺少访问令牌";
                    lastStatusCode = HttpStatusCode.Unauthorized;
                    await aiAccountService.DisableAccount(account.Id);
                    if (attempt < MaxRetries)
                    {
                        continue;
                    }

                    break;
                }

                var requestBody = BuildCodeWhispererRequest(messages, input.Model, tools, systemPrompt, credentials);
                var region = BuildRegion(credentials);
                var requestUrl = requestModel.StartsWith("amazonq", StringComparison.OrdinalIgnoreCase)
                    ? BuildAmazonQUrl(region)
                    : BuildBaseUrl(region);

                HttpResponseMessage response;
                try
                {
                    response = await SendKiroRequestAsync(
                        credentials,
                        requestBody,
                        requestUrl,
                        input.Stream,
                        context.RequestAborted);
                }
                catch (Exception ex)
                {
                    logger.LogError(ex,
                        "Kiro 请求异常 (账户: {AccountId}, 尝试: {Attempt}/{MaxRetries})",
                        account.Id,
                        attempt,
                        MaxRetries);
                    lastErrorMessage = $"Kiro 请求异常: {ex.Message}";
                    lastStatusCode = HttpStatusCode.BadGateway;
                    if (attempt < MaxRetries)
                    {
                        continue;
                    }

                    break;
                }

                if (response.StatusCode == HttpStatusCode.Unauthorized)
                {
                    response.Dispose();
                    var (refreshed, refreshError) = await TryRefreshKiroTokenAsync(account, credentials);
                    if (!refreshed)
                    {
                        lastErrorMessage = string.IsNullOrWhiteSpace(refreshError)
                            ? $"账户 {account.Id} Token 刷新失败"
                            : $"账户 {account.Id} Token 刷新失败: {refreshError}";
                        lastStatusCode = HttpStatusCode.Unauthorized;
                        await aiAccountService.DisableAccount(account.Id);
                        if (attempt < MaxRetries)
                        {
                            continue;
                        }

                        break;
                    }

                    credentials = account.GetKiroOauth();
                    if (credentials == null || string.IsNullOrWhiteSpace(credentials.AccessToken))
                    {
                        lastErrorMessage = $"账户 {account.Id} Token 刷新后仍无效";
                        lastStatusCode = HttpStatusCode.Unauthorized;
                        await aiAccountService.DisableAccount(account.Id);
                        if (attempt < MaxRetries)
                        {
                            continue;
                        }

                        break;
                    }

                    requestBody = BuildCodeWhispererRequest(messages, input.Model, tools, systemPrompt, credentials);
                    region = BuildRegion(credentials);
                    requestUrl = requestModel.StartsWith("amazonq", StringComparison.OrdinalIgnoreCase)
                        ? BuildAmazonQUrl(region)
                        : BuildBaseUrl(region);

                    try
                    {
                        response = await SendKiroRequestAsync(
                            credentials,
                            requestBody,
                            requestUrl,
                            input.Stream,
                            context.RequestAborted);
                    }
                    catch (Exception ex)
                    {
                        logger.LogError(ex,
                            "Kiro 请求异常 (账户: {AccountId}, 尝试: {Attempt}/{MaxRetries})",
                            account.Id,
                            attempt,
                            MaxRetries);
                        lastErrorMessage = $"Kiro 请求异常: {ex.Message}";
                        lastStatusCode = HttpStatusCode.BadGateway;
                        if (attempt < MaxRetries)
                        {
                            continue;
                        }

                        break;
                    }
                }

                if (!response.IsSuccessStatusCode)
                {
                    var error = await response.Content.ReadAsStringAsync(context.RequestAborted);
                    response.Dispose();

                    lastErrorMessage = string.IsNullOrWhiteSpace(error)
                        ? $"Kiro 请求失败 (状态码: {(int)response.StatusCode})"
                        : error;
                    lastStatusCode = response.StatusCode;

                    if (response.StatusCode is HttpStatusCode.Unauthorized
                        or HttpStatusCode.Forbidden
                        or HttpStatusCode.PaymentRequired)
                    {
                        await aiAccountService.DisableAccount(account.Id);
                    }

                    var shouldRetry = response.StatusCode == HttpStatusCode.TooManyRequests
                                      || response.StatusCode is HttpStatusCode.Unauthorized
                                          or HttpStatusCode.Forbidden
                                          or HttpStatusCode.PaymentRequired
                                      || (int)response.StatusCode >= 500;

                    if (attempt < MaxRetries && shouldRetry)
                    {
                        continue;
                    }

                    break;
                }

                if (input.Stream)
                {
                    var (streamOutputTokens, streamTimeToFirstByteMs) = await StreamAnthropicResponse(
                        context,
                        response,
                        input.Model,
                        inputTokens,
                        stopwatch);
                    await requestLogService.RecordSuccess(
                        logId,
                        stopwatch,
                        StatusCodes.Status200OK,
                        timeToFirstByteMs: streamTimeToFirstByteMs,
                        promptTokens: inputTokens,
                        completionTokens: streamOutputTokens,
                        totalTokens: inputTokens + streamOutputTokens);
                    return;
                }

                var responseText = await response.Content.ReadAsStringAsync(context.RequestAborted);
                response.Dispose();
                var (content, toolCalls) = ParseKiroResponse(responseText);

                var outputTokens = toolCalls.Count > 0
                    ? toolCalls.Sum(call => CountTokens(call.Arguments))
                    : CountTokens(content);
                await requestLogService.RecordSuccess(
                    logId,
                    stopwatch,
                    StatusCodes.Status200OK,
                    timeToFirstByteMs: stopwatch.ElapsedMilliseconds,
                    promptTokens: inputTokens,
                    completionTokens: outputTokens,
                    totalTokens: inputTokens + outputTokens);

                var responsePayload = BuildAnthropicResponse(content, toolCalls, input.Model, inputTokens);
                context.Response.ContentType = "application/json";
                await context.Response.WriteAsync(responsePayload.ToJsonString(JsonOptions.DefaultOptions));
                return;
            }

            var finalStatus = (int)(lastStatusCode ?? HttpStatusCode.ServiceUnavailable);
            var finalMessage = lastErrorMessage ?? "Kiro 账户池暂无可用账户";
            await requestLogService.RecordFailure(
                logId,
                stopwatch,
                finalStatus,
                finalMessage);
            await WriteAnthropicError(context, finalStatus, finalMessage, "api_error");
        }
        catch (OperationCanceledException)
        {
            logger.LogInformation("客户端取消 /kiro/v1/messages 请求");
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Kiro /v1/messages 处理失败");
            if (!context.Response.HasStarted)
            {
                await requestLogService.RecordFailure(
                    logId,
                    stopwatch,
                    StatusCodes.Status500InternalServerError,
                    $"服务器内部错误: {ex.Message}");
                await WriteAnthropicError(context, StatusCodes.Status500InternalServerError,
                    $"服务器内部错误: {ex.Message}", "api_error");
            }
            else
            {
                await requestLogService.RecordFailure(
                    logId,
                    stopwatch,
                    StatusCodes.Status500InternalServerError,
                    $"服务器内部错误: {ex.Message}");
            }
        }
        finally
        {
            AIProviderAsyncLocal.AIProviderIds.Clear();
        }
    }

    public async Task ExecuteChatCompletionsAsync(
        HttpContext context,
        ThorChatCompletionsRequest request,
        AIAccountService aiAccountService)
    {
        if (string.IsNullOrWhiteSpace(request.Model) || request.Messages == null)
        {
            await WriteOpenAIErrorResponse(context, "缺少必填字段：model / messages", 400);
            return;
        }

        AIProviderAsyncLocal.AIProviderIds = [];

        var (logId, stopwatch) = await requestLogService.CreateRequestLog(
            context,
            request.Model,
            request.Stream == true,
            null,
            false);

        var (systemPrompt, messages) = ConvertOpenAiMessages(request.Messages);
        var tools = BuildToolSpecificationsFromOpenAi(request.Tools);
        var requestModel = ResolveKiroModel(request.Model);
        var inputTokens = EstimateInputTokens(systemPrompt, messages, tools);
        HttpStatusCode? lastStatusCode = null;
        string? lastErrorMessage = null;

        try
        {
            for (var attempt = 1; attempt <= MaxRetries; attempt++)
            {
                var account = await aiAccountService.GetAIAccountByProvider(AIProviders.Kiro);
                if (account == null)
                {
                    var statusCode = (int)(lastStatusCode ?? HttpStatusCode.ServiceUnavailable);
                    var message = lastErrorMessage ?? "Kiro 账户池暂无可用账户";
                    await requestLogService.RecordFailure(
                        logId,
                        stopwatch,
                        statusCode,
                        message);
                    await WriteOpenAIErrorResponse(context, message, statusCode);
                    return;
                }

                AIProviderAsyncLocal.AIProviderIds.Add(account.Id);
                await requestLogService.UpdateRetry(logId, attempt, account.Id);

                var credentials = account.GetKiroOauth();
                if (credentials == null)
                {
                    lastErrorMessage = $"账户 {account.Id} 缺少有效凭证";
                    lastStatusCode = HttpStatusCode.Unauthorized;
                    await aiAccountService.DisableAccount(account.Id);
                    if (attempt < MaxRetries)
                    {
                        continue;
                    }

                    break;
                }

                if (IsExpiryDateNear(credentials))
                {
                    var (refreshed, refreshError) = await TryRefreshKiroTokenAsync(account, credentials);
                    if (refreshed)
                    {
                        credentials = account.GetKiroOauth();
                    }
                    else if (!string.IsNullOrWhiteSpace(refreshError))
                    {
                        logger.LogWarning(
                            "Kiro Token 预刷新失败 (账户: {AccountId}): {Error}",
                            account.Id,
                            refreshError);
                    }
                }

                if (string.IsNullOrWhiteSpace(credentials?.AccessToken))
                {
                    lastErrorMessage = $"账户 {account.Id} 缺少访问令牌";
                    lastStatusCode = HttpStatusCode.Unauthorized;
                    await aiAccountService.DisableAccount(account.Id);
                    if (attempt < MaxRetries)
                    {
                        continue;
                    }

                    break;
                }

                var requestBody = BuildCodeWhispererRequest(messages, request.Model, tools, systemPrompt, credentials);
                var region = BuildRegion(credentials);
                var requestUrl = requestModel.StartsWith("amazonq", StringComparison.OrdinalIgnoreCase)
                    ? BuildAmazonQUrl(region)
                    : BuildBaseUrl(region);

                HttpResponseMessage response;
                try
                {
                    response = await SendKiroRequestAsync(
                        credentials,
                        requestBody,
                        requestUrl,
                        request.Stream == true,
                        context.RequestAborted);
                }
                catch (Exception ex)
                {
                    logger.LogError(ex,
                        "Kiro 请求异常 (账户: {AccountId}, 尝试: {Attempt}/{MaxRetries})",
                        account.Id,
                        attempt,
                        MaxRetries);
                    lastErrorMessage = $"Kiro 请求异常: {ex.Message}";
                    lastStatusCode = HttpStatusCode.BadGateway;
                    if (attempt < MaxRetries)
                    {
                        continue;
                    }

                    break;
                }

                if (response.StatusCode == HttpStatusCode.Unauthorized)
                {
                    response.Dispose();
                    var (refreshed, refreshError) = await TryRefreshKiroTokenAsync(account, credentials);
                    if (!refreshed)
                    {
                        lastErrorMessage = string.IsNullOrWhiteSpace(refreshError)
                            ? $"账户 {account.Id} Token 刷新失败"
                            : $"账户 {account.Id} Token 刷新失败: {refreshError}";
                        lastStatusCode = HttpStatusCode.Unauthorized;
                        await aiAccountService.DisableAccount(account.Id);
                        if (attempt < MaxRetries)
                        {
                            continue;
                        }

                        break;
                    }

                    credentials = account.GetKiroOauth();
                    if (credentials == null || string.IsNullOrWhiteSpace(credentials.AccessToken))
                    {
                        lastErrorMessage = $"账户 {account.Id} Token 刷新后仍无效";
                        lastStatusCode = HttpStatusCode.Unauthorized;
                        await aiAccountService.DisableAccount(account.Id);
                        if (attempt < MaxRetries)
                        {
                            continue;
                        }

                        break;
                    }

                    requestBody = BuildCodeWhispererRequest(messages, request.Model, tools, systemPrompt, credentials);
                    region = BuildRegion(credentials);
                    requestUrl = requestModel.StartsWith("amazonq", StringComparison.OrdinalIgnoreCase)
                        ? BuildAmazonQUrl(region)
                        : BuildBaseUrl(region);

                    try
                    {
                        response = await SendKiroRequestAsync(
                            credentials,
                            requestBody,
                            requestUrl,
                            request.Stream == true,
                            context.RequestAborted);
                    }
                    catch (Exception ex)
                    {
                        logger.LogError(ex,
                            "Kiro 请求异常 (账户: {AccountId}, 尝试: {Attempt}/{MaxRetries})",
                            account.Id,
                            attempt,
                            MaxRetries);
                        lastErrorMessage = $"Kiro 请求异常: {ex.Message}";
                        lastStatusCode = HttpStatusCode.BadGateway;
                        if (attempt < MaxRetries)
                        {
                            continue;
                        }

                        break;
                    }
                }

                if (!response.IsSuccessStatusCode)
                {
                    var error = await response.Content.ReadAsStringAsync(context.RequestAborted);
                    response.Dispose();

                    lastErrorMessage = string.IsNullOrWhiteSpace(error)
                        ? $"Kiro 请求失败 (状态码: {(int)response.StatusCode})"
                        : error;
                    lastStatusCode = response.StatusCode;

                    if (response.StatusCode is HttpStatusCode.Unauthorized
                        or HttpStatusCode.Forbidden
                        or HttpStatusCode.PaymentRequired)
                    {
                        await aiAccountService.DisableAccount(account.Id);
                    }

                    var shouldRetry = response.StatusCode == HttpStatusCode.TooManyRequests
                                      || response.StatusCode is HttpStatusCode.Unauthorized
                                          or HttpStatusCode.Forbidden
                                          or HttpStatusCode.PaymentRequired
                                      || (int)response.StatusCode >= 500;

                    if (attempt < MaxRetries && shouldRetry)
                    {
                        continue;
                    }

                    break;
                }

                if (request.Stream == true)
                {
                    var (streamOutputTokens, streamTimeToFirstByteMs) = await StreamOpenAiResponse(
                        context,
                        response,
                        request.Model,
                        inputTokens,
                        stopwatch);
                    await requestLogService.RecordSuccess(
                        logId,
                        stopwatch,
                        StatusCodes.Status200OK,
                        timeToFirstByteMs: streamTimeToFirstByteMs,
                        promptTokens: inputTokens,
                        completionTokens: streamOutputTokens,
                        totalTokens: inputTokens + streamOutputTokens);
                    return;
                }

                var responseText = await response.Content.ReadAsStringAsync(context.RequestAborted);
                response.Dispose();
                var (content, toolCalls) = ParseKiroResponse(responseText);

                var outputTokens = CountTokens(content) + toolCalls.Sum(call => CountTokens(call.Arguments));
                await requestLogService.RecordSuccess(
                    logId,
                    stopwatch,
                    StatusCodes.Status200OK,
                    timeToFirstByteMs: stopwatch.ElapsedMilliseconds,
                    promptTokens: inputTokens,
                    completionTokens: outputTokens,
                    totalTokens: inputTokens + outputTokens);

                var responsePayload = BuildOpenAiResponse(content, toolCalls, request.Model, inputTokens);
                context.Response.ContentType = "application/json; charset=utf-8";
                await context.Response.WriteAsync(responsePayload.ToJsonString(JsonOptions.DefaultOptions));
                return;
            }

            var finalStatus = (int)(lastStatusCode ?? HttpStatusCode.ServiceUnavailable);
            var finalMessage = lastErrorMessage ?? "Kiro 账户池暂无可用账户";
            await requestLogService.RecordFailure(
                logId,
                stopwatch,
                finalStatus,
                finalMessage);
            await WriteOpenAIErrorResponse(context, finalMessage, finalStatus);
        }
        catch (OperationCanceledException)
        {
            logger.LogInformation("客户端取消 /kiro/v1/chat/completions 请求");
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Kiro /v1/chat/completions 处理失败");
            if (!context.Response.HasStarted)
            {
                await requestLogService.RecordFailure(
                    logId,
                    stopwatch,
                    StatusCodes.Status500InternalServerError,
                    $"服务器内部错误: {ex.Message}");
                await WriteOpenAIErrorResponse(context, $"服务器内部错误: {ex.Message}", 500);
            }
            else
            {
                await requestLogService.RecordFailure(
                    logId,
                    stopwatch,
                    StatusCodes.Status500InternalServerError,
                    $"服务器内部错误: {ex.Message}");
            }
        }
        finally
        {
            AIProviderAsyncLocal.AIProviderIds.Clear();
        }
    }

    private static HttpClient CreateHttpClient()
    {
        var handler = new SocketsHttpHandler
        {
            AutomaticDecompression = DecompressionMethods.All,
            PooledConnectionLifetime = TimeSpan.FromMinutes(5),
            AllowAutoRedirect = false
        };

        return new HttpClient(handler)
        {
            Timeout = TimeSpan.FromMinutes(10)
        };
    }

    private static string ResolveKiroModel(string model)
    {
        return ModelMapping.ContainsKey(model) ? model : DefaultModelName;
    }

    private static string ResolveCodewhispererModel(string model)
    {
        return ModelMapping.TryGetValue(model, out var mapped)
            ? mapped
            : ModelMapping[DefaultModelName];
    }

    private static string BuildRegion(KiroOAuthCredentialsDto credentials)
    {
        return string.IsNullOrWhiteSpace(credentials.Region)
            ? "us-east-1"
            : credentials.Region.Trim();
    }

    private static string BuildBaseUrl(string region)
    {
        return $"https://codewhisperer.{region}.amazonaws.com/generateAssistantResponse";
    }

    private static string BuildAmazonQUrl(string region)
    {
        return $"https://codewhisperer.{region}.amazonaws.com/SendMessageStreaming";
    }

    private async Task<HttpResponseMessage> SendKiroRequestAsync(
        KiroOAuthCredentialsDto credentials,
        JsonObject requestBody,
        string requestUrl,
        bool stream,
        CancellationToken cancellationToken)
    {
        var body = requestBody.ToJsonString(JsonOptions.DefaultOptions);

        using var request = new HttpRequestMessage(HttpMethod.Post, requestUrl)
        {
            Content = new StringContent(body, Encoding.UTF8, "application/json")
        };

        ApplyKiroHeaders(request, credentials);

        return await HttpClient.SendAsync(
            request,
            stream ? HttpCompletionOption.ResponseHeadersRead : HttpCompletionOption.ResponseContentRead,
            cancellationToken);
    }

    private async Task<(bool Refreshed, string? ErrorMessage)> TryRefreshKiroTokenAsync(
        AIAccount account,
        KiroOAuthCredentialsDto credentials)
    {
        if (string.IsNullOrWhiteSpace(credentials.RefreshToken))
        {
            return (false, "缺少 refresh token");
        }

        var authMethod = string.IsNullOrWhiteSpace(credentials.AuthMethod)
            ? AuthMethodSocial
            : credentials.AuthMethod.Trim();

        if (!string.Equals(authMethod, AuthMethodSocial, StringComparison.OrdinalIgnoreCase)
            && (string.IsNullOrWhiteSpace(credentials.ClientId)
                || string.IsNullOrWhiteSpace(credentials.ClientSecret)))
        {
            return (false, "缺少 clientId 或 clientSecret");
        }

        try
        {
            await kiroOAuthService.RefreshKiroOAuthTokenAsync(account);
            return (true, null);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Kiro Token 刷新失败 (账户: {AccountId})", account.Id);
            return (false, "token 刷新失败");
        }
    }

    private async Task<(int OutputTokens, long? TimeToFirstByteMs)> StreamAnthropicResponse(
        HttpContext context,
        HttpResponseMessage response,
        string responseModel,
        int inputTokens,
        Stopwatch stopwatch)
    {
        var responseStarted = false;

        async Task EnsureStreamStartedAsync()
        {
            if (responseStarted)
            {
                return;
            }

            responseStarted = true;
            context.Response.ContentType = "text/event-stream;charset=utf-8";
            context.Response.Headers.TryAdd("Cache-Control", "no-cache");
            context.Response.Headers.TryAdd("Connection", "keep-alive");
            context.Response.StatusCode = 200;

            var messageId = Guid.NewGuid().ToString();
            var messageStart = new JsonObject
            {
                ["type"] = "message_start",
                ["message"] = new JsonObject
                {
                    ["id"] = messageId,
                    ["type"] = "message",
                    ["role"] = "assistant",
                    ["model"] = responseModel,
                    ["usage"] = new JsonObject
                    {
                        ["input_tokens"] = inputTokens,
                        ["cache_creation_input_tokens"] = 0,
                        ["cache_read_input_tokens"] = 0,
                        ["output_tokens"] = 0
                    },
                    ["content"] = new JsonArray()
                }
            };

            await WriteSseJsonAsync(context, messageStart, context.RequestAborted);

            var contentBlockStart = new JsonObject
            {
                ["type"] = "content_block_start",
                ["index"] = 0,
                ["content_block"] = new JsonObject
                {
                    ["type"] = "text",
                    ["text"] = string.Empty
                }
            };

            await WriteSseJsonAsync(context, contentBlockStart, context.RequestAborted);
        }

        var toolCalls = new List<KiroToolCall>();
        var currentTool = (ToolCallAccumulator?)null;
        var totalOutputTokens = 0;
        long? timeToFirstByteMs = null;
        string? lastContent = null;

        using (response)
        {
            await using var stream = await response.Content.ReadAsStreamAsync(context.RequestAborted);

            await foreach (var ev in ReadKiroEventsAsync(stream, context.RequestAborted))
            {
                timeToFirstByteMs ??= stopwatch.ElapsedMilliseconds;
                await EnsureStreamStartedAsync();
                if (ev.Type == KiroStreamEventType.Content && !string.IsNullOrEmpty(ev.Content))
                {
                    if (ev.Content == lastContent)
                    {
                        continue;
                    }

                    lastContent = ev.Content;
                    totalOutputTokens += CountTokens(ev.Content);

                    var delta = new JsonObject
                    {
                        ["type"] = "content_block_delta",
                        ["index"] = 0,
                        ["delta"] = new JsonObject
                        {
                            ["type"] = "text_delta",
                            ["text"] = ev.Content
                        }
                    };

                    await WriteSseJsonAsync(context, delta, context.RequestAborted);
                }
                else if (ev.Type == KiroStreamEventType.ToolUse && ev.ToolUse != null)
                {
                    currentTool = new ToolCallAccumulator(ev.ToolUse.ToolUseId, ev.ToolUse.Name);
                    if (!string.IsNullOrEmpty(ev.ToolUse.Input))
                    {
                        currentTool.Input.Append(ev.ToolUse.Input);
                    }

                    if (ev.ToolUse.Stop)
                    {
                        toolCalls.Add(currentTool.ToToolCall());
                        currentTool = null;
                    }
                }
                else if (ev.Type == KiroStreamEventType.ToolUseInput && currentTool != null)
                {
                    currentTool.Input.Append(ev.ToolInput);
                }
                else if (ev.Type == KiroStreamEventType.ToolUseStop && currentTool != null)
                {
                    toolCalls.Add(currentTool.ToToolCall());
                    currentTool = null;
                }
            }
        }

        if (currentTool != null)
        {
            toolCalls.Add(currentTool.ToToolCall());
        }

        await EnsureStreamStartedAsync();
        await WriteSseJsonAsync(context, new JsonObject
        {
            ["type"] = "content_block_stop",
            ["index"] = 0
        }, context.RequestAborted);

        if (toolCalls.Count > 0)
        {
            for (var i = 0; i < toolCalls.Count; i++)
            {
                var toolCall = toolCalls[i];
                var inputJson = BuildToolInputNode(toolCall.Arguments);
                await WriteSseJsonAsync(context, new JsonObject
                {
                    ["type"] = "content_block_start",
                    ["index"] = i + 1,
                    ["content_block"] = new JsonObject
                    {
                        ["type"] = "tool_use",
                        ["id"] = toolCall.Id,
                        ["name"] = toolCall.Name,
                        ["input"] = new JsonObject()
                    }
                }, context.RequestAborted);

                var partialJson = inputJson.ToJsonString(JsonOptions.DefaultOptions);

                await WriteSseJsonAsync(context, new JsonObject
                {
                    ["type"] = "content_block_delta",
                    ["index"] = i + 1,
                    ["delta"] = new JsonObject
                    {
                        ["type"] = "input_json_delta",
                        ["partial_json"] = partialJson
                    }
                }, context.RequestAborted);

                totalOutputTokens += CountTokens(partialJson);
                await WriteSseJsonAsync(context, new JsonObject
                {
                    ["type"] = "content_block_stop",
                    ["index"] = i + 1
                }, context.RequestAborted);
            }
        }

        var stopReason = toolCalls.Count > 0 ? "tool_use" : "end_turn";
        await WriteSseJsonAsync(context, new JsonObject
        {
            ["type"] = "message_delta",
            ["delta"] = new JsonObject
            {
                ["stop_reason"] = stopReason,
                ["usage"] = new JsonObject
                {
                    ["input_tokens"] = inputTokens,
                    ["cache_creation_input_tokens"] = 0,
                    ["cache_read_input_tokens"] = 0,
                    ["output_tokens"] = totalOutputTokens
                }
            },
            ["usage"] =  new JsonObject
            {
                ["input_tokens"] = inputTokens,
                ["cache_creation_input_tokens"] = 0,
                ["cache_read_input_tokens"] = 0,
                ["output_tokens"] = totalOutputTokens
            }
        }, context.RequestAborted);

        await WriteSseJsonAsync(context, new JsonObject
        {
            ["type"] = "message_stop"
        }, context.RequestAborted);

        if (timeToFirstByteMs == null)
        {
            timeToFirstByteMs = stopwatch.ElapsedMilliseconds;
        }

        return (totalOutputTokens, timeToFirstByteMs);
    }

    private async Task<(int OutputTokens, long? TimeToFirstByteMs)> StreamOpenAiResponse(
        HttpContext context,
        HttpResponseMessage response,
        string responseModel,
        int inputTokens,
        Stopwatch stopwatch)
    {
        var responseId = Guid.NewGuid().ToString();
        var created = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        var responseStarted = false;

        async Task EnsureStreamStartedAsync()
        {
            if (responseStarted)
            {
                return;
            }

            responseStarted = true;
            context.Response.ContentType = "text/event-stream;charset=utf-8";
            context.Response.Headers.TryAdd("Cache-Control", "no-cache");
            context.Response.Headers.TryAdd("Connection", "keep-alive");
            context.Response.StatusCode = 200;

            var roleChunk = new JsonObject
            {
                ["id"] = responseId,
                ["object"] = "chat.completion.chunk",
                ["created"] = created,
                ["model"] = responseModel,
                ["choices"] = new JsonArray
                {
                    new JsonObject
                    {
                        ["index"] = 0,
                        ["delta"] = new JsonObject
                        {
                            ["role"] = "assistant",
                            ["content"] = string.Empty
                        },
                        ["finish_reason"] = null
                    }
                }
            };

            await WriteSseJsonAsync(context, roleChunk, context.RequestAborted);
        }

        var toolCalls = new List<KiroToolCall>();
        var currentTool = (ToolCallAccumulator?)null;
        var totalOutputTokens = 0;
        long? timeToFirstByteMs = null;
        string? lastContent = null;

        using (response)
        {
            await using var stream = await response.Content.ReadAsStreamAsync(context.RequestAborted);

            await foreach (var ev in ReadKiroEventsAsync(stream, context.RequestAborted))
            {
                timeToFirstByteMs ??= stopwatch.ElapsedMilliseconds;
                await EnsureStreamStartedAsync();
                if (ev.Type == KiroStreamEventType.Content && !string.IsNullOrEmpty(ev.Content))
                {
                    if (ev.Content == lastContent)
                    {
                        continue;
                    }

                    lastContent = ev.Content;
                    totalOutputTokens += CountTokens(ev.Content);
                    var chunk = new JsonObject
                    {
                        ["id"] = responseId,
                        ["object"] = "chat.completion.chunk",
                        ["created"] = created,
                        ["model"] = responseModel,
                        ["choices"] = new JsonArray
                        {
                            new JsonObject
                            {
                                ["index"] = 0,
                                ["delta"] = new JsonObject
                                {
                                    ["content"] = ev.Content
                                },
                                ["finish_reason"] = null
                            }
                        }
                    };

                    await WriteSseJsonAsync(context, chunk, context.RequestAborted);
                }
                else if (ev is { Type: KiroStreamEventType.ToolUse, ToolUse: not null })
                {
                    currentTool = new ToolCallAccumulator(ev.ToolUse.ToolUseId, ev.ToolUse.Name);
                    if (!string.IsNullOrEmpty(ev.ToolUse.Input))
                    {
                        currentTool.Input.Append(ev.ToolUse.Input);
                    }

                    if (ev.ToolUse.Stop)
                    {
                        toolCalls.Add(currentTool.ToToolCall());
                        currentTool = null;
                    }
                }
                else if (ev.Type == KiroStreamEventType.ToolUseInput && currentTool != null)
                {
                    currentTool.Input.Append(ev.ToolInput);
                }
                else if (ev.Type == KiroStreamEventType.ToolUseStop && currentTool != null)
                {
                    toolCalls.Add(currentTool.ToToolCall());
                    currentTool = null;
                }
            }
        }

        if (currentTool != null)
        {
            toolCalls.Add(currentTool.ToToolCall());
        }

        if (toolCalls.Count > 0)
        {
            totalOutputTokens += toolCalls.Sum(call => CountTokens(call.Arguments));
            timeToFirstByteMs ??= stopwatch.ElapsedMilliseconds;
            await EnsureStreamStartedAsync();

            var toolCallsArray = BuildOpenAiToolCalls(toolCalls, includeIndex: true);
            var toolChunk = new JsonObject
            {
                ["id"] = responseId,
                ["object"] = "chat.completion.chunk",
                ["created"] = created,
                ["model"] = responseModel,
                ["choices"] = new JsonArray
                {
                    new JsonObject
                    {
                        ["index"] = 0,
                        ["delta"] = new JsonObject
                        {
                            ["tool_calls"] = toolCallsArray
                        },
                        ["finish_reason"] = "tool_calls"
                    }
                }
            };

            await WriteSseJsonAsync(context, toolChunk, context.RequestAborted);
        }
        else
        {
            timeToFirstByteMs ??= stopwatch.ElapsedMilliseconds;
            await EnsureStreamStartedAsync();
            var doneChunk = new JsonObject
            {
                ["id"] = responseId,
                ["object"] = "chat.completion.chunk",
                ["created"] = created,
                ["model"] = responseModel,
                ["choices"] = new JsonArray
                {
                    new JsonObject
                    {
                        ["index"] = 0,
                        ["delta"] = new JsonObject(),
                        ["finish_reason"] = "stop"
                    }
                },
                ["usage"] = new JsonObject
                {
                    ["prompt_tokens"] = inputTokens,
                    ["completion_tokens"] = 0,
                    ["total_tokens"] = inputTokens
                }
            };

            await WriteSseJsonAsync(context, doneChunk, context.RequestAborted);
        }

        await WriteSseDoneAsync(context, context.RequestAborted);

        if (timeToFirstByteMs == null)
        {
            timeToFirstByteMs = stopwatch.ElapsedMilliseconds;
        }

        return (totalOutputTokens, timeToFirstByteMs);
    }

    public async Task<KiroUsageLimitsResponse?> GetUsageLimitsAsync(AIAccount account)
    {
        var credentials = account.GetKiroOauth();
        if (credentials == null)
        {
            throw new InvalidOperationException("Kiro credentials not found");
        }

        if (IsExpiryDateNear(credentials))
        {
            await kiroOAuthService.RefreshKiroOAuthTokenAsync(account);
            credentials = account.GetKiroOauth();
        }

        if (string.IsNullOrWhiteSpace(credentials?.AccessToken))
        {
            throw new InvalidOperationException("Kiro access token is required");
        }

        var region = string.IsNullOrWhiteSpace(credentials.Region) ? "us-east-1" : credentials.Region.Trim();
        var authMethod = string.IsNullOrWhiteSpace(credentials.AuthMethod) ? "social" : credentials.AuthMethod.Trim();

        var baseUrl = $"https://q.{region}.amazonaws.com/getUsageLimits";

        var queryParams = new List<string>
        {
            "isEmailRequired=true",
            "origin=AI_EDITOR",
            "resourceType=AGENTIC_REQUEST"
        };

        if (string.Equals(authMethod, "social", StringComparison.OrdinalIgnoreCase) &&
            !string.IsNullOrWhiteSpace(credentials.ProfileArn))
        {
            queryParams.Add($"profileArn={Uri.EscapeDataString(credentials.ProfileArn)}");
        }

        var requestUrl = $"{baseUrl}?{string.Join("&", queryParams)}";

        using var request = new HttpRequestMessage(HttpMethod.Get, requestUrl);

        request.Headers.Add("Authorization", $"Bearer {credentials.AccessToken}");
        request.Headers.Add("User-Agent", $"OneAI/{KiroVersion}");
        request.Headers.Add("amz-sdk-invocation-id", Guid.NewGuid().ToString());
        request.Headers.Add("Origin", "https://app.kiro.dev");
        request.Headers.Add("Referer", "https://app.kiro.dev/");

        try
        {
            var response = await HttpClient.SendAsync(request);

            if (!response.IsSuccessStatusCode)
            {
                var error = await response.Content.ReadAsStringAsync();
                logger.LogError("Kiro usage limits request failed: {StatusCode} - {Error}",
                    response.StatusCode, error);
                return null;
            }

            var content = await response.Content.ReadAsStringAsync();
            logger.LogInformation("Kiro usage limits request succeeded");

            var result = JsonSerializer.Deserialize<KiroUsageLimitsResponse>(content, new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true
            });

            return result;
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error fetching Kiro usage limits");
            return null;
        }
    }

    private async IAsyncEnumerable<KiroStreamEvent> ReadKiroEventsAsync(
        Stream stream,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        using var reader = new StreamReader(stream, Encoding.UTF8);
        var buffer = new StringBuilder();
        var charBuffer = new char[4096];

        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var read = await reader.ReadAsync(charBuffer.AsMemory(0, charBuffer.Length), cancellationToken);
            if (read == 0)
            {
                yield break;
            }

            buffer.Append(charBuffer, 0, read);
            var (events, remaining) = ParseAwsEventStreamBuffer(buffer.ToString());
            buffer.Clear();
            buffer.Append(remaining);

            foreach (var ev in events)
            {
                yield return ev;
            }
        }
    }

    private static void ApplyKiroHeaders(HttpRequestMessage request, KiroOAuthCredentialsDto credentials)
    {
        var machineId = GenerateMachineId(credentials);
        var (osName, runtimeVersion) = GetSystemRuntimeInfo();

        request.Headers.Authorization =
            new AuthenticationHeaderValue("Bearer", credentials.AccessToken ?? string.Empty);
        request.Headers.TryAddWithoutValidation("amz-sdk-invocation-id", Guid.NewGuid().ToString());
        request.Headers.TryAddWithoutValidation("amz-sdk-request", "attempt=1; max=1");
        request.Headers.TryAddWithoutValidation("x-amzn-kiro-agent-mode", "vibe");
        request.Headers.TryAddWithoutValidation("x-amz-user-agent",
            $"aws-sdk-js/1.0.0 KiroIDE-{KiroVersion}-{machineId}");
        request.Headers.TryAddWithoutValidation("user-agent",
            $"aws-sdk-js/1.0.0 ua/2.1 os/{osName} lang/js md/dotnet#{runtimeVersion} api/codewhispererruntime#1.0.0 m/E KiroIDE-{KiroVersion}-{machineId}");
        request.Headers.ConnectionClose = true;
    }

    private static (string OsName, string RuntimeVersion) GetSystemRuntimeInfo()
    {
        var osPlatform = Environment.OSVersion.Platform.ToString().ToLowerInvariant();
        var osRelease = Environment.OSVersion.Version.ToString();
        var osName = osPlatform switch
        {
            "win32nt" => $"windows#{osRelease}",
            "unix" => $"linux#{osRelease}",
            "macosx" => $"macos#{osRelease}",
            _ => $"{osPlatform}#{osRelease}"
        };

        var runtimeVersion = Environment.Version.ToString();
        return (osName, runtimeVersion);
    }

    private static string GenerateMachineId(KiroOAuthCredentialsDto credentials)
    {
        var uniqueKey = credentials.Uuid
                        ?? credentials.ProfileArn
                        ?? credentials.ClientId
                        ?? "KIRO_DEFAULT_MACHINE";

        using var sha = SHA256.Create();
        var hash = sha.ComputeHash(Encoding.UTF8.GetBytes(uniqueKey));
        return Convert.ToHexString(hash).ToLowerInvariant();
    }

    private static bool IsExpiryDateNear(KiroOAuthCredentialsDto credentials)
    {
        var expiresAt = credentials.ExpiresAt;
        if (string.IsNullOrWhiteSpace(expiresAt))
        {
            return true;
        }

        if (!DateTimeOffset.TryParse(expiresAt, out var expiry))
        {
            return true;
        }

        var nearMinutes = 10;
        var threshold = DateTimeOffset.UtcNow.AddMinutes(nearMinutes);
        return expiry <= threshold;
    }

    private JsonObject BuildCodeWhispererRequest(
        List<KiroMessage> messages,
        string model,
        JsonArray? toolsContext,
        string? systemPrompt,
        KiroOAuthCredentialsDto credentials)
    {
        if (messages.Count == 0)
        {
            throw new InvalidOperationException("No user messages found");
        }

        var processedMessages = messages
            .Select(message => message.Clone())
            .ToList();

        if (processedMessages.Count > 0
            && processedMessages[^1].Role == "assistant"
            && processedMessages[^1].Parts.Count > 0
            && processedMessages[^1].Parts[0].Type == "text"
            && processedMessages[^1].Parts[0].Text == "{")
        {
            processedMessages.RemoveAt(processedMessages.Count - 1);
        }

        processedMessages = MergeAdjacentMessages(processedMessages);

        var codewhispererModel = ResolveCodewhispererModel(model);
        var history = new JsonArray();
        var startIndex = 0;

        if (!string.IsNullOrWhiteSpace(systemPrompt))
        {
            if (processedMessages.Count > 0 && processedMessages[0].Role == "user")
            {
                var firstContent = GetMessageText(processedMessages[0]);
                var merged = string.IsNullOrWhiteSpace(firstContent)
                    ? systemPrompt
                    : $"{systemPrompt}\n\n{firstContent}";

                var cp = processedMessages[0].Parts.FirstOrDefault(p => p.CachePoint != null)?.CachePoint;

                history.Add(new JsonObject
                {
                    ["userInputMessage"] = BuildUserInputMessage(
                        merged,
                        codewhispererModel,
                        OriginAiEditor,
                        null,
                        null,
                        null,
                        cp != null ? new JsonObject { ["type"] = cp.Type } : null)
                });
                startIndex = 1;
            }
            else
            {
                history.Add(new JsonObject
                {
                    ["userInputMessage"] = BuildUserInputMessage(
                        systemPrompt,
                        codewhispererModel,
                        OriginAiEditor,
                        null,
                        null,
                        null)
                });
            }
        }

        for (var i = startIndex; i < processedMessages.Count - 1; i++)
        {
            var message = processedMessages[i];
            if (message.Role == "user")
            {
                var cp = message.Parts.FirstOrDefault(p => p.CachePoint != null)?.CachePoint;
                var userMessage = BuildUserInputMessageFromParts(
                    message,
                    codewhispererModel,
                    includeTools: false,
                    toolsContext: null,
                    cachePoint: cp != null ? new JsonObject { ["type"] = cp.Type } : null);
                history.Add(new JsonObject { ["userInputMessage"] = userMessage });
            }
            else if (message.Role == "assistant")
            {
                var assistantMessage = BuildAssistantResponseMessage(message);
                history.Add(new JsonObject { ["assistantResponseMessage"] = assistantMessage });
            }
        }

        var currentMessage = processedMessages[^1];
        var currentContent = string.Empty;
        var currentToolResults = new List<JsonObject>();
        var currentImages = new List<JsonObject>();
        JsonObject? currentCachePoint = null;

        if (currentMessage.Role == "assistant")
        {
            var assistantMessage = BuildAssistantResponseMessage(currentMessage);
            history.Add(new JsonObject { ["assistantResponseMessage"] = assistantMessage });
            currentContent = "Continue";
        }
        else
        {
            if (history.Count > 0)
            {
                var lastHistory = history[^1] as JsonObject;
                if (lastHistory != null && !lastHistory.ContainsKey("assistantResponseMessage"))
                {
                    history.Add(new JsonObject
                    {
                        ["assistantResponseMessage"] = new JsonObject
                        {
                            ["content"] = "Continue"
                        }
                    });
                }
            }

            foreach (var part in currentMessage.Parts)
            {
                if (part.Type == "text")
                {
                    currentContent += part.Text;
                }
                else if (part.Type == "tool_result" && !string.IsNullOrWhiteSpace(part.ToolUseId))
                {
                    currentToolResults.Add(new JsonObject
                    {
                        ["content"] = new JsonArray
                        {
                            new JsonObject
                            {
                                ["text"] = part.Content ?? string.Empty
                            }
                        },
                        ["status"] = "success",
                        ["toolUseId"] = part.ToolUseId
                    });
                }
                else if (part.Type == "image" && part.Source != null)
                {
                    currentImages.Add(new JsonObject
                    {
                        ["format"] = part.Source.MediaType?.Split('/').LastOrDefault(),
                        ["source"] = new JsonObject
                        {
                            ["bytes"] = part.Source.Data ?? string.Empty
                        }
                    });
                }

                if (part.CachePoint != null)
                {
                    currentCachePoint = new JsonObject { ["type"] = part.CachePoint.Type };
                }
            }

            if (string.IsNullOrWhiteSpace(currentContent))
            {
                currentContent = currentToolResults.Count > 0 ? "Tool results provided." : "Continue";
            }
        }

        var currentUserInput = BuildUserInputMessage(
            currentContent,
            codewhispererModel,
            OriginAiEditor,
            currentImages.Count > 0 ? currentImages : null,
            currentToolResults.Count > 0 ? currentToolResults : null,
            toolsContext,
            currentCachePoint);

        var request = new JsonObject
        {
            ["conversationState"] = new JsonObject
            {
                ["chatTriggerType"] = ChatTriggerManual,
                ["conversationId"] = Guid.NewGuid().ToString(),
                ["currentMessage"] = new JsonObject
                {
                    ["userInputMessage"] = currentUserInput
                }
            }
        };

        if (history.Count > 0)
        {
            ((JsonObject)request["conversationState"]!)["history"] = history;
        }

        if (string.Equals(credentials.AuthMethod, AuthMethodSocial, StringComparison.OrdinalIgnoreCase)
            && !string.IsNullOrWhiteSpace(credentials.ProfileArn))
        {
            request["profileArn"] = credentials.ProfileArn;
        }

        return request;
    }

    private static JsonObject BuildUserInputMessage(
        string content,
        string modelId,
        string origin,
        List<JsonObject>? images,
        List<JsonObject>? toolResults,
        JsonArray? tools,
        JsonObject? cachePoint = null)
    {
        var userInputMessage = new JsonObject
        {
            ["content"] = content,
            ["modelId"] = modelId,
            ["origin"] = origin
        };

        if (cachePoint != null)
        {
            userInputMessage["cachePoint"] = cachePoint;
        }

        if (images is { Count: > 0 })
        {
            userInputMessage["images"] = new JsonArray(images.ToArray());
        }

        var context = new JsonObject();
        if (toolResults is { Count: > 0 })
        {
            context["toolResults"] = new JsonArray(toolResults.ToArray());
        }

        if (tools is { Count: > 0 })
        {
            context["tools"] = tools;
        }

        if (context.Count > 0)
        {
            userInputMessage["userInputMessageContext"] = context;
        }

        return userInputMessage;
    }

    private static JsonObject BuildUserInputMessageFromParts(
        KiroMessage message,
        string modelId,
        bool includeTools,
        JsonArray? toolsContext,
        JsonObject? cachePoint = null)
    {
        var contentBuilder = new StringBuilder();
        var toolResults = new List<JsonObject>();
        var images = new List<JsonObject>();

        foreach (var part in message.Parts)
        {
            if (part.Type == "text")
            {
                contentBuilder.Append(part.Text);
            }
            else if (part.Type == "tool_result" && !string.IsNullOrWhiteSpace(part.ToolUseId))
            {
                toolResults.Add(new JsonObject
                {
                    ["content"] = new JsonArray
                    {
                        new JsonObject
                        {
                            ["text"] = part.Content ?? string.Empty
                        }
                    },
                    ["status"] = "success",
                    ["toolUseId"] = part.ToolUseId
                });
            }
            else if (part.Type == "image" && part.Source != null)
            {
                images.Add(new JsonObject
                {
                    ["format"] = part.Source.MediaType?.Split('/').LastOrDefault(),
                    ["source"] = new JsonObject
                    {
                        ["bytes"] = part.Source.Data ?? string.Empty
                    }
                });
            }
        }

        var content = contentBuilder.ToString();
        if (string.IsNullOrWhiteSpace(content))
        {
            content = toolResults.Count > 0 ? "Tool results provided." : "Continue";
        }

        return BuildUserInputMessage(
            content,
            modelId,
            OriginAiEditor,
            images.Count > 0 ? images : null,
            toolResults.Count > 0 ? toolResults : null,
            includeTools ? toolsContext : null,
            cachePoint);
    }

    private static JsonObject BuildAssistantResponseMessage(KiroMessage message)
    {
        var contentBuilder = new StringBuilder();
        var toolUses = new List<JsonObject>();

        foreach (var part in message.Parts)
        {
            if (part.Type == "text")
            {
                contentBuilder.Append(part.Text);
            }
            else if (part.Type == "tool_use" && !string.IsNullOrWhiteSpace(part.ToolUseId))
            {
                toolUses.Add(new JsonObject
                {
                    ["input"] = part.Input ?? new JsonObject(),
                    ["name"] = part.Name ?? string.Empty,
                    ["toolUseId"] = part.ToolUseId
                });
            }
        }

        var assistant = new JsonObject
        {
            ["content"] = contentBuilder.ToString()
        };

        if (toolUses.Count > 0)
        {
            assistant["toolUses"] = new JsonArray(toolUses.ToArray());
        }

        return assistant;
    }

    private static List<KiroMessage> MergeAdjacentMessages(List<KiroMessage> messages)
    {
        var merged = new List<KiroMessage>();
        foreach (var message in messages)
        {
            if (merged.Count == 0)
            {
                merged.Add(message);
                continue;
            }

            var last = merged[^1];
            if (last.Role == message.Role)
            {
                last.Parts.AddRange(message.Parts);
            }
            else
            {
                merged.Add(message);
            }
        }

        return merged;
    }

    private static string GetMessageText(KiroMessage message)
    {
        var builder = new StringBuilder();
        foreach (var part in message.Parts)
        {
            if (part.Type == "text")
            {
                builder.Append(part.Text);
            }
        }

        return builder.ToString();
    }

    private static JsonArray? BuildToolSpecificationsFromAnthropic(IList<AnthropicMessageTool>? tools)
    {
        if (tools == null || tools.Count == 0)
        {
            return null;
        }

        var array = new JsonArray();
        foreach (var tool in tools)
        {
            if (string.IsNullOrWhiteSpace(tool.Name))
            {
                continue;
            }

            var schema = tool.InputSchema == null
                ? new JsonObject()
                : JsonSerializer.SerializeToNode(tool.InputSchema, JsonOptions.DefaultOptions) ?? new JsonObject();

            array.Add(new JsonObject
            {
                ["toolSpecification"] = new JsonObject
                {
                    ["name"] = tool.Name,
                    ["description"] = tool.Description ?? string.Empty,
                    ["inputSchema"] = new JsonObject
                    {
                        ["json"] = schema
                    }
                }
            });
        }

        return array.Count == 0 ? null : array;
    }

    private static JsonArray? BuildToolSpecificationsFromOpenAi(List<ThorToolDefinition>? tools)
    {
        if (tools == null || tools.Count == 0)
        {
            return null;
        }

        var array = new JsonArray();
        foreach (var tool in tools)
        {
            var function = tool.Function;
            if (function == null || string.IsNullOrWhiteSpace(function.Name))
            {
                continue;
            }

            var schema = function.Parameters == null
                ? new JsonObject()
                : JsonSerializer.SerializeToNode(function.Parameters, JsonOptions.DefaultOptions) ?? new JsonObject();

            array.Add(new JsonObject
            {
                ["toolSpecification"] = new JsonObject
                {
                    ["name"] = function.Name,
                    ["description"] = function.Description ?? string.Empty,
                    ["inputSchema"] = new JsonObject
                    {
                        ["json"] = schema
                    }
                }
            });
        }

        return array.Count == 0 ? null : array;
    }

    private static string? BuildAnthropicSystemPrompt(AnthropicInput input)
    {
        if (!string.IsNullOrWhiteSpace(input.System))
        {
            return input.System;
        }

        if (input.Systems == null || input.Systems.Count == 0)
        {
            return null;
        }

        var builder = new StringBuilder();
        foreach (var item in input.Systems)
        {
            if (item.Type == "text" && !string.IsNullOrWhiteSpace(item.Text))
            {
                if (builder.Length > 0)
                {
                    builder.Append('\n');
                }

                builder.Append(item.Text);
            }
        }

        return builder.Length == 0 ? null : builder.ToString();
    }

    private static List<KiroMessage> ConvertAnthropicMessages(AnthropicInput input)
    {
        var result = new List<KiroMessage>();

        foreach (var message in input.Messages)
        {
            var parts = new List<KiroMessagePart>();

            if (message.Contents != null)
            {
                foreach (var content in message.Contents)
                {
                    KiroMessagePart? part = null;
                    if (content.Type == "text")
                    {
                        part = new KiroMessagePart("text") { Text = content.Text ?? content.Content?.ToString() };
                    }
                    else if (content.Type == "tool_use")
                    {
                        var toolId = content.Id ?? content.ToolUseId;
                        part = new KiroMessagePart("tool_use")
                        {
                            ToolUseId = toolId,
                            Name = content.Name,
                            Input = content.Input != null
                                ? JsonSerializer.SerializeToNode(content.Input, JsonOptions.DefaultOptions)
                                : new JsonObject()
                        };
                    }
                    else if (content.Type == "tool_result")
                    {
                        part = new KiroMessagePart("tool_result")
                        {
                            ToolUseId = content.ToolUseId,
                            Content = content.Content?.ToString() ?? content.Text
                        };
                    }
                    else if (content.Type == "image" && content.Source != null)
                    {
                        part = new KiroMessagePart("image")
                        {
                            Source = new KiroImageSource(content.Source.MediaType, content.Source.Data)
                        };
                    }

                    if (part != null)
                    {
                        if (content.CacheControl != null)
                        {
                            part.CachePoint = new KiroCachePoint { Type = "default" };
                        }

                        parts.Add(part);
                    }
                }
            }
            else if (!string.IsNullOrWhiteSpace(message.Content))
            {
                parts.Add(new KiroMessagePart("text") { Text = message.Content });
            }

            result.Add(new KiroMessage(message.Role, parts));
        }

        return result;
    }

    private static (string? SystemPrompt, List<KiroMessage> Messages) ConvertOpenAiMessages(
        IList<ThorChatMessage> messages)
    {
        var systemBuilder = new StringBuilder();
        var result = new List<KiroMessage>();

        foreach (var message in messages)
        {
            if (message.Role == "system")
            {
                if (!string.IsNullOrWhiteSpace(message.Content))
                {
                    if (systemBuilder.Length > 0)
                    {
                        systemBuilder.Append("\n\n");
                    }

                    systemBuilder.Append(message.Content);
                }

                continue;
            }

            if (message.Role == "tool")
            {
                var toolResult = new KiroMessagePart("tool_result")
                {
                    ToolUseId = message.ToolCallId,
                    Content = message.Content ?? string.Empty
                };
                result.Add(new KiroMessage("user", new List<KiroMessagePart> { toolResult }));
                continue;
            }

            var parts = new List<KiroMessagePart>();
            if (!string.IsNullOrWhiteSpace(message.Content))
            {
                parts.Add(new KiroMessagePart("text") { Text = message.Content });
            }

            if (message.Contents != null)
            {
                foreach (var content in message.Contents)
                {
                    if (content.Type == "text")
                    {
                        parts.Add(new KiroMessagePart("text") { Text = content.Text });
                    }
                    else if (content.Type == "image_url" && content.ImageUrl?.Url != null)
                    {
                        parts.Add(new KiroMessagePart("text")
                        {
                            Text = $"[image] {content.ImageUrl.Url}"
                        });
                    }
                }
            }

            if (message.ToolCalls != null && message.ToolCalls.Count > 0)
            {
                foreach (var toolCall in message.ToolCalls)
                {
                    var arguments = toolCall.Function?.Arguments ?? "{}";
                    JsonNode? input = null;
                    try
                    {
                        input = JsonNode.Parse(arguments);
                    }
                    catch
                    {
                        input = new JsonObject { ["raw_arguments"] = arguments };
                    }

                    parts.Add(new KiroMessagePart("tool_use")
                    {
                        ToolUseId = toolCall.Id ?? Guid.NewGuid().ToString("N"),
                        Name = toolCall.Function?.Name,
                        Input = input
                    });
                }
            }

            result.Add(new KiroMessage(message.Role, parts));
        }

        return (systemBuilder.Length == 0 ? null : systemBuilder.ToString(), result);
    }

    private static int EstimateInputTokens(string? systemPrompt, List<KiroMessage> messages, JsonArray? tools)
    {
        var total = 0;
        if (!string.IsNullOrWhiteSpace(systemPrompt))
        {
            total += CountTokens(systemPrompt);
        }

        foreach (var message in messages)
        {
            total += CountTokens(GetMessageText(message));
            foreach (var part in message.Parts)
            {
                if (part.Type == "tool_use" && part.Input != null)
                {
                    total += CountTokens(part.Input.ToJsonString(JsonOptions.DefaultOptions));
                }
                else if (part.Type == "tool_result" && !string.IsNullOrWhiteSpace(part.Content))
                {
                    total += CountTokens(part.Content);
                }
            }
        }

        if (tools != null)
        {
            total += CountTokens(tools.ToJsonString(JsonOptions.DefaultOptions));
        }

        return total;
    }

    private static int CountTokens(string? text)
    {
        return text?.GetTokens() ?? 0;
    }

    private static (string Content, List<KiroToolCall> ToolCalls) ParseKiroResponse(string raw)
    {
        var contentBuilder = new StringBuilder();
        var toolCalls = new List<KiroToolCall>();
        ToolCallAccumulator? currentTool = null;

        var (events, _) = ParseAwsEventStreamBuffer(raw);
        foreach (var ev in events)
        {
            if (ev.Type == KiroStreamEventType.Content && !string.IsNullOrEmpty(ev.Content))
            {
                contentBuilder.Append(ev.Content);
            }
            else if (ev.Type == KiroStreamEventType.ToolUse && ev.ToolUse != null)
            {
                currentTool = new ToolCallAccumulator(ev.ToolUse.ToolUseId, ev.ToolUse.Name);
                if (!string.IsNullOrEmpty(ev.ToolUse.Input))
                {
                    currentTool.Input.Append(ev.ToolUse.Input);
                }

                if (ev.ToolUse.Stop)
                {
                    toolCalls.Add(currentTool.ToToolCall());
                    currentTool = null;
                }
            }
            else if (ev.Type == KiroStreamEventType.ToolUseInput && currentTool != null)
            {
                currentTool.Input.Append(ev.ToolInput);
            }
            else if (ev.Type == KiroStreamEventType.ToolUseStop && currentTool != null)
            {
                toolCalls.Add(currentTool.ToToolCall());
                currentTool = null;
            }
        }

        if (currentTool != null)
        {
            toolCalls.Add(currentTool.ToToolCall());
        }

        var bracketToolCalls = ParseBracketToolCalls(raw);
        if (bracketToolCalls.Count > 0)
        {
            toolCalls.AddRange(bracketToolCalls);
        }

        toolCalls = DeduplicateToolCalls(toolCalls);
        var cleaned = RemoveBracketToolCalls(contentBuilder.ToString(), toolCalls);

        return (cleaned, toolCalls);
    }

    private static List<KiroToolCall> ParseBracketToolCalls(string responseText)
    {
        if (string.IsNullOrWhiteSpace(responseText) || !responseText.Contains("[Called", StringComparison.Ordinal))
        {
            return new List<KiroToolCall>();
        }

        var toolCalls = new List<KiroToolCall>();
        var callPositions = new List<int>();
        var start = 0;

        while (true)
        {
            var pos = responseText.IndexOf("[Called", start, StringComparison.Ordinal);
            if (pos == -1)
            {
                break;
            }

            callPositions.Add(pos);
            start = pos + 1;
        }

        for (var i = 0; i < callPositions.Count; i++)
        {
            var startPos = callPositions[i];
            var endLimit = i + 1 < callPositions.Count ? callPositions[i + 1] : responseText.Length;
            var segment = responseText.Substring(startPos, endLimit - startPos);
            var bracketEnd = FindMatchingBracket(segment, 0);
            if (bracketEnd == -1)
            {
                var lastBracket = segment.LastIndexOf(']');
                if (lastBracket == -1)
                {
                    continue;
                }

                bracketEnd = lastBracket;
            }

            var toolCallText = segment.Substring(0, bracketEnd + 1);
            var parsed = ParseSingleToolCall(toolCallText);
            if (parsed != null)
            {
                toolCalls.Add(parsed);
            }
        }

        return toolCalls;
    }

    private static int FindMatchingBracket(string text, int startPos)
    {
        if (string.IsNullOrEmpty(text) || startPos >= text.Length || text[startPos] != '[')
        {
            return -1;
        }

        var bracketCount = 1;
        var inString = false;
        var escapeNext = false;

        for (var i = startPos + 1; i < text.Length; i++)
        {
            var ch = text[i];

            if (escapeNext)
            {
                escapeNext = false;
                continue;
            }

            if (ch == '\\' && inString)
            {
                escapeNext = true;
                continue;
            }

            if (ch == '"')
            {
                inString = !inString;
                continue;
            }

            if (!inString)
            {
                if (ch == '[')
                {
                    bracketCount++;
                }
                else if (ch == ']')
                {
                    bracketCount--;
                    if (bracketCount == 0)
                    {
                        return i;
                    }
                }
            }
        }

        return -1;
    }

    private static KiroToolCall? ParseSingleToolCall(string toolCallText)
    {
        var nameMatch = Regex.Match(toolCallText, "\\[Called\\s+(\\w+)\\s+with\\s+args:", RegexOptions.IgnoreCase);
        if (!nameMatch.Success)
        {
            return null;
        }

        var functionName = nameMatch.Groups[1].Value.Trim();
        var argsStart = toolCallText.IndexOf("with args:", StringComparison.OrdinalIgnoreCase);
        if (argsStart < 0)
        {
            return null;
        }

        argsStart += "with args:".Length;
        var argsEnd = toolCallText.LastIndexOf(']');
        if (argsEnd <= argsStart)
        {
            return null;
        }

        var jsonCandidate = toolCallText.Substring(argsStart, argsEnd - argsStart).Trim();
        try
        {
            var repaired = RepairJson(jsonCandidate);
            using var doc = JsonDocument.Parse(repaired);
            var toolCallId = $"call_{Guid.NewGuid():N}"[..16];
            return new KiroToolCall(toolCallId, functionName, doc.RootElement.GetRawText());
        }
        catch
        {
            return null;
        }
    }

    private static string RepairJson(string json)
    {
        var repaired = Regex.Replace(json, ",\\s*([}\\]])", "$1");
        repaired = Regex.Replace(repaired, ":\\s*([a-zA-Z0-9_]+)(?=[,\\}\\]])", ":\"$1\"");
        return repaired;
    }

    private static List<KiroToolCall> DeduplicateToolCalls(List<KiroToolCall> toolCalls)
    {
        var seen = new HashSet<string>();
        var unique = new List<KiroToolCall>();
        foreach (var call in toolCalls)
        {
            var key = $"{call.Name}-{call.Arguments}";
            if (seen.Add(key))
            {
                unique.Add(call);
            }
        }

        return unique;
    }

    private static string RemoveBracketToolCalls(string content, List<KiroToolCall> toolCalls)
    {
        var cleaned = content;
        foreach (var toolCall in toolCalls)
        {
            if (string.IsNullOrWhiteSpace(toolCall.Name))
            {
                continue;
            }

            var escapedName = Regex.Escape(toolCall.Name);
            var pattern = $"\\[Called\\s+{escapedName}\\s+with\\s+args:\\s*\\{{[^}}]*(?:\\{{[^}}]*\\}}[^}}]*)*\\}}\\]";
            cleaned = Regex.Replace(cleaned, pattern, string.Empty, RegexOptions.Singleline);
        }

        return Regex.Replace(cleaned, "\\s+", " ").Trim();
    }

    private static JsonObject BuildAnthropicResponse(
        string content,
        List<KiroToolCall> toolCalls,
        string model,
        int inputTokens)
    {
        var messageId = Guid.NewGuid().ToString();
        var contentArray = new JsonArray();
        var outputTokens = 0;
        var stopReason = "end_turn";

        if (toolCalls.Count > 0)
        {
            foreach (var call in toolCalls)
            {
                var inputNode = BuildToolInputNode(call.Arguments);
                contentArray.Add(new JsonObject
                {
                    ["type"] = "tool_use",
                    ["id"] = call.Id,
                    ["name"] = call.Name,
                    ["input"] = inputNode
                });
                outputTokens += CountTokens(call.Arguments);
            }

            stopReason = "tool_use";
        }
        else
        {
            contentArray.Add(new JsonObject
            {
                ["type"] = "text",
                ["text"] = content
            });
            outputTokens += CountTokens(content);
        }

        return new JsonObject
        {
            ["id"] = messageId,
            ["type"] = "message",
            ["role"] = "assistant",
            ["model"] = model,
            ["stop_reason"] = stopReason,
            ["stop_sequence"] = null,
            ["usage"] = new JsonObject
            {
                ["input_tokens"] = inputTokens,
                ["cache_creation_input_tokens"] = 0,
                ["cache_read_input_tokens"] = 0,
                ["output_tokens"] = outputTokens
            },
            ["content"] = contentArray
        };
    }

    private static JsonObject BuildOpenAiResponse(
        string content,
        List<KiroToolCall> toolCalls,
        string model,
        int inputTokens)
    {
        var choices = new JsonArray();
        var message = new JsonObject
        {
            ["role"] = "assistant"
        };

        string? finishReason;
        if (toolCalls.Count > 0)
        {
            message["tool_calls"] = BuildOpenAiToolCalls(toolCalls, includeIndex: false);
            message["content"] = string.IsNullOrWhiteSpace(content) ? null : content;
            finishReason = "tool_calls";
        }
        else
        {
            message["content"] = content;
            finishReason = "stop";
        }

        choices.Add(new JsonObject
        {
            ["index"] = 0,
            ["message"] = message,
            ["finish_reason"] = finishReason
        });

        var outputTokens = CountTokens(content) + toolCalls.Sum(call => CountTokens(call.Arguments));
        return new JsonObject
        {
            ["id"] = Guid.NewGuid().ToString(),
            ["object"] = "chat.completion",
            ["created"] = DateTimeOffset.UtcNow.ToUnixTimeSeconds(),
            ["model"] = model,
            ["choices"] = choices,
            ["usage"] = new JsonObject
            {
                ["prompt_tokens"] = inputTokens,
                ["completion_tokens"] = outputTokens,
                ["total_tokens"] = inputTokens + outputTokens
            }
        };
    }

    private static JsonArray BuildOpenAiToolCalls(List<KiroToolCall> toolCalls, bool includeIndex)
    {
        var array = new JsonArray();
        for (var i = 0; i < toolCalls.Count; i++)
        {
            var call = toolCalls[i];
            var toolCall = new JsonObject
            {
                ["id"] = call.Id,
                ["type"] = "function",
                ["function"] = new JsonObject
                {
                    ["name"] = call.Name,
                    ["arguments"] = string.IsNullOrWhiteSpace(call.Arguments) ? "{}" : call.Arguments
                }
            };

            if (includeIndex)
            {
                toolCall["index"] = i;
            }

            array.Add(toolCall);
        }

        return array;
    }

    private static JsonNode BuildToolInputNode(string? arguments)
    {
        if (string.IsNullOrWhiteSpace(arguments))
        {
            return new JsonObject();
        }

        try
        {
            return JsonNode.Parse(arguments) ?? new JsonObject();
        }
        catch
        {
            return new JsonObject { ["raw_arguments"] = arguments };
        }
    }

    private static (List<KiroStreamEvent> Events, string Remaining) ParseAwsEventStreamBuffer(string buffer)
    {
        var events = new List<KiroStreamEvent>();
        var remaining = buffer;
        var searchStart = 0;

        while (true)
        {
            var contentStart = remaining.IndexOf("{\"content\":", searchStart, StringComparison.Ordinal);
            var nameStart = remaining.IndexOf("{\"name\":", searchStart, StringComparison.Ordinal);
            var followupStart = remaining.IndexOf("{\"followupPrompt\":", searchStart, StringComparison.Ordinal);
            var inputStart = remaining.IndexOf("{\"input\":", searchStart, StringComparison.Ordinal);
            var stopStart = remaining.IndexOf("{\"stop\":", searchStart, StringComparison.Ordinal);

            var candidates = new[] { contentStart, nameStart, followupStart, inputStart, stopStart }
                .Where(pos => pos >= 0)
                .ToList();

            if (candidates.Count == 0)
            {
                break;
            }

            var jsonStart = candidates.Min();
            if (jsonStart < 0)
            {
                break;
            }

            var braceCount = 0;
            var jsonEnd = -1;
            var inString = false;
            var escapeNext = false;

            for (var i = jsonStart; i < remaining.Length; i++)
            {
                var ch = remaining[i];

                if (escapeNext)
                {
                    escapeNext = false;
                    continue;
                }

                if (ch == '\\')
                {
                    escapeNext = true;
                    continue;
                }

                if (ch == '"')
                {
                    inString = !inString;
                    continue;
                }

                if (!inString)
                {
                    if (ch == '{')
                    {
                        braceCount++;
                    }
                    else if (ch == '}')
                    {
                        braceCount--;
                        if (braceCount == 0)
                        {
                            jsonEnd = i;
                            break;
                        }
                    }
                }
            }

            if (jsonEnd < 0)
            {
                remaining = remaining.Substring(jsonStart);
                searchStart = 0;
                break;
            }

            var jsonStr = remaining.Substring(jsonStart, jsonEnd - jsonStart + 1);
            try
            {
                using var doc = JsonDocument.Parse(jsonStr);
                var root = doc.RootElement;

                if (root.TryGetProperty("content", out var contentElement)
                    && !root.TryGetProperty("followupPrompt", out _))
                {
                    events.Add(new KiroStreamEvent(KiroStreamEventType.Content)
                    {
                        Content = contentElement.GetString()
                    });
                }
                else if (root.TryGetProperty("name", out var nameElement)
                         && root.TryGetProperty("toolUseId", out var toolUseIdElement))
                {
                    events.Add(new KiroStreamEvent(KiroStreamEventType.ToolUse)
                    {
                        ToolUse = new KiroToolUseEvent(
                            nameElement.GetString() ?? string.Empty,
                            toolUseIdElement.GetString() ?? Guid.NewGuid().ToString("N"),
                            root.TryGetProperty("input", out var inputElement) ? inputElement.GetString() : null,
                            root.TryGetProperty("stop", out var stopElement) &&
                            stopElement.ValueKind == JsonValueKind.True)
                    });
                }
                else if (root.TryGetProperty("input", out var inputOnlyElement)
                         && !root.TryGetProperty("name", out _))
                {
                    events.Add(new KiroStreamEvent(KiroStreamEventType.ToolUseInput)
                    {
                        ToolInput = inputOnlyElement.GetString()
                    });
                }
                else if (root.TryGetProperty("stop", out var stopOnlyElement))
                {
                    events.Add(new KiroStreamEvent(KiroStreamEventType.ToolUseStop)
                    {
                        ToolStop = stopOnlyElement.ValueKind == JsonValueKind.True
                    });
                }
            }
            catch
            {
                // ignore parse errors
            }

            searchStart = jsonEnd + 1;
            if (searchStart >= remaining.Length)
            {
                remaining = string.Empty;
                break;
            }
        }

        if (searchStart > 0 && remaining.Length > 0)
        {
            remaining = remaining[searchStart..];
        }

        return (events, remaining);
    }

    private static async Task WriteAnthropicError(HttpContext context, int statusCode, string message, string errorType)
    {
        context.Response.StatusCode = statusCode;
        context.Response.ContentType = "application/json";
        var payload = new Dictionary<string, object?>
        {
            ["type"] = "error",
            ["error"] = new Dictionary<string, object?>
            {
                ["type"] = errorType,
                ["message"] = message
            }
        };

        await context.Response.WriteAsync(JsonSerializer.Serialize(payload));
    }

    private static async Task WriteOpenAIErrorResponse(HttpContext context, string message, int statusCode)
    {
        context.Response.ContentType = "application/json; charset=utf-8";
        context.Response.StatusCode = statusCode;
        var payload = new JsonObject
        {
            ["error"] = new JsonObject
            {
                ["message"] = message,
                ["type"] = "api_error",
                ["code"] = statusCode
            }
        };
        await context.Response.WriteAsync(payload.ToJsonString(JsonOptions.DefaultOptions));
    }

    private static async Task WriteSseJsonAsync(HttpContext context, JsonObject json, CancellationToken ct)
    {
        await context.Response.WriteAsync("data: ", ct);
        await context.Response.WriteAsync(json.ToJsonString(JsonOptions.DefaultOptions), ct);
        await context.Response.WriteAsync("\n\n", ct);
        await context.Response.Body.FlushAsync(ct);
    }

    private static async Task WriteSseDoneAsync(HttpContext context, CancellationToken ct)
    {
        await context.Response.WriteAsync("data: [DONE]\n\n", ct);
        await context.Response.Body.FlushAsync(ct);
    }

    private sealed class KiroMessage(string role, List<KiroMessagePart> parts)
    {
        public string Role { get; } = role;
        public List<KiroMessagePart> Parts { get; } = parts;

        public KiroMessage Clone()
        {
            return new KiroMessage(Role, Parts.Select(part => part.Clone()).ToList());
        }
    }

    private sealed class KiroMessagePart
    {
        public KiroMessagePart()
        {
        }

        public KiroMessagePart(string type)
        {
            Type = type;
        }

        public string Type { get; }
        public string? Text { get; init; }
        public string? ToolUseId { get; init; }
        public string? Name { get; init; }
        public JsonNode? Input { get; init; }
        public string? Content { get; init; }
        public KiroImageSource? Source { get; init; }

        public KiroCachePoint? CachePoint { get; set; }

        public KiroMessagePart Clone()
        {
            return new KiroMessagePart(Type)
            {
                Text = Text,
                ToolUseId = ToolUseId,
                Name = Name,
                Input = Input,
                Content = Content,
                Source = Source,
                CachePoint = CachePoint
            };
        }
    }

    private sealed class KiroCachePoint
    {
        public string Type { get; set; } = "default";
    }

    private sealed class KiroImageSource(string? mediaType, string? data)
    {
        public string? MediaType { get; } = mediaType;
        public string? Data { get; } = data;
    }

    private sealed record KiroToolCall(string Id, string Name, string Arguments);

    private sealed class ToolCallAccumulator
    {
        public ToolCallAccumulator(string toolUseId, string name)
        {
            ToolUseId = string.IsNullOrWhiteSpace(toolUseId) ? Guid.NewGuid().ToString("N") : toolUseId;
            Name = name;
        }

        public string ToolUseId { get; }
        public string Name { get; }
        public StringBuilder Input { get; } = new();

        public KiroToolCall ToToolCall()
        {
            var args = Input.ToString();
            return new KiroToolCall(ToolUseId, Name, string.IsNullOrWhiteSpace(args) ? "{}" : args);
        }
    }

    private enum KiroStreamEventType
    {
        Content,
        ToolUse,
        ToolUseInput,
        ToolUseStop
    }

    private sealed class KiroStreamEvent(KiroStreamEventType type)
    {
        public KiroStreamEventType Type { get; } = type;
        public string? Content { get; init; }
        public KiroToolUseEvent? ToolUse { get; init; }
        public string? ToolInput { get; init; }
        public bool? ToolStop { get; init; }
    }

    private sealed record KiroToolUseEvent(string Name, string ToolUseId, string? Input, bool Stop);
}