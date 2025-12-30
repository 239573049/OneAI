using System.Diagnostics;
using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using OneAI.Constants;
using OneAI.Entities;
using OneAI.Services;
using OneAI.Services.AI.Models.Dtos;
using OneAI.Services.GeminiOAuth;
using OneAI.Services.Logging;
using Thor.Abstractions.Chats.Consts;
using Thor.Abstractions.Chats.Dtos;

namespace OneAI.Services.AI;

/// <summary>
/// OpenAI Chat Completions 兼容层（OpenAI ↔ Gemini 协议转换）
/// </summary>
public sealed class ChatCompletionsService(
    AccountQuotaCacheService quotaCache,
    AIRequestLogService requestLogService,
    ILogger<ChatCompletionsService> logger,
    IConfiguration configuration,
    IModelMappingService modelMappingService,
    GeminiOAuthService geminiOAuthService,
    GeminiAntigravityOAuthService geminiAntigravityOAuthService)
{
    private static readonly HttpClient HttpClient = new();

    private static readonly string[] ClientErrorKeywords =
    [
        "invalid_argument",
        "permission_denied",
        "resource_exhausted",
        "\"INVALID_ARGUMENT\""
    ];

    private const string FakeStreamingPrefix = "假流式/";
    private const string AntiTruncationPrefix = "流式抗截断/";

    private static readonly JsonArray DefaultSafetySettings =
    [
        new JsonObject { ["category"] = "HARM_CATEGORY_HARASSMENT", ["threshold"] = "BLOCK_NONE" },
        new JsonObject { ["category"] = "HARM_CATEGORY_HATE_SPEECH", ["threshold"] = "BLOCK_NONE" },
        new JsonObject { ["category"] = "HARM_CATEGORY_SEXUALLY_EXPLICIT", ["threshold"] = "BLOCK_NONE" },
        new JsonObject { ["category"] = "HARM_CATEGORY_DANGEROUS_CONTENT", ["threshold"] = "BLOCK_NONE" },
        new JsonObject { ["category"] = "HARM_CATEGORY_CIVIC_INTEGRITY", ["threshold"] = "BLOCK_NONE" },
        new JsonObject { ["category"] = "HARM_CATEGORY_IMAGE_HATE", ["threshold"] = "BLOCK_NONE" },
        new JsonObject { ["category"] = "HARM_CATEGORY_IMAGE_DANGEROUS_CONTENT", ["threshold"] = "BLOCK_NONE" },
        new JsonObject { ["category"] = "HARM_CATEGORY_IMAGE_HARASSMENT", ["threshold"] = "BLOCK_NONE" },
        new JsonObject { ["category"] = "HARM_CATEGORY_IMAGE_SEXUALLY_EXPLICIT", ["threshold"] = "BLOCK_NONE" },
        new JsonObject { ["category"] = "HARM_CATEGORY_JAILBREAK", ["threshold"] = "BLOCK_NONE" }
    ];

    public async Task Execute(
        HttpContext context,
        ThorChatCompletionsRequest request,
        string? conversationId,
        AIAccountService aiAccountService)
    {
        ClampMaxTokens(request);
        request.Messages = FilterEmptyMessages(request.Messages);

        var originalModel = request.Model;
        var isStreaming = request.Stream == true;

        var useFakeStreaming = IsFakeStreamingModel(originalModel);
        var useAntiTruncation = IsAntiTruncationModel(originalModel);
        if (useAntiTruncation)
        {
            logger.LogInformation("已检测到模型前缀 {Prefix}，但当前不执行抗截断续写逻辑", AntiTruncationPrefix);
        }

        var modelWithoutPrefix = StripFeaturePrefixes(originalModel);
        var baseModelForMapping = GetBaseModelName(modelWithoutPrefix);
        var mapping = await modelMappingService.ResolveOpenAiChatAsync(baseModelForMapping);
        var geminiModel = mapping?.TargetModel ?? baseModelForMapping;
        var preferredProvider = mapping?.TargetProvider;

        var (geminiRequestData, toolNameMapper) = BuildGeminiRequestData(request, modelWithoutPrefix);
        var geminiPayload = new JsonObject
        {
            ["model"] = geminiModel,
            ["request"] = geminiRequestData
        };

        AIProviderAsyncLocal.AIProviderIds = new List<int>();

        var (logId, stopwatch) = await requestLogService.CreateRequestLog(
            context,
            model: originalModel,
            isStreaming: isStreaming,
            account: null,
            sessionStickinessUsed: false);

        try
        {
            if (useFakeStreaming && isStreaming)
            {
                await ExecuteFakeStreaming(
                    context,
                    geminiPayload,
                    conversationId,
                    aiAccountService,
                    preferredProvider,
                    logId,
                    stopwatch);
                return;
            }

            if (isStreaming)
            {
                await ExecuteStream(
                    context,
                    geminiPayload,
                    originalModel,
                    toolNameMapper,
                    conversationId,
                    aiAccountService,
                    preferredProvider,
                    includeUsage: true,
                    logId,
                    stopwatch);
                return;
            }

            await ExecuteNonStreaming(
                context,
                geminiPayload,
                originalModel,
                toolNameMapper,
                conversationId,
                aiAccountService,
                preferredProvider,
                logId,
                stopwatch);
        }
        catch (OperationCanceledException)
        {
            logger.LogInformation("客户端取消 /v1/chat/completions 请求");
        }
        catch (UpstreamRequestException ex)
        {
            logger.LogError(ex, "Gemini 上游请求失败: {StatusCode} {Message}", (int)ex.StatusCode, ex.Message);
            if (!context.Response.HasStarted)
            {
                await WriteOpenAIErrorResponse(context, ex.Message, (int)ex.StatusCode);
            }
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "处理 /v1/chat/completions 发生未捕获异常");
            if (!context.Response.HasStarted)
            {
                await requestLogService.RecordFailure(
                    logId,
                    stopwatch,
                    500,
                    $"服务器内部错误: {ex.Message}");
                await WriteOpenAIErrorResponse(context, $"服务器内部错误: {ex.Message}", 500);
            }
        }
    }

    private sealed class UpstreamRequestException(HttpStatusCode statusCode, string message) : Exception(message)
    {
        public HttpStatusCode StatusCode { get; } = statusCode;
    }

    private sealed class ToolNameMapper
    {
        private readonly Dictionary<string, string> _originalToGemini = new(StringComparer.Ordinal);
        private readonly Dictionary<string, string> _geminiToOriginal = new(StringComparer.Ordinal);

        public string ToGemini(string name)
        {
            if (string.IsNullOrWhiteSpace(name))
            {
                return "_unnamed_function";
            }

            if (_originalToGemini.TryGetValue(name, out var mapped))
            {
                return mapped;
            }

            var normalized = NormalizeFunctionName(name);
            _originalToGemini[name] = normalized;

            if (!_geminiToOriginal.ContainsKey(normalized))
            {
                _geminiToOriginal[normalized] = name;
            }

            return normalized;
        }

        public string ToOpenAi(string geminiName)
        {
            if (string.IsNullOrWhiteSpace(geminiName))
            {
                return geminiName;
            }

            return _geminiToOriginal.TryGetValue(geminiName, out var original)
                ? original
                : geminiName;
        }
    }

    private static string NormalizeFunctionName(string name)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            return "_unnamed_function";
        }

        var sb = new StringBuilder();
        foreach (var ch in name)
        {
            sb.Append(IsValidFunctionNameChar(ch) ? ch : '_');
        }

        var normalized = Regex.Replace(sb.ToString(), "_+", "_");

        var prefixAdded = false;
        if (!IsAsciiLetter(normalized[0]) && normalized[0] != '_')
        {
            normalized = normalized[0] is '.' or '-'
                ? "_" + normalized[1..]
                : "_" + normalized;
            prefixAdded = true;
        }

        normalized = (name.StartsWith('_') || prefixAdded)
            ? normalized.TrimEnd('_')
            : normalized.Trim('_');

        if (string.IsNullOrWhiteSpace(normalized))
        {
            normalized = "_unnamed_function";
        }

        var changed = !string.Equals(normalized, name, StringComparison.Ordinal);
        if (changed)
        {
            normalized = AppendShortHash(normalized, name, 64);
        }

        return normalized.Length <= 64 ? normalized : normalized[..64];
    }

    private static string AppendShortHash(string baseName, string original, int maxLength)
    {
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(original));
        var hashSuffix = "_" + Convert.ToHexString(hash)[..8].ToLowerInvariant();

        var allowedBaseLength = Math.Max(1, maxLength - hashSuffix.Length);
        var trimmed = baseName.Length > allowedBaseLength ? baseName[..allowedBaseLength] : baseName;
        return trimmed + hashSuffix;
    }

    private static bool IsValidFunctionNameChar(char ch)
    {
        return IsAsciiLetterOrDigit(ch) || ch == '_' || ch == '.' || ch == '-';
    }

    private static bool IsAsciiLetterOrDigit(char ch)
    {
        return (ch is >= 'a' and <= 'z')
               || (ch is >= 'A' and <= 'Z')
               || (ch is >= '0' and <= '9');
    }

    private static bool IsAsciiLetter(char ch)
    {
        return (ch is >= 'a' and <= 'z') || (ch is >= 'A' and <= 'Z');
    }

    private static bool TryParseJsonNode(string? json, out JsonNode? node)
    {
        node = null;
        if (string.IsNullOrWhiteSpace(json))
        {
            return false;
        }

        try
        {
            node = JsonNode.Parse(json);
            return node != null;
        }
        catch
        {
            return false;
        }
    }

    private static bool TryParseDataUri(string? url, out string mimeType, out string base64Data)
    {
        mimeType = string.Empty;
        base64Data = string.Empty;

        if (string.IsNullOrWhiteSpace(url))
        {
            return false;
        }

        if (!url.StartsWith("data:", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        var commaIndex = url.IndexOf(',');
        if (commaIndex < 0)
        {
            return false;
        }

        var header = url[5..commaIndex];
        var data = url[(commaIndex + 1)..];

        var parts = header.Split(';', StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length == 0)
        {
            return false;
        }

        mimeType = parts[0];
        base64Data = data;
        return !string.IsNullOrWhiteSpace(mimeType) && !string.IsNullOrWhiteSpace(base64Data);
    }

    private static bool IsHealthCheckRequest(ThorChatCompletionsRequest request)
    {
        if (request.Messages.Count != 1)
        {
            return false;
        }

        var msg = request.Messages[0];
        return msg.Role == ThorChatMessageRoleConst.User && msg.Content == "Hi";
    }

    private static void ClampMaxTokens(ThorChatCompletionsRequest request)
    {
        var max = request.MaxCompletionTokens ?? request.MaxTokens;
        if (max.HasValue && max.Value > 65535)
        {
            if (request.MaxCompletionTokens.HasValue)
            {
                request.MaxCompletionTokens = 65535;
            }
            else
            {
                request.MaxTokens = 65535;
            }
        }
    }

    private static List<ThorChatMessage> FilterEmptyMessages(List<ThorChatMessage> messages)
    {
        var filtered = new List<ThorChatMessage>(messages.Count);
        foreach (var m in messages)
        {
            if (m.Role == ThorChatMessageRoleConst.Assistant && m.ToolCalls is { Count: > 0 })
            {
                filtered.Add(m);
                continue;
            }

            if (m.Role == ThorChatMessageRoleConst.Tool)
            {
                filtered.Add(m);
                continue;
            }

            if (HasValidContent(m))
            {
                filtered.Add(m);
            }
        }

        return filtered;
    }

    private static bool HasValidContent(ThorChatMessage message)
    {
        if (!string.IsNullOrWhiteSpace(message.Content))
        {
            return true;
        }

        if (message.Contents is not { Count: > 0 })
        {
            return false;
        }

        foreach (var part in message.Contents)
        {
            if (part.Type == ThorMessageContentTypeConst.Text && !string.IsNullOrWhiteSpace(part.Text))
            {
                return true;
            }

            if (part.Type == ThorMessageContentTypeConst.ImageUrl && !string.IsNullOrWhiteSpace(part.ImageUrl?.Url))
            {
                return true;
            }
        }

        return false;
    }

    private static bool IsFakeStreamingModel(string modelName)
    {
        return modelName.StartsWith(FakeStreamingPrefix, StringComparison.Ordinal);
    }

    private static bool IsAntiTruncationModel(string modelName)
    {
        return modelName.StartsWith(AntiTruncationPrefix, StringComparison.Ordinal);
    }

    private static string StripFeaturePrefixes(string modelName)
    {
        foreach (var prefix in new[] { FakeStreamingPrefix, AntiTruncationPrefix })
        {
            if (modelName.StartsWith(prefix, StringComparison.Ordinal))
            {
                return modelName[prefix.Length..];
            }
        }

        return modelName;
    }

    private static string GetBaseModelName(string modelName)
    {
        var suffixes = new[] { "-maxthinking", "-nothinking", "-search" };
        var result = modelName;
        var changed = true;

        while (changed)
        {
            changed = false;
            foreach (var suffix in suffixes)
            {
                if (result.EndsWith(suffix, StringComparison.Ordinal))
                {
                    result = result[..^suffix.Length];
                    changed = true;
                    break;
                }
            }
        }

        return result;
    }

    private static bool IsSearchModel(string modelName)
    {
        return modelName.Contains("-search", StringComparison.Ordinal);
    }

    private static bool IsNoThinkingModel(string modelName)
    {
        return modelName.Contains("-nothinking", StringComparison.Ordinal);
    }

    private static bool IsMaxThinkingModel(string modelName)
    {
        return modelName.Contains("-maxthinking", StringComparison.Ordinal);
    }

    private static int? GetThinkingBudget(string modelName)
    {
        if (IsNoThinkingModel(modelName))
        {
            return 128;
        }

        if (IsMaxThinkingModel(modelName))
        {
            var baseModel = GetBaseModelName(StripFeaturePrefixes(modelName));
            return baseModel.Contains("flash", StringComparison.OrdinalIgnoreCase) ? 24576 : 32768;
        }

        return null;
    }

    private static bool ShouldIncludeThoughts(string modelName)
    {
        if (IsNoThinkingModel(modelName))
        {
            var baseModel = GetBaseModelName(modelName);
            return baseModel.Contains("pro", StringComparison.OrdinalIgnoreCase);
        }

        return true;
    }

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

    private string BuildGeminiInternalUrl(bool isStreaming)
    {
        var codeAssistEndpoint = configuration["Gemini:CodeAssistEndpoint"]
                                 ?? throw new InvalidOperationException("Gemini CodeAssistEndpoint is not configured");

        return isStreaming
            ? $"{codeAssistEndpoint}/v1internal:streamGenerateContent?alt=sse"
            : $"{codeAssistEndpoint}/v1internal:generateContent";
    }

    private async Task<HttpResponseMessage> SendGeminiRequest(
        string url,
        JsonObject payload,
        string accessToken,
        string? projectId,
        bool isStream,
        string userAgent,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(projectId))
        {
            throw new InvalidOperationException("Project ID is required for Gemini API");
        }

        var finalPayload = new JsonObject
        {
            ["model"] = payload["model"]?.GetValue<string>(),
            ["project"] = projectId,
            ["request"] = payload["request"]?.DeepClone()
        };

        var jsonPayload = finalPayload.ToJsonString(new JsonSerializerOptions
        {
            WriteIndented = false
        });

        var requestMessage = new HttpRequestMessage(HttpMethod.Post, url)
        {
            Content = new StringContent(jsonPayload, Encoding.UTF8, "application/json")
        };

        requestMessage.Headers.Add("Authorization", $"Bearer {accessToken}");
        requestMessage.Headers.Add("User-Agent", userAgent);

        return await HttpClient.SendAsync(
            requestMessage,
            isStream ? HttpCompletionOption.ResponseHeadersRead : HttpCompletionOption.ResponseContentRead,
            cancellationToken);
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
            AIProviders.GeminiAntigravity => geminiAntigravityOAuthService.RefreshGeminiAntigravityOAuthTokenAsync(
                account),
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

    private async Task<AIAccount?> GetGeminiAccount(
        string? conversationId,
        string? preferredProvider,
        AIAccountService aiAccountService)
    {
        AIAccount? account = null;

        if (!string.IsNullOrEmpty(conversationId))
        {
            var lastAccountId = quotaCache.GetConversationAccount(conversationId);
            if (lastAccountId.HasValue)
            {
                account = await aiAccountService.TryGetAccountById(lastAccountId.Value);
                if (account != null
                    && (account.Provider == AIProviders.Gemini || account.Provider == AIProviders.GeminiAntigravity))
                {
                    if (!string.IsNullOrWhiteSpace(preferredProvider)
                        && !string.Equals(account.Provider, preferredProvider, StringComparison.Ordinal))
                    {
                        account = null;
                    }
                }

                if (account != null)
                {
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

        if (account == null)
        {
            if (string.Equals(preferredProvider, AIProviders.Gemini, StringComparison.Ordinal))
            {
                return await aiAccountService.GetAIAccountByProvider(AIProviders.Gemini);
            }

            if (string.Equals(preferredProvider, AIProviders.GeminiAntigravity, StringComparison.Ordinal))
            {
                return await aiAccountService.GetAIAccountByProvider(AIProviders.GeminiAntigravity);
            }

            account = await aiAccountService.GetAIAccountByProvider(AIProviders.Gemini);
            if (account == null)
            {
                account = await aiAccountService.GetAIAccountByProvider(AIProviders.GeminiAntigravity);
            }
        }

        return account;
    }

    private async Task<HttpResponseMessage> SendGeminiWithRetries(
        HttpContext context,
        JsonObject payload,
        bool isStreaming,
        bool allowResponseStarted,
        string? conversationId,
        AIAccountService aiAccountService,
        string? preferredProvider,
        long logId,
        Stopwatch stopwatch)
    {
        const int maxRetries = 15;
        string? lastErrorMessage = null;
        HttpStatusCode? lastStatusCode = null;

        for (int attempt = 1; attempt <= maxRetries; attempt++)
        {
            if (!allowResponseStarted && context.Response.HasStarted)
            {
                lastErrorMessage = "响应已开始写入，无法继续重试";
                lastStatusCode = HttpStatusCode.InternalServerError;
                break;
            }

            var account = await GetGeminiAccount(conversationId, preferredProvider, aiAccountService);
            if (account == null)
            {
                lastErrorMessage = "账户池都无可用";
                lastStatusCode = HttpStatusCode.ServiceUnavailable;
                break;
            }

            AIProviderAsyncLocal.AIProviderIds.Add(account.Id);

            var geminiOAuth = account.GetGeminiOauth();
            if (geminiOAuth == null)
            {
                lastErrorMessage = $"账户 {account.Id} 没有有效的 Gemini OAuth 凭证";
                lastStatusCode = HttpStatusCode.Unauthorized;

                if (attempt < maxRetries)
                {
                    continue;
                }

                break;
            }

            geminiOAuth = await GetValidGeminiOAuthAsync(account, aiAccountService);
            if (geminiOAuth == null)
            {
                lastErrorMessage = $"账户 {account.Id} Gemini Token 无效或已过期且刷新失败";
                lastStatusCode = HttpStatusCode.Unauthorized;

                if (attempt < maxRetries)
                {
                    continue;
                }

                break;
            }

            try
            {
                var url = BuildGeminiInternalUrl(isStreaming);
                var userAgent = account.Provider == AIProviders.GeminiAntigravity
                    ? GeminiAntigravityOAuthConfig.UserAgent
                    : GetUserAgent();

                var response = await SendGeminiRequest(
                    url,
                    payload,
                    geminiOAuth.Token,
                    geminiOAuth.ProjectId,
                    isStreaming,
                    userAgent,
                    context.RequestAborted);

                await requestLogService.UpdateRetry(logId, attempt, account.Id);

                if (response.StatusCode >= HttpStatusCode.BadRequest)
                {
                    var error = await response.Content.ReadAsStringAsync(context.RequestAborted);

                    lastErrorMessage = error;
                    lastStatusCode = response.StatusCode;

                    logger.LogError(
                        "Gemini 请求失败 (账户: {AccountId}, 状态码: {StatusCode}, 尝试: {Attempt}/{MaxRetries}): {Error}",
                        account.Id,
                        response.StatusCode,
                        attempt,
                        maxRetries,
                        error);

                    var shouldDisableAccount =
                        response.StatusCode is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden;

                    var isClientError =
                        shouldDisableAccount
                        || ClientErrorKeywords.Any(keyword =>
                            error.Contains(keyword, StringComparison.OrdinalIgnoreCase));

                    if (shouldDisableAccount)
                    {
                        await aiAccountService.DisableAccount(account.Id);
                    }

                    if (isClientError)
                    {
                        await requestLogService.RecordFailure(
                            logId,
                            stopwatch,
                            (int)response.StatusCode,
                            error);

                        response.Dispose();
                        throw new UpstreamRequestException(response.StatusCode, error);
                    }

                    response.Dispose();

                    if (attempt < maxRetries)
                    {
                        continue;
                    }

                    break;
                }

                if (!string.IsNullOrEmpty(conversationId))
                {
                    quotaCache.SetConversationAccount(conversationId, account.Id);
                }

                await requestLogService.RecordSuccess(
                    logId,
                    stopwatch,
                    (int)response.StatusCode,
                    timeToFirstByteMs: stopwatch.ElapsedMilliseconds);

                return response;
            }
            catch (OperationCanceledException)
            {
                throw;
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

                break;
            }
        }

        var status = lastStatusCode ?? HttpStatusCode.ServiceUnavailable;
        var message = lastErrorMessage ?? $"所有 {maxRetries} 次重试均失败，无法完成请求";

        await requestLogService.RecordFailure(logId, stopwatch, (int)status, message);
        throw new UpstreamRequestException(status, message);
    }

    private async Task ExecuteNonStreaming(
        HttpContext context,
        JsonObject geminiPayload,
        string responseModel,
        ToolNameMapper toolNameMapper,
        string? conversationId,
        AIAccountService aiAccountService,
        string? preferredProvider,
        long logId,
        Stopwatch stopwatch)
    {
        using var response = await SendGeminiWithRetries(
            context,
            geminiPayload,
            isStreaming: false,
            allowResponseStarted: false,
            conversationId,
            aiAccountService,
            preferredProvider,
            logId,
            stopwatch);

        var responseText = await response.Content.ReadAsStringAsync(context.RequestAborted);
        if (!TryParseJson(responseText, out var doc))
        {
            context.Response.StatusCode = (int)HttpStatusCode.BadGateway;
            await WriteOpenAIErrorResponse(context, "Gemini 响应不是有效的 JSON", 502);
            return;
        }

        using (doc)
        {
            var root = ExtractGeminiResponseOrSelf(doc.RootElement);
            var openAiResponse = ConvertGeminiResponseToOpenAi(root, responseModel, toolNameMapper);

            context.Response.ContentType = "application/json; charset=utf-8";
            context.Response.StatusCode = (int)response.StatusCode;
            await context.Response.WriteAsync(openAiResponse.ToJsonString());
        }
    }

    private async Task ExecuteStream(
        HttpContext context,
        JsonObject geminiPayload,
        string responseModel,
        ToolNameMapper toolNameMapper,
        string? conversationId,
        AIAccountService aiAccountService,
        string? preferredProvider,
        bool includeUsage,
        long logId,
        Stopwatch stopwatch)
    {
        using var response = await SendGeminiWithRetries(
            context,
            geminiPayload,
            isStreaming: true,
            allowResponseStarted: false,
            conversationId,
            aiAccountService,
            preferredProvider,
            logId,
            stopwatch);

        context.Response.ContentType = "text/event-stream;charset=utf-8";
        context.Response.Headers.TryAdd("Cache-Control", "no-cache");
        context.Response.Headers.TryAdd("Connection", "keep-alive");
        context.Response.StatusCode = (int)response.StatusCode;

        var responseId = Guid.NewGuid().ToString();

        await using var stream = await response.Content.ReadAsStreamAsync(context.RequestAborted);
        using var reader = new System.IO.StreamReader(stream);

        while (true)
        {
            context.RequestAborted.ThrowIfCancellationRequested();

            var line = await reader.ReadLineAsync();
            if (line == null)
            {
                break;
            }

            if (!TryParseSseDataLine(line, out var data))
            {
                continue;
            }

            if (string.Equals(data, OpenAIConstant.Done, StringComparison.OrdinalIgnoreCase))
            {
                break;
            }

            if (!TryParseJson(data, out var doc))
            {
                continue;
            }

            using (doc)
            {
                var geminiChunk = ExtractGeminiResponseOrSelf(doc.RootElement);
                var openAiChunk = ConvertGeminiChunkToOpenAi(
                    geminiChunk,
                    responseModel,
                    responseId,
                    toolNameMapper,
                    includeUsage);

                await WriteSseJsonAsync(context, openAiChunk, context.RequestAborted);
            }
        }

        await WriteSseDoneAsync(context, context.RequestAborted);
    }

    private async Task ExecuteFakeStreaming(
        HttpContext context,
        JsonObject geminiPayload,
        string? conversationId,
        AIAccountService aiAccountService,
        string? preferredProvider,
        long logId,
        Stopwatch stopwatch)
    {
        context.Response.ContentType = "text/event-stream;charset=utf-8";
        context.Response.Headers.TryAdd("Cache-Control", "no-cache");
        context.Response.Headers.TryAdd("Connection", "keep-alive");

        var responseId = Guid.NewGuid().ToString();

        var heartbeatChunk = new JsonObject
        {
            ["id"] = responseId,
            ["object"] = "chat.completion.chunk",
            ["created"] = DateTimeOffset.UtcNow.ToUnixTimeSeconds(),
            ["model"] = "gcli2api-streaming",
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

        await WriteSseJsonAsync(context, heartbeatChunk, context.RequestAborted);

        try
        {
            var fetchTask = FetchFakeStreamResult(
                context,
                geminiPayload,
                conversationId,
                aiAccountService,
                preferredProvider,
                logId,
                stopwatch);

            while (!fetchTask.IsCompleted)
            {
                await Task.Delay(TimeSpan.FromSeconds(3), context.RequestAborted);
                if (!fetchTask.IsCompleted)
                {
                    await WriteSseJsonAsync(context, heartbeatChunk, context.RequestAborted);
                }
            }

            var (content, reasoningContent, usage) = await fetchTask;

            if (string.IsNullOrEmpty(content) && !string.IsNullOrEmpty(reasoningContent))
            {
                content = "[模型正在思考中，请稍后再试或重新提问]";
            }

            if (string.IsNullOrEmpty(content))
            {
                content = "[响应为空，请重新尝试]";
            }

            var delta = new JsonObject
            {
                ["role"] = "assistant",
                ["content"] = content
            };
            if (!string.IsNullOrEmpty(reasoningContent))
            {
                delta["reasoning_content"] = reasoningContent;
            }

            var contentChunk = new JsonObject
            {
                ["id"] = Guid.NewGuid().ToString(),
                ["object"] = "chat.completion.chunk",
                ["created"] = DateTimeOffset.UtcNow.ToUnixTimeSeconds(),
                ["model"] = "gcli2api-streaming",
                ["choices"] = new JsonArray
                {
                    new JsonObject
                    {
                        ["index"] = 0,
                        ["delta"] = delta,
                        ["finish_reason"] = "stop"
                    }
                }
            };

            if (usage != null)
            {
                contentChunk["usage"] = usage;
            }

            await WriteSseJsonAsync(context, contentChunk, context.RequestAborted);
        }
        catch (OperationCanceledException)
        {
            return;
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "假流式处理失败");

            var errorChunk = new JsonObject
            {
                ["id"] = responseId,
                ["object"] = "chat.completion.chunk",
                ["created"] = DateTimeOffset.UtcNow.ToUnixTimeSeconds(),
                ["model"] = "gcli2api-streaming",
                ["choices"] = new JsonArray
                {
                    new JsonObject
                    {
                        ["index"] = 0,
                        ["delta"] = new JsonObject
                        {
                            ["role"] = "assistant",
                            ["content"] = $"Error: {ex.Message}"
                        },
                        ["finish_reason"] = "stop"
                    }
                }
            };

            await WriteSseJsonAsync(context, errorChunk, context.RequestAborted);
        }
        finally
        {
            await WriteSseDoneAsync(context, context.RequestAborted);
        }
    }

    private async Task<(string Content, string ReasoningContent, JsonObject? Usage)> FetchFakeStreamResult(
        HttpContext context,
        JsonObject geminiPayload,
        string? conversationId,
        AIAccountService aiAccountService,
        string? preferredProvider,
        long logId,
        Stopwatch stopwatch)
    {
        using var response = await SendGeminiWithRetries(
            context,
            geminiPayload,
            isStreaming: false,
            allowResponseStarted: true,
            conversationId,
            aiAccountService,
            preferredProvider,
            logId,
            stopwatch);

        var responseText = await response.Content.ReadAsStringAsync(context.RequestAborted);
        if (!TryParseJson(responseText, out var doc))
        {
            throw new InvalidOperationException("Gemini 响应不是有效的 JSON");
        }

        using (doc)
        {
            var root = ExtractGeminiResponseOrSelf(doc.RootElement);
            return ExtractContentAndReasoningForFakeStream(root);
        }
    }

    private (JsonObject RequestData, ToolNameMapper Mapper) BuildGeminiRequestData(
        ThorChatCompletionsRequest request,
        string modelName)
    {
        var mapper = new ToolNameMapper();

        var contents = new JsonArray();
        var systemInstructions = new List<string>();
        var collectingSystem = true;

        foreach (var message in request.Messages)
        {
            var role = message.Role;

            if (role == ThorChatMessageRoleConst.Tool)
            {
                var functionResponse = ConvertToolMessageToFunctionResponse(message, request.Messages, mapper);
                contents.Add(new JsonObject
                {
                    ["role"] = "user",
                    ["parts"] = new JsonArray { functionResponse }
                });
                continue;
            }

            if (role == ThorChatMessageRoleConst.System)
            {
                if (collectingSystem)
                {
                    if (!string.IsNullOrWhiteSpace(message.Content))
                    {
                        systemInstructions.Add(message.Content);
                    }
                    else if (message.Contents is { Count: > 0 })
                    {
                        foreach (var part in message.Contents)
                        {
                            if (part.Type == ThorMessageContentTypeConst.Text && !string.IsNullOrWhiteSpace(part.Text))
                            {
                                systemInstructions.Add(part.Text);
                            }
                        }
                    }

                    continue;
                }

                role = ThorChatMessageRoleConst.User;
            }
            else
            {
                collectingSystem = false;
            }

            var geminiRole = role == ThorChatMessageRoleConst.Assistant ? "model" : "user";

            if (role == ThorChatMessageRoleConst.Assistant && message.ToolCalls is { Count: > 0 })
            {
                var parts = new JsonArray();

                if (!string.IsNullOrWhiteSpace(message.Content))
                {
                    parts.Add(new JsonObject { ["text"] = message.Content });
                }

                var parsedCount = 0;
                foreach (var toolCall in message.ToolCalls)
                {
                    var functionName = toolCall.Function?.Name;
                    if (string.IsNullOrWhiteSpace(functionName))
                    {
                        continue;
                    }

                    var arguments = toolCall.Function?.Arguments;
                    if (!TryParseJsonNode(arguments, out var argsNode))
                    {
                        continue;
                    }

                    parts.Add(new JsonObject
                    {
                        ["functionCall"] = new JsonObject
                        {
                            ["name"] = mapper.ToGemini(functionName),
                            ["args"] = argsNode
                        }
                    });
                    parsedCount++;
                }

                if (parsedCount == 0 && parts.Count == 0)
                {
                    throw new InvalidOperationException("所有 tool_calls 的 arguments 都无法解析且没有可用的文本内容");
                }

                if (parts.Count > 0)
                {
                    contents.Add(new JsonObject
                    {
                        ["role"] = geminiRole,
                        ["parts"] = parts
                    });
                }

                continue;
            }

            if (message.Contents is { Count: > 0 })
            {
                var parts = new JsonArray();
                foreach (var part in message.Contents)
                {
                    if (part.Type == ThorMessageContentTypeConst.Text)
                    {
                        parts.Add(new JsonObject { ["text"] = part.Text ?? string.Empty });
                    }
                    else if (part.Type == ThorMessageContentTypeConst.ImageUrl)
                    {
                        if (TryParseDataUri(part.ImageUrl?.Url, out var mimeType, out var base64Data))
                        {
                            parts.Add(new JsonObject
                            {
                                ["inlineData"] = new JsonObject
                                {
                                    ["mimeType"] = mimeType,
                                    ["data"] = base64Data
                                }
                            });
                        }
                    }
                }

                if (parts.Count > 0)
                {
                    contents.Add(new JsonObject
                    {
                        ["role"] = geminiRole,
                        ["parts"] = parts
                    });
                }
            }
            else if (!string.IsNullOrWhiteSpace(message.Content))
            {
                contents.Add(new JsonObject
                {
                    ["role"] = geminiRole,
                    ["parts"] = new JsonArray
                    {
                        new JsonObject { ["text"] = message.Content }
                    }
                });
            }
        }

        if (contents.Count == 0)
        {
            contents.Add(new JsonObject
            {
                ["role"] = "user",
                ["parts"] = new JsonArray { new JsonObject { ["text"] = "请根据系统指令回答。" } }
            });
        }

        var generationConfig = new JsonObject
        {
            ["topK"] = 64
        };

        if (request.Temperature is not null)
        {
            generationConfig["temperature"] = request.Temperature.Value;
        }

        if (request.TopP is not null)
        {
            generationConfig["topP"] = request.TopP.Value;
        }

        var maxTokens = request.MaxCompletionTokens ?? request.MaxTokens;
        if (maxTokens is not null)
        {
            generationConfig["maxOutputTokens"] = maxTokens.Value;
        }

        if (request.StopCalculated is { Count: > 0 })
        {
            generationConfig["stopSequences"] = JsonSerializer.SerializeToNode(request.StopCalculated);
        }

        if (request.FrequencyPenalty is not null)
        {
            generationConfig["frequencyPenalty"] = request.FrequencyPenalty.Value;
        }

        if (request.PresencePenalty is not null)
        {
            generationConfig["presencePenalty"] = request.PresencePenalty.Value;
        }

        if (request.N is not null)
        {
            generationConfig["candidateCount"] = request.N.Value;
        }

        if (request.Seed is not null)
        {
            generationConfig["seed"] = request.Seed.Value;
        }

        if (string.Equals(request.ResponseFormat?.Type, "json_object", StringComparison.OrdinalIgnoreCase))
        {
            generationConfig["responseMimeType"] = "application/json";
        }

        var thinkingBudget = GetThinkingBudget(modelName);
        if (thinkingBudget is not null)
        {
            generationConfig["thinkingConfig"] = new JsonObject
            {
                ["thinkingBudget"] = thinkingBudget.Value,
                ["includeThoughts"] = ShouldIncludeThoughts(modelName)
            };
        }

        var requestData = new JsonObject
        {
            ["contents"] = contents,
            ["generationConfig"] = generationConfig,
            ["safetySettings"] = DefaultSafetySettings.DeepClone()
        };

        if (systemInstructions.Count > 0)
        {
            requestData["systemInstruction"] = new JsonObject
            {
                ["parts"] = new JsonArray
                {
                    new JsonObject { ["text"] = string.Join("\n\n", systemInstructions) }
                }
            };
        }

        var tools = ConvertOpenAiToolsToGeminiTools(request.Tools, mapper);
        if (IsSearchModel(modelName))
        {
            tools ??= new JsonArray();
            var hasSearch = tools
                .Select(t => t as JsonObject)
                .Any(t => t?["googleSearch"] is not null);
            if (!hasSearch)
            {
                tools.Add(new JsonObject { ["googleSearch"] = new JsonObject() });
            }
        }

        if (tools is { Count: > 0 })
        {
            requestData["tools"] = tools;
        }

        var toolConfig = ConvertToolChoiceToGeminiToolConfig(request.ToolChoice, mapper);
        if (toolConfig != null)
        {
            requestData["toolConfig"] = toolConfig;
        }

        return (requestData, mapper);
    }

    private JsonArray? ConvertOpenAiToolsToGeminiTools(List<ThorToolDefinition>? openAiTools, ToolNameMapper mapper)
    {
        if (openAiTools is not { Count: > 0 })
        {
            return null;
        }

        var functionDeclarations = new JsonArray();
        foreach (var tool in openAiTools)
        {
            if (!string.Equals(tool.Type, ThorToolTypeConst.Function, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            var fn = tool.Function;
            if (fn == null || string.IsNullOrWhiteSpace(fn.Name))
            {
                continue;
            }

            var geminiName = mapper.ToGemini(fn.Name);
            if (!string.Equals(geminiName, fn.Name, StringComparison.Ordinal))
            {
                logger.LogInformation("Function name normalized: '{Original}' -> '{Normalized}'", fn.Name, geminiName);
            }

            var declaration = new JsonObject
            {
                ["name"] = geminiName,
                ["description"] = fn.Description ?? string.Empty
            };

            if (fn.Parameters != null)
            {
                var parametersNode = JsonSerializer.SerializeToNode(fn.Parameters);
                if (parametersNode != null)
                {
                    declaration["parameters"] = parametersNode;
                }
            }

            functionDeclarations.Add(declaration);
        }

        if (functionDeclarations.Count == 0)
        {
            return null;
        }

        return new JsonArray
        {
            new JsonObject { ["functionDeclarations"] = functionDeclarations }
        };
    }

    private static JsonObject? ConvertToolChoiceToGeminiToolConfig(ThorToolChoice? toolChoice, ToolNameMapper mapper)
    {
        if (toolChoice == null || string.IsNullOrWhiteSpace(toolChoice.Type))
        {
            return null;
        }

        var type = toolChoice.Type.Trim().ToLowerInvariant();
        return type switch
        {
            "auto" => new JsonObject
            {
                ["functionCallingConfig"] = new JsonObject { ["mode"] = "AUTO" }
            },
            "none" => new JsonObject
            {
                ["functionCallingConfig"] = new JsonObject { ["mode"] = "NONE" }
            },
            "required" => new JsonObject
            {
                ["functionCallingConfig"] = new JsonObject { ["mode"] = "ANY" }
            },
            "function" => BuildForceFunctionToolConfig(toolChoice, mapper),
            _ => new JsonObject
            {
                ["functionCallingConfig"] = new JsonObject { ["mode"] = "AUTO" }
            }
        };
    }

    private static JsonObject ConvertToolMessageToFunctionResponse(
        ThorChatMessage toolMessage,
        List<ThorChatMessage> allMessages,
        ToolNameMapper mapper)
    {
        var name = toolMessage.Name;
        if (string.IsNullOrWhiteSpace(name))
        {
            var toolCallId = toolMessage.ToolCallId;
            if (!string.IsNullOrWhiteSpace(toolCallId))
            {
                foreach (var msg in allMessages)
                {
                    if (msg.Role != ThorChatMessageRoleConst.Assistant || msg.ToolCalls is not { Count: > 0 })
                    {
                        continue;
                    }

                    foreach (var tc in msg.ToolCalls)
                    {
                        if (string.Equals(tc.Id, toolCallId, StringComparison.Ordinal)
                            && !string.IsNullOrWhiteSpace(tc.Function?.Name))
                        {
                            name = tc.Function!.Name;
                            break;
                        }
                    }

                    if (!string.IsNullOrWhiteSpace(name))
                    {
                        break;
                    }
                }
            }

            if (string.IsNullOrWhiteSpace(name))
            {
                throw new InvalidOperationException("tool 消息缺少 name 且无法通过 tool_call_id 推断函数名");
            }
        }

        var geminiName = mapper.ToGemini(name);

        JsonNode responseNode;
        if (TryParseJsonNode(toolMessage.Content, out var parsed))
        {
            responseNode = parsed!;
        }
        else
        {
            responseNode = new JsonObject { ["result"] = toolMessage.Content ?? string.Empty };
        }

        return new JsonObject
        {
            ["functionResponse"] = new JsonObject
            {
                ["name"] = geminiName,
                ["response"] = responseNode
            }
        };
    }

    private static JsonObject BuildForceFunctionToolConfig(ThorToolChoice toolChoice, ToolNameMapper mapper)
    {
        var name = toolChoice.Function?.Name;
        if (string.IsNullOrWhiteSpace(name))
        {
            return new JsonObject
            {
                ["functionCallingConfig"] = new JsonObject { ["mode"] = "ANY" }
            };
        }

        var geminiName = mapper.ToGemini(name);
        return new JsonObject
        {
            ["functionCallingConfig"] = new JsonObject
            {
                ["mode"] = "ANY",
                ["allowedFunctionNames"] = new JsonArray { geminiName }
            }
        };
    }

    private static bool TryParseJson(string json, out JsonDocument doc)
    {
        doc = null!;
        try
        {
            doc = JsonDocument.Parse(json);
            return true;
        }
        catch
        {
            return false;
        }
    }

    private static JsonElement ExtractGeminiResponseOrSelf(JsonElement root)
    {
        if (root.ValueKind == JsonValueKind.Object
            && root.TryGetProperty("response", out var resp)
            && resp.ValueKind == JsonValueKind.Object)
        {
            return resp;
        }

        return root;
    }

    private static bool TryParseSseDataLine(string line, out string data)
    {
        data = string.Empty;

        if (string.IsNullOrWhiteSpace(line))
        {
            return false;
        }

        var trimmed = line.TrimStart();
        if (!trimmed.StartsWith("data:", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        data = trimmed.Length == 5 ? string.Empty : trimmed[5..].TrimStart();
        return true;
    }

    private static async Task WriteSseJsonAsync(HttpContext context, JsonObject json, CancellationToken ct)
    {
        await context.Response.WriteAsync("data: ", ct);
        await context.Response.WriteAsync(json.ToJsonString(), ct);
        await context.Response.WriteAsync("\n\n", ct);
        await context.Response.Body.FlushAsync(ct);
    }

    private static async Task WriteSseDoneAsync(HttpContext context, CancellationToken ct)
    {
        await context.Response.WriteAsync("data: [DONE]\n\n", ct);
        await context.Response.Body.FlushAsync(ct);
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
        await context.Response.WriteAsync(payload.ToJsonString());
    }

    private static JsonObject? ConvertUsageMetadata(JsonElement gemini)
    {
        if (!gemini.TryGetProperty("usageMetadata", out var usage) || usage.ValueKind != JsonValueKind.Object)
        {
            return null;
        }

        return new JsonObject
        {
            ["prompt_tokens"] = usage.TryGetProperty("promptTokenCount", out var p) && p.ValueKind == JsonValueKind.Number
                ? p.GetInt32()
                : 0,
            ["completion_tokens"] = usage.TryGetProperty("candidatesTokenCount", out var c) && c.ValueKind == JsonValueKind.Number
                ? c.GetInt32()
                : 0,
            ["total_tokens"] = usage.TryGetProperty("totalTokenCount", out var t) && t.ValueKind == JsonValueKind.Number
                ? t.GetInt32()
                : 0
        };
    }

    private static string? MapFinishReason(string? geminiReason)
    {
        return geminiReason switch
        {
            "STOP" => "stop",
            "MAX_TOKENS" => "length",
            "SAFETY" => "content_filter",
            "RECITATION" => "content_filter",
            _ => null
        };
    }

    private static (string Content, string ReasoningContent, JsonObject? Usage) ExtractContentAndReasoningForFakeStream(
        JsonElement geminiResponse)
    {
        string content = string.Empty;
        string reasoningContent = string.Empty;

        if (geminiResponse.TryGetProperty("candidates", out var candidates)
            && candidates.ValueKind == JsonValueKind.Array
            && candidates.GetArrayLength() > 0)
        {
            var candidate = candidates[0];
            if (candidate.TryGetProperty("content", out var contentObj)
                && contentObj.ValueKind == JsonValueKind.Object
                && contentObj.TryGetProperty("parts", out var parts)
                && parts.ValueKind == JsonValueKind.Array)
            {
                foreach (var part in parts.EnumerateArray())
                {
                    if (part.ValueKind != JsonValueKind.Object)
                    {
                        continue;
                    }

                    if (!part.TryGetProperty("text", out var textObj) || textObj.ValueKind != JsonValueKind.String)
                    {
                        continue;
                    }

                    var isThought = part.TryGetProperty("thought", out var thoughtObj)
                                   && thoughtObj.ValueKind == JsonValueKind.True;
                    if (isThought)
                    {
                        reasoningContent += textObj.GetString();
                    }
                    else
                    {
                        content += textObj.GetString();
                    }
                }
            }
        }

        var usage = ConvertUsageMetadata(geminiResponse);
        return (content, reasoningContent, usage);
    }

    private static (JsonArray ToolCalls, string TextContent, string ReasoningContent) ExtractToolCallsAndText(
        JsonElement parts,
        bool isStreaming,
        ToolNameMapper toolNameMapper)
    {
        var toolCalls = new JsonArray();
        var text = new StringBuilder();
        var reasoning = new StringBuilder();

        if (parts.ValueKind != JsonValueKind.Array)
        {
            return (toolCalls, string.Empty, string.Empty);
        }

        var partIndex = 0;
        foreach (var part in parts.EnumerateArray())
        {
            if (part.ValueKind != JsonValueKind.Object)
            {
                partIndex++;
                continue;
            }

            if (part.TryGetProperty("functionCall", out var functionCall)
                && functionCall.ValueKind == JsonValueKind.Object)
            {
                var name = functionCall.TryGetProperty("name", out var n) ? n.GetString() : null;
                var args = functionCall.TryGetProperty("args", out var a) ? a : default;

                var toolCallId = "call_" + Guid.NewGuid().ToString("N")[..24];

                var toolCall = new JsonObject
                {
                    ["id"] = toolCallId,
                    ["type"] = "function",
                    ["function"] = new JsonObject
                    {
                        ["name"] = toolNameMapper.ToOpenAi(name ?? string.Empty),
                        ["arguments"] = args.ValueKind is JsonValueKind.Undefined or JsonValueKind.Null
                            ? "{}"
                            : args.GetRawText()
                    }
                };

                if (isStreaming)
                {
                    toolCall["index"] = partIndex;
                }

                toolCalls.Add(toolCall);
                partIndex++;
                continue;
            }

            if (part.TryGetProperty("text", out var textObj) && textObj.ValueKind == JsonValueKind.String)
            {
                var isThought = part.TryGetProperty("thought", out var thoughtObj)
                               && thoughtObj.ValueKind == JsonValueKind.True;
                if (isThought)
                {
                    reasoning.Append(textObj.GetString());
                }
                else
                {
                    text.Append(textObj.GetString());
                }
            }

            partIndex++;
        }

        return (toolCalls, text.ToString(), reasoning.ToString());
    }

    private static JsonObject ConvertGeminiResponseToOpenAi(
        JsonElement geminiResponse,
        string model,
        ToolNameMapper toolNameMapper)
    {
        var choices = new JsonArray();

        if (geminiResponse.TryGetProperty("candidates", out var candidates) && candidates.ValueKind == JsonValueKind.Array)
        {
            foreach (var candidate in candidates.EnumerateArray())
            {
                var parts = candidate.TryGetProperty("content", out var contentObj)
                    && contentObj.ValueKind == JsonValueKind.Object
                    && contentObj.TryGetProperty("parts", out var partsObj)
                    ? partsObj
                    : default;

                var (toolCalls, textContent, reasoningContent) = ExtractToolCallsAndText(
                    parts,
                    isStreaming: false,
                    toolNameMapper);

                var message = new JsonObject
                {
                    ["role"] = "assistant"
                };

                string? finishReason;
                if (toolCalls.Count > 0)
                {
                    message["tool_calls"] = toolCalls;
                    message["content"] = string.IsNullOrEmpty(textContent) ? null : textContent;
                    finishReason = "tool_calls";
                }
                else
                {
                    message["content"] = textContent;
                    finishReason = candidate.TryGetProperty("finishReason", out var fr)
                        ? MapFinishReason(fr.GetString())
                        : null;
                }

                if (!string.IsNullOrEmpty(reasoningContent))
                {
                    message["reasoning_content"] = reasoningContent;
                }

                choices.Add(new JsonObject
                {
                    ["index"] = candidate.TryGetProperty("index", out var idx) && idx.ValueKind == JsonValueKind.Number ? idx.GetInt32() : 0,
                    ["message"] = message,
                    ["finish_reason"] = finishReason
                });
            }
        }

        var response = new JsonObject
        {
            ["id"] = Guid.NewGuid().ToString(),
            ["object"] = "chat.completion",
            ["created"] = DateTimeOffset.UtcNow.ToUnixTimeSeconds(),
            ["model"] = model,
            ["choices"] = choices
        };

        var usage = ConvertUsageMetadata(geminiResponse);
        if (usage != null)
        {
            response["usage"] = usage;
        }

        return response;
    }

    private static JsonObject ConvertGeminiChunkToOpenAi(
        JsonElement geminiChunk,
        string model,
        string responseId,
        ToolNameMapper toolNameMapper,
        bool includeUsage)
    {
        var choices = new JsonArray();

        if (geminiChunk.TryGetProperty("candidates", out var candidates) && candidates.ValueKind == JsonValueKind.Array)
        {
            foreach (var candidate in candidates.EnumerateArray())
            {
                var parts = candidate.TryGetProperty("content", out var contentObj)
                    && contentObj.ValueKind == JsonValueKind.Object
                    && contentObj.TryGetProperty("parts", out var partsObj)
                    ? partsObj
                    : default;

                var (toolCalls, textContent, reasoningContent) = ExtractToolCallsAndText(
                    parts,
                    isStreaming: true,
                    toolNameMapper);

                var delta = new JsonObject();
                if (toolCalls.Count > 0)
                {
                    delta["tool_calls"] = toolCalls;
                    if (!string.IsNullOrEmpty(textContent))
                    {
                        delta["content"] = textContent;
                    }
                }
                else if (!string.IsNullOrEmpty(textContent))
                {
                    delta["content"] = textContent;
                }

                if (!string.IsNullOrEmpty(reasoningContent))
                {
                    delta["reasoning_content"] = reasoningContent;
                }

                var finishReason = candidate.TryGetProperty("finishReason", out var fr)
                    ? MapFinishReason(fr.GetString())
                    : null;

                if (!string.IsNullOrEmpty(finishReason) && toolCalls.Count > 0)
                {
                    finishReason = "tool_calls";
                }

                choices.Add(new JsonObject
                {
                    ["index"] = candidate.TryGetProperty("index", out var idx) && idx.ValueKind == JsonValueKind.Number ? idx.GetInt32() : 0,
                    ["delta"] = delta,
                    ["finish_reason"] = finishReason
                });
            }
        }

        var response = new JsonObject
        {
            ["id"] = responseId,
            ["object"] = "chat.completion.chunk",
            ["created"] = DateTimeOffset.UtcNow.ToUnixTimeSeconds(),
            ["model"] = model,
            ["choices"] = choices
        };

        if (includeUsage)
        {
            var usage = ConvertUsageMetadata(geminiChunk);
            if (usage != null)
            {
                var hasFinishReason = choices
                    .Select(c => c as JsonObject)
                    .Any(c => c?["finish_reason"] is not null && c["finish_reason"]!.GetValue<string?>() != null);

                if (hasFinishReason)
                {
                    response["usage"] = usage;
                }
            }
        }

        return response;
    }
}
