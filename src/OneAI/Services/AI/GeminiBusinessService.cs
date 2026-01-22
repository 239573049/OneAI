using System.Collections.Concurrent;
using System.Linq;
using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using OneAI.Constants;
using OneAI.Entities;
using OneAI.Services.AI.Gemini;
using OneAI.Services.AI.Models.Dtos;
using OneAI.Services.GeminiBusinessOAuth;
using Thor.Abstractions.Chats.Consts;
using Thor.Abstractions.Chats.Dtos;

namespace OneAI.Services.AI;

/// <summary>
/// Gemini Business reverse API service (business.gemini.google)
/// </summary>
public sealed class GeminiBusinessService(
    AccountQuotaCacheService quotaCache,
    ILogger<GeminiBusinessService> logger,
    IConfiguration configuration)
{
    private const string GetXsrfUrl = "https://business.gemini.google/auth/getoxsrf";
    private const string WidgetCreateSessionUrl =
        "https://biz-discoveryengine.googleapis.com/v1alpha/locations/global/widgetCreateSession";
    private const string WidgetStreamAssistUrl =
        "https://biz-discoveryengine.googleapis.com/v1alpha/locations/global/widgetStreamAssist";
    private const string WidgetAddContextFileUrl =
        "https://biz-discoveryengine.googleapis.com/v1alpha/locations/global/widgetAddContextFile";
    private const string WidgetListSessionFileMetadataUrl =
        "https://biz-discoveryengine.googleapis.com/v1alpha/locations/global/widgetListSessionFileMetadata";
    private const string GeminiBusinessApiBaseUrl = "https://biz-discoveryengine.googleapis.com/v1alpha";

    private static readonly HttpClient HttpClient = new(new HttpClientHandler
    {
        AutomaticDecompression = DecompressionMethods.GZip | DecompressionMethods.Deflate | DecompressionMethods.Brotli
    })
    {
        Timeout = TimeSpan.FromSeconds(180)
    };

    private static readonly Dictionary<string, string?> ModelMapping = new(StringComparer.OrdinalIgnoreCase)
    {
        ["gemini-auto"] = null,
        ["gemini-2.5-flash"] = "gemini-2.5-flash",
        ["gemini-2.5-pro"] = "gemini-2.5-pro",
        ["gemini-3-flash-preview"] = "gemini-3-flash-preview",
        ["gemini-3-pro-preview"] = "gemini-3-pro-preview",
        // Alias: force-enable image generation while keeping upstream modelId compatible
        ["gemini-3-pro-image-preview"] = "gemini-3-pro-preview"
    };

    private sealed class JwtCacheEntry
    {
        public string Jwt { get; set; } = string.Empty;
        public DateTimeOffset ExpiresAt { get; set; } = DateTimeOffset.MinValue;
        public SemaphoreSlim Lock { get; } = new(1, 1);
    }

    private static readonly ConcurrentDictionary<int, JwtCacheEntry> JwtCache = new();

    private sealed record SessionCacheEntry(int AccountId, string SessionName, DateTimeOffset UpdatedAt);

    private static readonly ConcurrentDictionary<string, SessionCacheEntry> SessionCache = new();

    private static readonly TimeSpan SessionTtl = TimeSpan.FromMinutes(60);

    private sealed record GeneratedFileInfo(string FileId, string MimeType);

    private sealed record DownloadedImage(string MimeType, string Base64Data);

    private sealed record SessionFileMetadata(string FileId, string? SessionName, string? MimeType);

    private sealed record ContextFileUpload(string MimeType, string Base64Data);

    private static void TrimExpiredSessions()
    {
        var now = DateTimeOffset.UtcNow;
        foreach (var (key, value) in SessionCache)
        {
            if (now - value.UpdatedAt > SessionTtl)
            {
                SessionCache.TryRemove(key, out _);
            }
        }
    }

    private string GetUserAgent()
    {
        var configured = configuration["GeminiBusiness:UserAgent"];
        if (!string.IsNullOrWhiteSpace(configured))
        {
            return configured.Trim();
        }

        return "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/140.0.0.0 Safari/537.36";
    }

    private bool ShouldEnableImageGeneration(string modelName)
    {
        if (string.Equals(modelName, "gemini-3-pro-image-preview", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        var enabled = configuration.GetValue<bool?>("GeminiBusiness:ImageGeneration:Enabled")
                      ?? configuration.GetValue<bool?>("GeminiBusiness:ImageGenerationEnabled")
                      ?? true;

        if (!enabled)
        {
            return false;
        }

        var configuredModels = configuration
            .GetSection("GeminiBusiness:ImageGeneration:SupportedModels")
            .GetChildren()
            .Select(x => x.Value)
            .Where(x => !string.IsNullOrWhiteSpace(x))
            .ToArray();

        if (configuredModels.Length > 0)
        {
            return configuredModels.Any(m => string.Equals(m, modelName, StringComparison.OrdinalIgnoreCase));
        }

        // Default behavior (match gemini-business2api defaults)
        return string.Equals(modelName, "gemini-3-pro-preview", StringComparison.OrdinalIgnoreCase);
    }

    private long GetContextFilesMaxBytes()
    {
        var configured = configuration.GetValue<long?>("GeminiBusiness:ContextFiles:MaxBytes");
        if (configured is > 0)
        {
            return configured.Value;
        }

        // Default: 100MB (see gemini-business2api docs guidance)
        return 100L * 1024 * 1024;
    }

    private TimeSpan GetContextFilesDownloadTimeout()
    {
        var seconds = configuration.GetValue<int?>("GeminiBusiness:ContextFiles:DownloadTimeoutSeconds") ?? 30;
        if (seconds <= 0)
        {
            seconds = 30;
        }

        return TimeSpan.FromSeconds(seconds);
    }

    private sealed class UpstreamRequestException(HttpStatusCode statusCode, string message) : Exception(message)
    {
        public HttpStatusCode StatusCode { get; } = statusCode;
    }

    public async Task ExecuteChatCompletionsAsync(
        HttpContext context,
        ThorChatCompletionsRequest request,
        string? conversationId,
        AIAccountService aiAccountService)
    {
        var modelName = string.IsNullOrWhiteSpace(request.Model) ? "gemini-auto" : request.Model.Trim();
        var enableImageGeneration = ShouldEnableImageGeneration(modelName);
        var isStream = request.Stream == true;
        var cancellationToken = context.RequestAborted;

        if (request.Messages is not { Count: > 0 })
        {
            await WriteOpenAiError(context, StatusCodes.Status400BadRequest, "Missing messages");
            return;
        }

        var chatId = "chatcmpl-" + Guid.NewGuid().ToString("N");
        var createdTime = DateTimeOffset.UtcNow.ToUnixTimeSeconds();

        var userAgent = GetUserAgent();
        var lastText = ExtractText(request.Messages[^1]);
        var fullContextText = BuildFullContextText(request.Messages);
        List<ContextFileUpload> contextFiles;
        try
        {
            contextFiles = await ParseContextFilesFromMessageAsync(
                request.Messages[^1],
                userAgent,
                cancellationToken);
        }
        catch (UpstreamRequestException ex)
        {
            await WriteOpenAiError(context, (int)ex.StatusCode, ex.Message);
            return;
        }
        string? lastErrorMessage = null;
        HttpStatusCode? lastStatusCode = null;

        for (var attempt = 1; attempt <= 3; attempt++)
        {
            if (context.Response.HasStarted)
            {
                return;
            }

            var (account, _) = await TryResolveAccountAsync(aiAccountService, conversationId, cancellationToken);
            if (account == null)
            {
                await WriteOpenAiError(
                    context,
                    (int)(lastStatusCode ?? HttpStatusCode.ServiceUnavailable),
                    lastErrorMessage ?? "No available Gemini Business accounts");
                return;
            }

            var credentials = ValidateCredentials(account);
            if (credentials == null)
            {
                lastErrorMessage = $"Account {account.Id} missing/disabled credentials";
                lastStatusCode = HttpStatusCode.Unauthorized;
                await aiAccountService.DisableAccount(account.Id);
                continue;
            }

            try
            {
                var hadSession = !string.IsNullOrEmpty(conversationId)
                                 && SessionCache.TryGetValue(conversationId, out var cached)
                                 && cached.AccountId == account.Id
                                 && DateTimeOffset.UtcNow - cached.UpdatedAt <= SessionTtl;

                var sessionName = await GetOrCreateSessionAsync(account, credentials, conversationId, cancellationToken);
                var textToSend = hadSession ? lastText : fullContextText;
                var fileIds = contextFiles.Count > 0
                    ? await UploadContextFilesAsync(
                        account,
                        credentials,
                        sessionName,
                        contextFiles,
                        userAgent,
                        aiAccountService,
                        account.Id,
                        cancellationToken)
                    : new List<string>();

                using var response = await SendStreamAssistRequestAsync(
                    account,
                    credentials,
                    sessionName,
                    textToSend,
                    modelName,
                    userAgent,
                    cancellationToken,
                    enableImageGeneration: enableImageGeneration,
                    fileIds: fileIds);

                if (response.StatusCode == HttpStatusCode.Unauthorized)
                {
                    using var retryResponse = await SendStreamAssistRequestAsync(
                        account,
                        credentials,
                        sessionName,
                        textToSend,
                        modelName,
                        userAgent,
                        cancellationToken,
                        forceRefreshJwt: true,
                        enableImageGeneration: enableImageGeneration,
                        fileIds: fileIds);

                    if (retryResponse.StatusCode != HttpStatusCode.Unauthorized)
                    {
                        await HandleChatResponseAsync(
                            context,
                            retryResponse,
                            account,
                            credentials,
                            sessionName,
                            userAgent,
                            enableImageGeneration,
                            isStream,
                            chatId,
                            createdTime,
                            modelName,
                            conversationId,
                            aiAccountService,
                            account.Id,
                            cancellationToken);
                        return;
                    }
                }

                await HandleChatResponseAsync(
                    context,
                    response,
                    account,
                    credentials,
                    sessionName,
                    userAgent,
                    enableImageGeneration,
                    isStream,
                    chatId,
                    createdTime,
                    modelName,
                    conversationId,
                    aiAccountService,
                    account.Id,
                    cancellationToken);
                return;
            }
            catch (OperationCanceledException)
            {
                return;
            }
            catch (UpstreamRequestException ex)
            {
                lastErrorMessage = ex.Message;
                lastStatusCode = ex.StatusCode;

                if (ex.StatusCode is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden)
                {
                    await aiAccountService.DisableAccount(account.Id);
                    continue;
                }

                await WriteOpenAiError(context, (int)ex.StatusCode, ex.Message);
                return;
            }
            catch (Exception ex)
            {
                lastErrorMessage = ex.Message;
                lastStatusCode = HttpStatusCode.InternalServerError;
                logger.LogError(ex, "Gemini Business chat failed (attempt {Attempt})", attempt);
            }
        }

        await WriteOpenAiError(
            context,
            (int)(lastStatusCode ?? HttpStatusCode.ServiceUnavailable),
            lastErrorMessage ?? "Gemini Business request failed");
    }

    private async Task HandleChatResponseAsync(
        HttpContext context,
        HttpResponseMessage response,
        AIAccount account,
        GeminiBusinessCredentialsDto credentials,
        string sessionName,
        string userAgent,
        bool enableImageGeneration,
        bool isStream,
        string chatId,
        long createdTime,
        string modelName,
        string? conversationId,
        AIAccountService aiAccountService,
        int accountId,
        CancellationToken cancellationToken)
    {
        if (response.StatusCode >= HttpStatusCode.BadRequest)
        {
            var error = await response.Content.ReadAsStringAsync(cancellationToken);
            await HandleUpstreamErrorAsync(response, aiAccountService, accountId);
            await WriteOpenAiError(context, (int)response.StatusCode, error);
            return;
        }

        if (!string.IsNullOrEmpty(conversationId))
        {
            quotaCache.SetConversationAccount(conversationId, accountId);
        }

        if (isStream)
        {
            context.Response.ContentType = "text/event-stream;charset=utf-8";
            context.Response.Headers.TryAdd("Cache-Control", "no-cache");
            context.Response.Headers.TryAdd("Connection", "keep-alive");
            context.Response.StatusCode = StatusCodes.Status200OK;

            var initial = CreateOpenAiChunk(chatId, createdTime, modelName, new JsonObject { ["role"] = "assistant" }, null);
            await WriteSseLineAsync(context, $"data: {initial}", cancellationToken);
            await WriteSseLineAsync(context, string.Empty, cancellationToken);

            var generatedFiles = new Dictionary<string, GeneratedFileInfo>(StringComparer.Ordinal);
            string? responseSessionName = null;

            await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
            using var reader = new StreamReader(stream, Encoding.UTF8);

            await foreach (var json in ParseJsonArrayObjectsAsync(reader, cancellationToken))
            {
                cancellationToken.ThrowIfCancellationRequested();

                using var doc = JsonDocument.Parse(json, new JsonDocumentOptions { AllowTrailingCommas = true });
                var deltas = ExtractDeltasAndCollectFiles(doc.RootElement, generatedFiles, out var chunkSessionName);
                if (!string.IsNullOrWhiteSpace(chunkSessionName))
                {
                    responseSessionName = chunkSessionName;
                }

                foreach (var delta in deltas)
                {
                    var chunk = CreateOpenAiChunk(chatId, createdTime, modelName,
                        delta.IsThought
                            ? new JsonObject { ["reasoning_content"] = delta.Text }
                            : new JsonObject { ["content"] = delta.Text },
                        null);

                    await WriteSseLineAsync(context, $"data: {chunk}", cancellationToken);
                    await WriteSseLineAsync(context, string.Empty, cancellationToken);
                }
            }

            if (generatedFiles.Count > 0)
            {
                var images = await DownloadGeneratedImagesAsync(
                    account,
                    credentials,
                    responseSessionName ?? sessionName,
                    generatedFiles.Values.ToList(),
                    userAgent,
                    cancellationToken);

                for (var i = 0; i < images.Count; i++)
                {
                    var image = images[i];
                    var markdown = $"\n\n![generated image {i + 1}](data:{image.MimeType};base64,{image.Base64Data})\n\n";
                    var chunk = CreateOpenAiChunk(chatId, createdTime, modelName, new JsonObject { ["content"] = markdown }, null);
                    await WriteSseLineAsync(context, $"data: {chunk}", cancellationToken);
                    await WriteSseLineAsync(context, string.Empty, cancellationToken);
                }
            }

            var finalChunk = CreateOpenAiChunk(chatId, createdTime, modelName, new JsonObject(), "stop");
            await WriteSseLineAsync(context, $"data: {finalChunk}", cancellationToken);
            await WriteSseLineAsync(context, string.Empty, cancellationToken);
            await WriteSseLineAsync(context, "data: [DONE]", cancellationToken);
            await WriteSseLineAsync(context, string.Empty, cancellationToken);
            return;
        }

        var (content, reasoningContent, files, responseSessionName2) = await ReadAllContentAndFilesAsync(response, cancellationToken);

        if (files.Count > 0)
        {
            var images = await DownloadGeneratedImagesAsync(
                account,
                credentials,
                responseSessionName2 ?? sessionName,
                files,
                userAgent,
                cancellationToken);

            if (images.Count > 0)
            {
                var sb = new StringBuilder(content);
                for (var i = 0; i < images.Count; i++)
                {
                    var image = images[i];
                    sb.Append("\n\n")
                        .Append("![generated image ")
                        .Append(i + 1)
                        .Append("](data:")
                        .Append(image.MimeType)
                        .Append(";base64,")
                        .Append(image.Base64Data)
                        .Append(")\n\n");
                }

                content = sb.ToString();
            }
        }

        var message = new JsonObject
        {
            ["role"] = "assistant",
            ["content"] = content
        };
        if (!string.IsNullOrWhiteSpace(reasoningContent))
        {
            message["reasoning_content"] = reasoningContent;
        }

        var finalResponse = new JsonObject
        {
            ["id"] = chatId,
            ["object"] = "chat.completion",
            ["created"] = createdTime,
            ["model"] = modelName,
            ["choices"] = new JsonArray
            {
                new JsonObject
                {
                    ["index"] = 0,
                    ["message"] = message,
                    ["finish_reason"] = "stop"
                }
            },
            ["usage"] = new JsonObject
            {
                ["prompt_tokens"] = 0,
                ["completion_tokens"] = 0,
                ["total_tokens"] = 0
            }
        };

        context.Response.ContentType = "application/json; charset=utf-8";
        context.Response.StatusCode = StatusCodes.Status200OK;
        await context.Response.WriteAsync(finalResponse.ToJsonString(), cancellationToken);
    }

    private async Task<List<ContextFileUpload>> ParseContextFilesFromMessageAsync(
        ThorChatMessage message,
        string userAgent,
        CancellationToken cancellationToken)
    {
        if (message.Contents is not { Count: > 0 })
        {
            return new List<ContextFileUpload>();
        }

        var maxBytes = GetContextFilesMaxBytes();
        var timeout = GetContextFilesDownloadTimeout();
        var result = new List<ContextFileUpload>();

        foreach (var part in message.Contents)
        {
            if (!string.Equals(part.Type, ThorMessageContentTypeConst.ImageUrl, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            var url = part.ImageUrl?.Url;
            if (string.IsNullOrWhiteSpace(url))
            {
                continue;
            }

            if (TryParseDataUrl(url, out var mimeType, out var base64Data))
            {
                var estimatedBytes = EstimateBase64Bytes(base64Data);
                if (estimatedBytes > maxBytes)
                {
                    throw new UpstreamRequestException(
                        (HttpStatusCode)StatusCodes.Status413PayloadTooLarge,
                        $"Context file too large: ~{estimatedBytes} bytes (limit {maxBytes} bytes)");
                }

                result.Add(new ContextFileUpload(mimeType, base64Data));
                continue;
            }

            if (!url.StartsWith("http://", StringComparison.OrdinalIgnoreCase)
                && !url.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
            {
                logger.LogWarning("Gemini Business context file URL scheme not supported: {Url}", url);
                continue;
            }

            var downloaded = await DownloadContextFileFromUrlAsync(url, userAgent, timeout, maxBytes, cancellationToken);
            if (downloaded != null)
            {
                result.Add(downloaded);
            }
        }

        return result;
    }

    private static bool TryParseDataUrl(string url, out string mimeType, out string base64Data)
    {
        mimeType = string.Empty;
        base64Data = string.Empty;

        if (!url.StartsWith("data:", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        var markerIndex = url.IndexOf(";base64,", StringComparison.OrdinalIgnoreCase);
        if (markerIndex < 0)
        {
            return false;
        }

        var rawMime = url["data:".Length..markerIndex];
        if (string.IsNullOrWhiteSpace(rawMime))
        {
            return false;
        }

        mimeType = rawMime.Split(';', StringSplitOptions.RemoveEmptyEntries)[0].Trim();
        base64Data = url[(markerIndex + ";base64,".Length)..].Trim();

        return !string.IsNullOrWhiteSpace(mimeType) && !string.IsNullOrWhiteSpace(base64Data);
    }

    private static long EstimateBase64Bytes(string base64Data)
    {
        if (string.IsNullOrWhiteSpace(base64Data))
        {
            return 0;
        }

        var trimmed = base64Data.Trim();
        var padding = 0;
        if (trimmed.EndsWith("==", StringComparison.Ordinal))
        {
            padding = 2;
        }
        else if (trimmed.EndsWith("=", StringComparison.Ordinal))
        {
            padding = 1;
        }

        // Best-effort estimate (handles both padded and unpadded base64)
        return Math.Max(0, (trimmed.Length * 3L / 4L) - padding);
    }

    private async Task<ContextFileUpload?> DownloadContextFileFromUrlAsync(
        string url,
        string userAgent,
        TimeSpan timeout,
        long maxBytes,
        CancellationToken cancellationToken)
    {
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        cts.CancelAfter(timeout);

        using var request = new HttpRequestMessage(HttpMethod.Get, url);
        request.Headers.TryAddWithoutValidation("accept", "*/*");
        request.Headers.TryAddWithoutValidation("user-agent", userAgent);

        using var response = await HttpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cts.Token);
        if (response.StatusCode == HttpStatusCode.NotFound)
        {
            logger.LogWarning("Gemini Business context file URL returned 404: {Url}", url);
            return null;
        }

        if (!response.IsSuccessStatusCode)
        {
            logger.LogWarning(
                "Gemini Business context file URL download failed ({StatusCode}) for {Url}",
                (int)response.StatusCode,
                url);
            return null;
        }

        var mimeType = response.Content.Headers.ContentType?.MediaType ?? "application/octet-stream";
        if (mimeType.Contains(';', StringComparison.Ordinal))
        {
            mimeType = mimeType.Split(';', StringSplitOptions.RemoveEmptyEntries)[0].Trim();
        }

        var contentLength = response.Content.Headers.ContentLength;
        if (contentLength is > 0 && contentLength > maxBytes)
        {
            throw new UpstreamRequestException(
                (HttpStatusCode)StatusCodes.Status413PayloadTooLarge,
                $"Context file too large: {contentLength} bytes (limit {maxBytes} bytes)");
        }

        await using var stream = await response.Content.ReadAsStreamAsync(cts.Token);
        using var ms = new MemoryStream();
        var buffer = new byte[81920];
        long total = 0;

        while (true)
        {
            var read = await stream.ReadAsync(buffer.AsMemory(0, buffer.Length), cts.Token);
            if (read <= 0)
            {
                break;
            }

            total += read;
            if (total > maxBytes)
            {
                throw new UpstreamRequestException(
                    (HttpStatusCode)StatusCodes.Status413PayloadTooLarge,
                    $"Context file too large: {total} bytes (limit {maxBytes} bytes)");
            }

            ms.Write(buffer, 0, read);
        }

        var base64 = Convert.ToBase64String(ms.ToArray());
        return new ContextFileUpload(mimeType, base64);
    }

    private async Task<List<string>> UploadContextFilesAsync(
        AIAccount account,
        GeminiBusinessCredentialsDto credentials,
        string sessionName,
        IReadOnlyList<ContextFileUpload> files,
        string userAgent,
        AIAccountService aiAccountService,
        int accountId,
        CancellationToken cancellationToken)
    {
        if (files.Count == 0)
        {
            return new List<string>();
        }

        var ids = new List<string>(files.Count);
        foreach (var file in files)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var id = await UploadContextFileAsync(
                account,
                credentials,
                sessionName,
                file,
                userAgent,
                aiAccountService,
                accountId,
                cancellationToken);
            if (!string.IsNullOrWhiteSpace(id))
            {
                ids.Add(id);
            }
        }

        return ids;
    }

    private async Task<string> UploadContextFileAsync(
        AIAccount account,
        GeminiBusinessCredentialsDto credentials,
        string sessionName,
        ContextFileUpload file,
        string userAgent,
        AIAccountService aiAccountService,
        int accountId,
        CancellationToken cancellationToken)
    {
        async Task<HttpResponseMessage> SendAsync(string jwt)
        {
            var ext = "bin";
            var slash = file.MimeType.IndexOf('/', StringComparison.Ordinal);
            if (slash >= 0 && slash < file.MimeType.Length - 1)
            {
                ext = file.MimeType[(slash + 1)..];
                if (ext.Contains(';', StringComparison.Ordinal))
                {
                    ext = ext.Split(';', StringSplitOptions.RemoveEmptyEntries)[0].Trim();
                }
            }

            var suffix = Guid.NewGuid().ToString("N")[..6];
            var fileName = $"upload_{DateTimeOffset.UtcNow.ToUnixTimeSeconds()}_{suffix}.{ext}";

            var body = new JsonObject
            {
                ["configId"] = credentials.ConfigId,
                ["additionalParams"] = new JsonObject { ["token"] = "-" },
                ["addContextFileRequest"] = new JsonObject
                {
                    ["name"] = sessionName,
                    ["fileName"] = fileName,
                    ["mimeType"] = file.MimeType,
                    ["fileContents"] = file.Base64Data
                }
            };

            var json = body.ToJsonString();
            var request = new HttpRequestMessage(HttpMethod.Post, WidgetAddContextFileUrl)
            {
                Content = new StringContent(json, Encoding.UTF8, "application/json")
            };
            ApplyCommonHeaders(request, jwt, userAgent);
            return await HttpClient.SendAsync(request, cancellationToken);
        }

        var jwt = await GetJwtAsync(account.Id, credentials, userAgent, cancellationToken, forceRefresh: false);
        using var response = await SendAsync(jwt);
        if (response.StatusCode == HttpStatusCode.Unauthorized)
        {
            jwt = await GetJwtAsync(account.Id, credentials, userAgent, cancellationToken, forceRefresh: true);
            using var retryResponse = await SendAsync(jwt);
            return await ParseUploadContextFileResponseAsync(retryResponse, aiAccountService, accountId, cancellationToken);
        }

        return await ParseUploadContextFileResponseAsync(response, aiAccountService, accountId, cancellationToken);
    }

    private static async Task<string> ParseUploadContextFileResponseAsync(
        HttpResponseMessage response,
        AIAccountService aiAccountService,
        int accountId,
        CancellationToken cancellationToken)
    {
        if (response.StatusCode != HttpStatusCode.OK)
        {
            var errorBody = await response.Content.ReadAsStringAsync(cancellationToken);
            await HandleUpstreamErrorAsync(response, aiAccountService, accountId);

            if (response.StatusCode == HttpStatusCode.BadRequest)
            {
                try
                {
                    using var doc = JsonDocument.Parse(errorBody);
                    if (doc.RootElement.TryGetProperty("error", out var errObj)
                        && errObj.ValueKind == JsonValueKind.Object
                        && errObj.TryGetProperty("message", out var msgEl)
                        && msgEl.ValueKind == JsonValueKind.String)
                    {
                        var msg = msgEl.GetString() ?? string.Empty;
                        if (msg.Contains("Unsupported file type", StringComparison.OrdinalIgnoreCase))
                        {
                            var hint = msg.Contains(':', StringComparison.Ordinal)
                                ? msg.Split(':', 2, StringSplitOptions.RemoveEmptyEntries)[1].Trim()
                                : msg;
                            throw new UpstreamRequestException(HttpStatusCode.BadRequest, $"Unsupported file type: {hint}");
                        }
                    }
                }
                catch (JsonException)
                {
                    // ignore parse errors
                }
            }

            throw new UpstreamRequestException(
                response.StatusCode,
                $"Context file upload failed: {(int)response.StatusCode} {errorBody}");
        }

        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
        using var parsed = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken);
        if (!parsed.RootElement.TryGetProperty("addContextFileResponse", out var resp)
            || resp.ValueKind != JsonValueKind.Object
            || !resp.TryGetProperty("fileId", out var fileIdEl)
            || fileIdEl.ValueKind != JsonValueKind.String)
        {
            throw new UpstreamRequestException(HttpStatusCode.InternalServerError, "Context file upload response missing fileId");
        }

        return fileIdEl.GetString() ?? throw new UpstreamRequestException(HttpStatusCode.InternalServerError, "Context file upload response invalid fileId");
    }

    public async Task ExecuteGenerateContentAsync(
        HttpContext context,
        GeminiInput input,
        string model,
        string? conversationId,
        AIAccountService aiAccountService)
    {
        var modelName = string.IsNullOrWhiteSpace(model) ? "gemini-auto" : model.Trim();
        var enableImageGeneration = ShouldEnableImageGeneration(modelName);
        var cancellationToken = context.RequestAborted;

        var fullContextText = BuildFullContextText(input);
        var lastText = ExtractLastText(input);

        string? lastErrorMessage = null;
        HttpStatusCode? lastStatusCode = null;

        for (var attempt = 1; attempt <= 3; attempt++)
        {
            if (context.Response.HasStarted)
            {
                return;
            }

            var (account, _) = await TryResolveAccountAsync(aiAccountService, conversationId, cancellationToken);
            if (account == null)
            {
                context.Response.StatusCode = (int)(lastStatusCode ?? HttpStatusCode.ServiceUnavailable);
                await context.Response.WriteAsync(lastErrorMessage ?? "No available Gemini Business accounts", cancellationToken);
                return;
            }

            var credentials = ValidateCredentials(account);
            if (credentials == null)
            {
                lastErrorMessage = $"Account {account.Id} missing/disabled credentials";
                lastStatusCode = HttpStatusCode.Unauthorized;
                await aiAccountService.DisableAccount(account.Id);
                continue;
            }

            try
            {
                var hadSession = !string.IsNullOrEmpty(conversationId)
                                 && SessionCache.TryGetValue(conversationId, out var cached)
                                 && cached.AccountId == account.Id
                                 && DateTimeOffset.UtcNow - cached.UpdatedAt <= SessionTtl;

                var sessionName = await GetOrCreateSessionAsync(account, credentials, conversationId, cancellationToken);
                var textToSend = hadSession ? lastText : fullContextText;
                var userAgent = GetUserAgent();

                using var response = await SendStreamAssistRequestAsync(
                    account,
                    credentials,
                    sessionName,
                    textToSend,
                    modelName,
                    userAgent,
                    cancellationToken,
                    enableImageGeneration: enableImageGeneration);

                if (response.StatusCode == HttpStatusCode.Unauthorized)
                {
                    using var retryResponse = await SendStreamAssistRequestAsync(
                        account,
                        credentials,
                        sessionName,
                        textToSend,
                        modelName,
                        userAgent,
                        cancellationToken,
                        forceRefreshJwt: true,
                        enableImageGeneration: enableImageGeneration);

                    if (retryResponse.StatusCode != HttpStatusCode.Unauthorized)
                    {
                        await WriteGenerateContentResponseAsync(
                            context,
                            retryResponse,
                            account,
                            credentials,
                            sessionName,
                            userAgent,
                            enableImageGeneration,
                            conversationId,
                            aiAccountService,
                            account.Id,
                            cancellationToken);
                        return;
                    }
                }

                await WriteGenerateContentResponseAsync(
                    context,
                    response,
                    account,
                    credentials,
                    sessionName,
                    userAgent,
                    enableImageGeneration,
                    conversationId,
                    aiAccountService,
                    account.Id,
                    cancellationToken);
                return;
            }
            catch (OperationCanceledException)
            {
                return;
            }
            catch (Exception ex)
            {
                lastErrorMessage = ex.Message;
                lastStatusCode = HttpStatusCode.InternalServerError;
                logger.LogError(ex, "Gemini Business generateContent failed (attempt {Attempt})", attempt);
            }
        }

        context.Response.StatusCode = (int)(lastStatusCode ?? HttpStatusCode.ServiceUnavailable);
        await context.Response.WriteAsync(lastErrorMessage ?? "Gemini Business request failed", cancellationToken);
    }

    private async Task WriteGenerateContentResponseAsync(
        HttpContext context,
        HttpResponseMessage response,
        AIAccount account,
        GeminiBusinessCredentialsDto credentials,
        string sessionName,
        string userAgent,
        bool enableImageGeneration,
        string? conversationId,
        AIAccountService aiAccountService,
        int accountId,
        CancellationToken cancellationToken)
    {
        if (response.StatusCode >= HttpStatusCode.BadRequest)
        {
            var error = await response.Content.ReadAsStringAsync(cancellationToken);
            await HandleUpstreamErrorAsync(response, aiAccountService, accountId);
            context.Response.StatusCode = (int)response.StatusCode;
            context.Response.ContentType = "application/json; charset=utf-8";
            await context.Response.WriteAsync(error, cancellationToken);
            return;
        }

        if (!string.IsNullOrEmpty(conversationId))
        {
            quotaCache.SetConversationAccount(conversationId, accountId);
        }

        var (content, reasoningContent, files, responseSessionName) =
            await ReadAllContentAndFilesAsync(response, cancellationToken);

        var images = files.Count > 0
            ? await DownloadGeneratedImagesAsync(
                account,
                credentials,
                responseSessionName ?? sessionName,
                files,
                userAgent,
                cancellationToken)
            : new List<DownloadedImage>();

        var responseNode = BuildGeminiGenerateContentResponse(content, reasoningContent, images);

        context.Response.ContentType = "application/json; charset=utf-8";
        context.Response.StatusCode = StatusCodes.Status200OK;
        await context.Response.WriteAsync(responseNode.ToJsonString(), cancellationToken);
    }

    public async Task ExecuteStreamGenerateContentAsync(
        HttpContext context,
        GeminiInput input,
        string model,
        string? conversationId,
        AIAccountService aiAccountService)
    {
        var modelName = string.IsNullOrWhiteSpace(model) ? "gemini-auto" : model.Trim();
        var enableImageGeneration = ShouldEnableImageGeneration(modelName);
        var cancellationToken = context.RequestAborted;

        var fullContextText = BuildFullContextText(input);
        var lastText = ExtractLastText(input);

        string? lastErrorMessage = null;
        HttpStatusCode? lastStatusCode = null;

        for (var attempt = 1; attempt <= 3; attempt++)
        {
            if (context.Response.HasStarted)
            {
                return;
            }

            var (account, _) = await TryResolveAccountAsync(aiAccountService, conversationId, cancellationToken);
            if (account == null)
            {
                context.Response.StatusCode = (int)(lastStatusCode ?? HttpStatusCode.ServiceUnavailable);
                await context.Response.WriteAsync(lastErrorMessage ?? "No available Gemini Business accounts", cancellationToken);
                return;
            }

            var credentials = ValidateCredentials(account);
            if (credentials == null)
            {
                lastErrorMessage = $"Account {account.Id} missing/disabled credentials";
                lastStatusCode = HttpStatusCode.Unauthorized;
                await aiAccountService.DisableAccount(account.Id);
                continue;
            }

            try
            {
                var hadSession = !string.IsNullOrEmpty(conversationId)
                                 && SessionCache.TryGetValue(conversationId, out var cached)
                                 && cached.AccountId == account.Id
                                 && DateTimeOffset.UtcNow - cached.UpdatedAt <= SessionTtl;

                var sessionName = await GetOrCreateSessionAsync(account, credentials, conversationId, cancellationToken);
                var textToSend = hadSession ? lastText : fullContextText;
                var userAgent = GetUserAgent();

                using var response = await SendStreamAssistRequestAsync(
                    account,
                    credentials,
                    sessionName,
                    textToSend,
                    modelName,
                    userAgent,
                    cancellationToken,
                    enableImageGeneration: enableImageGeneration);

                if (response.StatusCode == HttpStatusCode.Unauthorized)
                {
                    using var retryResponse = await SendStreamAssistRequestAsync(
                        account,
                        credentials,
                        sessionName,
                        textToSend,
                        modelName,
                        userAgent,
                        cancellationToken,
                        forceRefreshJwt: true,
                        enableImageGeneration: enableImageGeneration);

                    if (retryResponse.StatusCode != HttpStatusCode.Unauthorized)
                    {
                        await WriteStreamGenerateContentResponseAsync(
                            context,
                            retryResponse,
                            account,
                            credentials,
                            sessionName,
                            userAgent,
                            enableImageGeneration,
                            conversationId,
                            aiAccountService,
                            account.Id,
                            cancellationToken);
                        return;
                    }
                }

                await WriteStreamGenerateContentResponseAsync(
                    context,
                    response,
                    account,
                    credentials,
                    sessionName,
                    userAgent,
                    enableImageGeneration,
                    conversationId,
                    aiAccountService,
                    account.Id,
                    cancellationToken);
                return;
            }
            catch (OperationCanceledException)
            {
                return;
            }
            catch (Exception ex)
            {
                lastErrorMessage = ex.Message;
                lastStatusCode = HttpStatusCode.InternalServerError;
                logger.LogError(ex, "Gemini Business streamGenerateContent failed (attempt {Attempt})", attempt);
            }
        }

        context.Response.StatusCode = (int)(lastStatusCode ?? HttpStatusCode.ServiceUnavailable);
        await context.Response.WriteAsync(lastErrorMessage ?? "Gemini Business request failed", cancellationToken);
    }

    private async Task WriteStreamGenerateContentResponseAsync(
        HttpContext context,
        HttpResponseMessage response,
        AIAccount account,
        GeminiBusinessCredentialsDto credentials,
        string sessionName,
        string userAgent,
        bool enableImageGeneration,
        string? conversationId,
        AIAccountService aiAccountService,
        int accountId,
        CancellationToken cancellationToken)
    {
        if (response.StatusCode >= HttpStatusCode.BadRequest)
        {
            var error = await response.Content.ReadAsStringAsync(cancellationToken);
            await HandleUpstreamErrorAsync(response, aiAccountService, accountId);
            context.Response.StatusCode = (int)response.StatusCode;
            await context.Response.WriteAsync(error, cancellationToken);
            return;
        }

        if (!string.IsNullOrEmpty(conversationId))
        {
            quotaCache.SetConversationAccount(conversationId, accountId);
        }

        context.Response.ContentType = "text/event-stream;charset=utf-8";
        context.Response.Headers.TryAdd("Cache-Control", "no-cache");
        context.Response.Headers.TryAdd("Connection", "keep-alive");
        context.Response.StatusCode = StatusCodes.Status200OK;

        var files = new Dictionary<string, GeneratedFileInfo>(StringComparer.Ordinal);
        string? responseSessionName = null;

        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
        using var reader = new StreamReader(stream, Encoding.UTF8);

        await foreach (var json in ParseJsonArrayObjectsAsync(reader, cancellationToken))
        {
            cancellationToken.ThrowIfCancellationRequested();
            using var doc = JsonDocument.Parse(json, new JsonDocumentOptions { AllowTrailingCommas = true });
            var deltas = ExtractDeltasAndCollectFiles(doc.RootElement, files, out var chunkSessionName);
            if (!string.IsNullOrWhiteSpace(chunkSessionName))
            {
                responseSessionName = chunkSessionName;
            }

            foreach (var delta in deltas)
            {
                var chunk = BuildGeminiStreamChunk(delta.Text, delta.IsThought);
                await WriteSseLineAsync(context, $"data: {chunk}", cancellationToken);
                await WriteSseLineAsync(context, string.Empty, cancellationToken);
            }
        }

        if (files.Count > 0)
        {
            var images = await DownloadGeneratedImagesAsync(
                account,
                credentials,
                responseSessionName ?? sessionName,
                files.Values.ToList(),
                userAgent,
                cancellationToken);

            foreach (var image in images)
            {
                var chunk = BuildGeminiStreamInlineDataChunk(image);
                await WriteSseLineAsync(context, $"data: {chunk}", cancellationToken);
                await WriteSseLineAsync(context, string.Empty, cancellationToken);
            }
        }

        var final = BuildGeminiStreamFinalChunk();
        await WriteSseLineAsync(context, $"data: {final}", cancellationToken);
        await WriteSseLineAsync(context, string.Empty, cancellationToken);
        await WriteSseLineAsync(context, "data: [DONE]", cancellationToken);
        await WriteSseLineAsync(context, string.Empty, cancellationToken);
    }

    private sealed record StreamAssistDelta(string Text, bool IsThought);

    private static async IAsyncEnumerable<string> ParseJsonArrayObjectsAsync(
        StreamReader reader,
        [System.Runtime.CompilerServices.EnumeratorCancellation]
        CancellationToken cancellationToken)
    {
        var buffer = new StringBuilder();
        var braceLevel = 0;
        var inArray = false;
        var inString = false;
        var escapeNext = false;

        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var line = await reader.ReadLineAsync();
            if (line == null)
            {
                yield break;
            }

            var stripped = line.Trim();
            if (string.IsNullOrEmpty(stripped))
            {
                continue;
            }

            if (!stripped.StartsWith("[", StringComparison.Ordinal))
            {
                continue;
            }

            inArray = true;
            stripped = stripped[1..];
            foreach (var obj in ProcessLine(stripped))
            {
                yield return obj;
            }

            break;
        }

        while (inArray)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var line = await reader.ReadLineAsync();
            if (line == null)
            {
                yield break;
            }

            foreach (var obj in ProcessLine(line))
            {
                yield return obj;
            }
        }

        IEnumerable<string> ProcessLine(string line)
        {
            List<string>? completed = null;
            foreach (var ch in line)
            {
                if (escapeNext)
                {
                    if (braceLevel > 0)
                    {
                        buffer.Append(ch);
                    }

                    escapeNext = false;
                    continue;
                }

                if (ch == '\\')
                {
                    if (braceLevel > 0)
                    {
                        buffer.Append(ch);
                    }

                    escapeNext = true;
                    continue;
                }

                if (ch == '"' && braceLevel > 0)
                {
                    inString = !inString;
                    buffer.Append(ch);
                    continue;
                }

                if (!inString)
                {
                    if (ch == '{')
                    {
                        if (braceLevel == 0)
                        {
                            buffer.Clear();
                        }

                        braceLevel++;
                    }

                    if (braceLevel > 0)
                    {
                        buffer.Append(ch);
                    }

                    if (ch == '}')
                    {
                        braceLevel--;
                        if (braceLevel == 0 && buffer.Length > 0)
                        {
                            completed ??= new List<string>();
                            completed.Add(buffer.ToString());
                            buffer.Clear();
                            inString = false;
                        }
                    }
                }
                else
                {
                    if (braceLevel > 0)
                    {
                        buffer.Append(ch);
                    }
                }
            }

            return completed ?? Enumerable.Empty<string>();
        }
    }

    private static List<StreamAssistDelta> ExtractDeltasAndCollectFiles(
        JsonElement root,
        IDictionary<string, GeneratedFileInfo> files,
        out string? sessionName)
    {
        sessionName = null;
        var deltas = new List<StreamAssistDelta>();

        if (!root.TryGetProperty("streamAssistResponse", out var sar) || sar.ValueKind != JsonValueKind.Object)
        {
            return deltas;
        }

        if (sar.TryGetProperty("sessionInfo", out var sessionInfo)
            && sessionInfo.ValueKind == JsonValueKind.Object
            && sessionInfo.TryGetProperty("session", out var sessionEl)
            && sessionEl.ValueKind == JsonValueKind.String)
        {
            sessionName = sessionEl.GetString();
        }

        if (!sar.TryGetProperty("answer", out var answer)
            || answer.ValueKind != JsonValueKind.Object
            || !answer.TryGetProperty("replies", out var replies)
            || replies.ValueKind != JsonValueKind.Array)
        {
            return deltas;
        }

        foreach (var reply in replies.EnumerateArray())
        {
            if (!reply.TryGetProperty("groundedContent", out var gc)
                || !gc.TryGetProperty("content", out var content)
                || content.ValueKind != JsonValueKind.Object)
            {
                continue;
            }

            if (content.TryGetProperty("file", out var fileObj)
                && fileObj.ValueKind == JsonValueKind.Object
                && fileObj.TryGetProperty("fileId", out var fileIdEl)
                && fileIdEl.ValueKind == JsonValueKind.String)
            {
                var fileId = fileIdEl.GetString();
                if (!string.IsNullOrWhiteSpace(fileId) && !files.ContainsKey(fileId))
                {
                    var mimeType = fileObj.TryGetProperty("mimeType", out var mimeEl) && mimeEl.ValueKind == JsonValueKind.String
                        ? mimeEl.GetString()
                        : null;

                    files[fileId] = new GeneratedFileInfo(fileId, string.IsNullOrWhiteSpace(mimeType) ? "image/png" : mimeType!);
                }
            }

            if (!content.TryGetProperty("text", out var textEl) || textEl.ValueKind != JsonValueKind.String)
            {
                continue;
            }

            var text = textEl.GetString();
            if (string.IsNullOrEmpty(text))
            {
                continue;
            }

            var isThought = content.TryGetProperty("thought", out var thoughtEl)
                            && thoughtEl.ValueKind is JsonValueKind.True or JsonValueKind.String or JsonValueKind.Number;

            deltas.Add(new StreamAssistDelta(text, isThought));
        }

        return deltas;
    }

    private static async Task<(string Content, string ReasoningContent, List<GeneratedFileInfo> Files, string? SessionName)>
        ReadAllContentAndFilesAsync(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
        using var reader = new StreamReader(stream, Encoding.UTF8);

        var content = new StringBuilder();
        var reasoning = new StringBuilder();
        var files = new Dictionary<string, GeneratedFileInfo>(StringComparer.Ordinal);
        string? sessionName = null;

        await foreach (var json in ParseJsonArrayObjectsAsync(reader, cancellationToken))
        {
            cancellationToken.ThrowIfCancellationRequested();
            using var doc = JsonDocument.Parse(json, new JsonDocumentOptions { AllowTrailingCommas = true });
            var deltas = ExtractDeltasAndCollectFiles(doc.RootElement, files, out var chunkSessionName);
            if (!string.IsNullOrWhiteSpace(chunkSessionName))
            {
                sessionName = chunkSessionName;
            }

            foreach (var delta in deltas)
            {
                if (delta.IsThought)
                {
                    reasoning.Append(delta.Text);
                }
                else
                {
                    content.Append(delta.Text);
                }
            }
        }

        return (content.ToString(), reasoning.ToString(), files.Values.ToList(), sessionName);
    }

    private static async IAsyncEnumerable<StreamAssistDelta> ParseStreamAssistAsync(
        Stream stream,
        [System.Runtime.CompilerServices.EnumeratorCancellation]
        CancellationToken cancellationToken)
    {
        using var reader = new StreamReader(stream, Encoding.UTF8);
        var files = new Dictionary<string, GeneratedFileInfo>(StringComparer.Ordinal);
        string? sessionName = null;

        await foreach (var json in ParseJsonArrayObjectsAsync(reader, cancellationToken))
        {
            cancellationToken.ThrowIfCancellationRequested();
            using var doc = JsonDocument.Parse(json, new JsonDocumentOptions { AllowTrailingCommas = true });
            var deltas = ExtractDeltasAndCollectFiles(doc.RootElement, files, out var chunkSessionName);
            if (!string.IsNullOrWhiteSpace(chunkSessionName))
            {
                sessionName = chunkSessionName;
            }

            foreach (var delta in deltas)
            {
                yield return delta;
            }
        }
    }

    private static async Task<(string Content, string ReasoningContent)> ReadAllContentAsync(
        HttpResponseMessage response,
        CancellationToken cancellationToken)
    {
        var (content, reasoning, _, _) = await ReadAllContentAndFilesAsync(response, cancellationToken);
        return (content, reasoning);
    }

    private async Task<List<DownloadedImage>> DownloadGeneratedImagesAsync(
        AIAccount account,
        GeminiBusinessCredentialsDto credentials,
        string sessionName,
        List<GeneratedFileInfo> files,
        string userAgent,
        CancellationToken cancellationToken)
    {
        if (files.Count == 0)
        {
            return new List<DownloadedImage>();
        }

        var imageFiles = files
            .Where(f => f.MimeType.StartsWith("image/", StringComparison.OrdinalIgnoreCase))
            .ToList();

        if (imageFiles.Count == 0)
        {
            return new List<DownloadedImage>();
        }

        Dictionary<string, SessionFileMetadata> metadata;
        try
        {
            metadata = await GetSessionGeneratedFileMetadataAsync(account, credentials, sessionName, userAgent, cancellationToken);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Gemini Business image metadata query failed; falling back to response session");
            metadata = new Dictionary<string, SessionFileMetadata>(StringComparer.Ordinal);
        }

        var tasks = imageFiles.Select(async f =>
        {
            try
            {
                var sessionForFile = sessionName;
                if (metadata.TryGetValue(f.FileId, out var meta) && !string.IsNullOrWhiteSpace(meta.SessionName))
                {
                    sessionForFile = meta.SessionName!;
                }

                var bytes = await DownloadGeneratedFileAsync(
                    account,
                    credentials,
                    sessionForFile,
                    f.FileId,
                    userAgent,
                    cancellationToken);

                return new DownloadedImage(f.MimeType, Convert.ToBase64String(bytes));
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "Gemini Business image download failed (fileId: {FileId})", f.FileId);
                return null;
            }
        }).ToArray();

        var results = await Task.WhenAll(tasks);
        return results.Where(x => x != null).Select(x => x!).ToList();
    }

    private async Task<Dictionary<string, SessionFileMetadata>> GetSessionGeneratedFileMetadataAsync(
        AIAccount account,
        GeminiBusinessCredentialsDto credentials,
        string sessionName,
        string userAgent,
        CancellationToken cancellationToken)
    {
        async Task<HttpResponseMessage> SendAsync(string jwt)
        {
            var body = new JsonObject
            {
                ["configId"] = credentials.ConfigId,
                ["additionalParams"] = new JsonObject { ["token"] = "-" },
                ["listSessionFileMetadataRequest"] = new JsonObject
                {
                    ["name"] = sessionName,
                    ["filter"] = "file_origin_type = AI_GENERATED"
                }
            };

            var json = body.ToJsonString();
            var request = new HttpRequestMessage(HttpMethod.Post, WidgetListSessionFileMetadataUrl)
            {
                Content = new StringContent(json, Encoding.UTF8, "application/json")
            };
            ApplyCommonHeaders(request, jwt, userAgent);
            return await HttpClient.SendAsync(request, cancellationToken);
        }

        var jwt = await GetJwtAsync(account.Id, credentials, userAgent, cancellationToken, forceRefresh: false);
        using var response = await SendAsync(jwt);
        if (response.StatusCode == HttpStatusCode.Unauthorized)
        {
            jwt = await GetJwtAsync(account.Id, credentials, userAgent, cancellationToken, forceRefresh: true);
            using var retryResponse = await SendAsync(jwt);
            return await ParseSessionFileMetadataAsync(retryResponse, cancellationToken);
        }

        return await ParseSessionFileMetadataAsync(response, cancellationToken);
    }

    private static async Task<Dictionary<string, SessionFileMetadata>> ParseSessionFileMetadataAsync(
        HttpResponseMessage response,
        CancellationToken cancellationToken)
    {
        if (response.StatusCode != HttpStatusCode.OK)
        {
            var error = await response.Content.ReadAsStringAsync(cancellationToken);
            throw new HttpRequestException($"ListSessionFileMetadata failed: {(int)response.StatusCode} {error}");
        }

        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
        using var doc = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken);

        var result = new Dictionary<string, SessionFileMetadata>(StringComparer.Ordinal);

        if (!doc.RootElement.TryGetProperty("listSessionFileMetadataResponse", out var resp)
            || resp.ValueKind != JsonValueKind.Object
            || !resp.TryGetProperty("fileMetadata", out var list)
            || list.ValueKind != JsonValueKind.Array)
        {
            return result;
        }

        foreach (var item in list.EnumerateArray())
        {
            if (item.ValueKind != JsonValueKind.Object)
            {
                continue;
            }

            if (!item.TryGetProperty("fileId", out var fileIdEl) || fileIdEl.ValueKind != JsonValueKind.String)
            {
                continue;
            }

            var fileId = fileIdEl.GetString();
            if (string.IsNullOrWhiteSpace(fileId))
            {
                continue;
            }

            var session = item.TryGetProperty("session", out var sessionEl) && sessionEl.ValueKind == JsonValueKind.String
                ? sessionEl.GetString()
                : null;

            var mime = item.TryGetProperty("mimeType", out var mimeEl) && mimeEl.ValueKind == JsonValueKind.String
                ? mimeEl.GetString()
                : null;

            result[fileId] = new SessionFileMetadata(fileId, session, mime);
        }

        return result;
    }

    private async Task<byte[]> DownloadGeneratedFileAsync(
        AIAccount account,
        GeminiBusinessCredentialsDto credentials,
        string sessionName,
        string fileId,
        string userAgent,
        CancellationToken cancellationToken)
    {
        async Task<HttpResponseMessage> SendAsync(string jwt)
        {
            var url = $"{GeminiBusinessApiBaseUrl}/{sessionName}:downloadFile?fileId={Uri.EscapeDataString(fileId)}&alt=media";
            var request = new HttpRequestMessage(HttpMethod.Get, url);
            ApplyCommonHeaders(request, jwt, userAgent);
            return await HttpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
        }

        var jwt = await GetJwtAsync(account.Id, credentials, userAgent, cancellationToken, forceRefresh: false);
        using var response = await SendAsync(jwt);
        if (response.StatusCode == HttpStatusCode.Unauthorized)
        {
            jwt = await GetJwtAsync(account.Id, credentials, userAgent, cancellationToken, forceRefresh: true);
            using var retryResponse = await SendAsync(jwt);
            return await ReadDownloadBytesAsync(retryResponse, cancellationToken);
        }

        return await ReadDownloadBytesAsync(response, cancellationToken);
    }

    private static async Task<byte[]> ReadDownloadBytesAsync(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        if (response.StatusCode != HttpStatusCode.OK)
        {
            var error = await response.Content.ReadAsStringAsync(cancellationToken);
            throw new HttpRequestException($"DownloadFile failed: {(int)response.StatusCode} {error}");
        }

        return await response.Content.ReadAsByteArrayAsync(cancellationToken);
    }

    private async Task<string> GetJwtAsync(
        int accountId,
        GeminiBusinessCredentialsDto credentials,
        string userAgent,
        CancellationToken cancellationToken,
        bool forceRefresh)
    {
        var entry = JwtCache.GetOrAdd(accountId, _ => new JwtCacheEntry());

        await entry.Lock.WaitAsync(cancellationToken);
        try
        {
            if (!forceRefresh
                && !string.IsNullOrWhiteSpace(entry.Jwt)
                && DateTimeOffset.UtcNow < entry.ExpiresAt)
            {
                return entry.Jwt;
            }

            var jwt = await RefreshJwtAsync(credentials, userAgent, cancellationToken);
            entry.Jwt = jwt;
            entry.ExpiresAt = DateTimeOffset.UtcNow.AddSeconds(270);
            return jwt;
        }
        finally
        {
            entry.Lock.Release();
        }
    }

    private async Task<string> RefreshJwtAsync(
        GeminiBusinessCredentialsDto credentials,
        string userAgent,
        CancellationToken cancellationToken)
    {
        var cookie = $"__Secure-C_SES={credentials.SecureCSes}";
        if (!string.IsNullOrWhiteSpace(credentials.HostCOses))
        {
            cookie += $"; __Host-C_OSES={credentials.HostCOses}";
        }

        var url = $"{GetXsrfUrl}?csesidx={Uri.EscapeDataString(credentials.Csesidx ?? string.Empty)}";
        using var request = new HttpRequestMessage(HttpMethod.Get, url);
        request.Headers.TryAddWithoutValidation("cookie", cookie);
        request.Headers.TryAddWithoutValidation("user-agent", userAgent);
        request.Headers.TryAddWithoutValidation("referer", "https://business.gemini.google/");

        using var response = await HttpClient.SendAsync(request, cancellationToken);
        if (response.StatusCode != HttpStatusCode.OK)
        {
            var error = await response.Content.ReadAsStringAsync(cancellationToken);
            throw new HttpRequestException($"getoxsrf failed: {(int)response.StatusCode} {error}");
        }

        var text = await response.Content.ReadAsStringAsync(cancellationToken);
        if (text.StartsWith(")]}'", StringComparison.Ordinal))
        {
            text = text[4..];
        }

        using var doc = JsonDocument.Parse(text);
        var root = doc.RootElement;

        if (!root.TryGetProperty("xsrfToken", out var xsrfTokenEl)
            || !root.TryGetProperty("keyId", out var keyIdEl))
        {
            throw new InvalidOperationException("getoxsrf response missing xsrfToken/keyId");
        }

        var xsrfToken = xsrfTokenEl.GetString();
        var keyId = keyIdEl.GetString();

        if (string.IsNullOrWhiteSpace(xsrfToken) || string.IsNullOrWhiteSpace(keyId))
        {
            throw new InvalidOperationException("getoxsrf response invalid xsrfToken/keyId");
        }

        var keyBytes = Base64UrlDecode(xsrfToken);
        return CreateJwt(keyBytes, keyId, credentials.Csesidx ?? string.Empty);
    }

    private static byte[] Base64UrlDecode(string input)
    {
        var normalized = input.Replace('-', '+').Replace('_', '/');
        normalized = normalized.PadRight(normalized.Length + (4 - normalized.Length % 4) % 4, '=');
        return Convert.FromBase64String(normalized);
    }

    private static string UrlSafeBase64Encode(byte[] data)
    {
        return Convert.ToBase64String(data)
            .TrimEnd('=')
            .Replace('+', '-')
            .Replace('/', '_');
    }

    private static string KqEncode(string s)
    {
        var bytes = new List<byte>(s.Length);
        foreach (var ch in s)
        {
            var v = (int)ch;
            if (v > 255)
            {
                bytes.Add((byte)(v & 255));
                bytes.Add((byte)(v >> 8));
            }
            else
            {
                bytes.Add((byte)v);
            }
        }

        return UrlSafeBase64Encode(bytes.ToArray());
    }

    private static string CreateJwt(byte[] keyBytes, string keyId, string csesidx)
    {
        var now = DateTimeOffset.UtcNow.ToUnixTimeSeconds();

        var headerJson = BuildJwtHeaderJson(keyId);
        var payloadJson = BuildJwtPayloadJson(csesidx, now);

        var headerB64 = KqEncode(headerJson);
        var payloadB64 = KqEncode(payloadJson);
        var message = $"{headerB64}.{payloadB64}";

        using var hmac = new HMACSHA256(keyBytes);
        var sig = hmac.ComputeHash(Encoding.UTF8.GetBytes(message));
        return $"{message}.{UrlSafeBase64Encode(sig)}";
    }

    private static string BuildJwtHeaderJson(string keyId)
    {
        using var stream = new MemoryStream();
        using (var writer = new Utf8JsonWriter(stream, new JsonWriterOptions { Indented = false }))
        {
            writer.WriteStartObject();
            writer.WriteString("alg", "HS256");
            writer.WriteString("typ", "JWT");
            writer.WriteString("kid", keyId);
            writer.WriteEndObject();
        }

        return Encoding.UTF8.GetString(stream.ToArray());
    }

    private static string BuildJwtPayloadJson(string csesidx, long now)
    {
        using var stream = new MemoryStream();
        using (var writer = new Utf8JsonWriter(stream, new JsonWriterOptions { Indented = false }))
        {
            writer.WriteStartObject();
            writer.WriteString("iss", "https://business.gemini.google");
            writer.WriteString("aud", "https://biz-discoveryengine.googleapis.com");
            writer.WriteString("sub", $"csesidx/{csesidx}");
            writer.WriteNumber("iat", now);
            writer.WriteNumber("exp", now + 300);
            writer.WriteNumber("nbf", now);
            writer.WriteEndObject();
        }

        return Encoding.UTF8.GetString(stream.ToArray());
    }

    private async Task<string> GetOrCreateSessionAsync(
        AIAccount account,
        GeminiBusinessCredentialsDto credentials,
        string? conversationId,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrEmpty(conversationId))
        {
            return await CreateSessionAsync(account, credentials, cancellationToken);
        }

        TrimExpiredSessions();

        if (SessionCache.TryGetValue(conversationId, out var cached)
            && cached.AccountId == account.Id
            && DateTimeOffset.UtcNow - cached.UpdatedAt <= SessionTtl)
        {
            SessionCache[conversationId] = cached with { UpdatedAt = DateTimeOffset.UtcNow };
            return cached.SessionName;
        }

        var sessionName = await CreateSessionAsync(account, credentials, cancellationToken);
        SessionCache[conversationId] = new SessionCacheEntry(account.Id, sessionName, DateTimeOffset.UtcNow);
        return sessionName;
    }

    private async Task<string> CreateSessionAsync(
        AIAccount account,
        GeminiBusinessCredentialsDto credentials,
        CancellationToken cancellationToken)
    {
        var userAgent = GetUserAgent();
        var jwt = await GetJwtAsync(account.Id, credentials, userAgent, cancellationToken, forceRefresh: false);

        var body = new JsonObject
        {
            ["configId"] = credentials.ConfigId,
            ["additionalParams"] = new JsonObject { ["token"] = "-" },
            ["createSessionRequest"] = new JsonObject
            {
                ["session"] = new JsonObject { ["name"] = "", ["displayName"] = "" }
            }
        };

        var payload = body.ToJsonString();

        using var request = new HttpRequestMessage(HttpMethod.Post, WidgetCreateSessionUrl)
        {
            Content = new StringContent(payload, Encoding.UTF8, "application/json")
        };
        ApplyCommonHeaders(request, jwt, userAgent);

        using var response = await HttpClient.SendAsync(request, cancellationToken);
        if (response.StatusCode == HttpStatusCode.Unauthorized)
        {
            jwt = await GetJwtAsync(account.Id, credentials, userAgent, cancellationToken, forceRefresh: true);
            using var retryRequest = new HttpRequestMessage(HttpMethod.Post, WidgetCreateSessionUrl)
            {
                Content = new StringContent(payload, Encoding.UTF8, "application/json")
            };
            ApplyCommonHeaders(retryRequest, jwt, userAgent);
            using var retryResponse = await HttpClient.SendAsync(retryRequest, cancellationToken);
            return await ParseSessionNameAsync(retryResponse, cancellationToken);
        }

        return await ParseSessionNameAsync(response, cancellationToken);
    }

    private static async Task<string> ParseSessionNameAsync(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        if (response.StatusCode != HttpStatusCode.OK)
        {
            var error = await response.Content.ReadAsStringAsync(cancellationToken);
            throw new HttpRequestException($"Create session failed: {(int)response.StatusCode} {error}");
        }

        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
        using var doc = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken);

        if (!doc.RootElement.TryGetProperty("session", out var session)
            || !session.TryGetProperty("name", out var nameEl)
            || nameEl.ValueKind != JsonValueKind.String)
        {
            throw new InvalidOperationException("Create session response missing session.name");
        }

        return nameEl.GetString() ?? throw new InvalidOperationException("Create session response invalid session.name");
    }

    private async Task<HttpResponseMessage> SendStreamAssistRequestAsync(
        AIAccount account,
        GeminiBusinessCredentialsDto credentials,
        string sessionName,
        string text,
        string modelName,
        string userAgent,
        CancellationToken cancellationToken,
        bool forceRefreshJwt = false,
        bool enableImageGeneration = false,
        IReadOnlyList<string>? fileIds = null)
    {
        var jwt = await GetJwtAsync(account.Id, credentials, userAgent, cancellationToken, forceRefreshJwt);

        var toolsSpec = new JsonObject
        {
            ["webGroundingSpec"] = new JsonObject(),
            ["toolRegistry"] = "default_tool_registry"
        };

        if (enableImageGeneration)
        {
            toolsSpec["imageGenerationSpec"] = new JsonObject();
            toolsSpec["videoGenerationSpec"] = new JsonObject();
        }

        var streamAssistRequest = new JsonObject
        {
            ["session"] = sessionName,
            ["query"] = new JsonObject
            {
                ["parts"] = new JsonArray { new JsonObject { ["text"] = text } }
            },
            ["filter"] = "",
            ["fileIds"] = fileIds is { Count: > 0 }
                ? new JsonArray(fileIds.Select(x => JsonValue.Create(x)).ToArray())
                : new JsonArray(),
            ["answerGenerationMode"] = "NORMAL",
            ["toolsSpec"] = toolsSpec,
            ["languageCode"] = "zh-CN",
            ["userMetadata"] = new JsonObject { ["timeZone"] = "Asia/Shanghai" },
            ["assistSkippingMode"] = "REQUEST_ASSIST"
        };

        if (ModelMapping.TryGetValue(modelName, out var modelId) && !string.IsNullOrWhiteSpace(modelId))
        {
            streamAssistRequest["assistGenerationConfig"] = new JsonObject
            {
                ["modelId"] = modelId
            };
        }

        var payload = new JsonObject
        {
            ["configId"] = credentials.ConfigId,
            ["additionalParams"] = new JsonObject { ["token"] = "-" },
            ["streamAssistRequest"] = streamAssistRequest
        };

        var json = payload.ToJsonString(new JsonSerializerOptions
        {
            DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
            WriteIndented = false
        });

        var request = new HttpRequestMessage(HttpMethod.Post, WidgetStreamAssistUrl)
        {
            Content = new StringContent(json, Encoding.UTF8, "application/json")
        };
        ApplyCommonHeaders(request, jwt, userAgent);

        return await HttpClient.SendAsync(
            request,
            HttpCompletionOption.ResponseHeadersRead,
            cancellationToken);
    }

    private static void ApplyCommonHeaders(HttpRequestMessage request, string jwt, string userAgent)
    {
        request.Headers.TryAddWithoutValidation("accept", "*/*");
        request.Headers.TryAddWithoutValidation("accept-language", "zh-CN,zh;q=0.9,en;q=0.8");
        request.Headers.TryAddWithoutValidation("authorization", $"Bearer {jwt}");
        request.Headers.TryAddWithoutValidation("origin", "https://business.gemini.google");
        request.Headers.TryAddWithoutValidation("referer", "https://business.gemini.google/");
        request.Headers.TryAddWithoutValidation("user-agent", userAgent);
        request.Headers.TryAddWithoutValidation("x-server-timeout", "1800");
        request.Headers.TryAddWithoutValidation("sec-fetch-dest", "empty");
        request.Headers.TryAddWithoutValidation("sec-fetch-mode", "cors");
        request.Headers.TryAddWithoutValidation("sec-fetch-site", "cross-site");
    }

    private static GeminiBusinessCredentialsDto? ValidateCredentials(AIAccount account)
    {
        var credentials = account.GetGeminiBusinessOauth();
        if (credentials == null)
        {
            return null;
        }

        if (credentials.Disabled)
        {
            return null;
        }

        if (string.IsNullOrWhiteSpace(credentials.SecureCSes)
            || string.IsNullOrWhiteSpace(credentials.Csesidx)
            || string.IsNullOrWhiteSpace(credentials.ConfigId))
        {
            return null;
        }

        return credentials;
    }

    private async Task<(AIAccount? Account, bool SessionStickinessUsed)> TryResolveAccountAsync(
        AIAccountService aiAccountService,
        string? conversationId,
        CancellationToken cancellationToken)
    {
        AIAccount? account = null;
        var sessionStickinessUsed = false;

        if (!string.IsNullOrEmpty(conversationId))
        {
            var lastAccountId = quotaCache.GetConversationAccount(conversationId);
            if (lastAccountId.HasValue)
            {
                var sticky = await aiAccountService.TryGetAccountById(lastAccountId.Value);
                if (sticky is { Provider: AIProviders.GeminiBusiness })
                {
                    account = sticky;
                    sessionStickinessUsed = true;
                }
            }
        }

        account ??= await aiAccountService.GetAIAccountByProvider(AIProviders.GeminiBusiness);
        return (account, sessionStickinessUsed);
    }

    private static int? TryParseRetryAfterSeconds(HttpResponseMessage response)
    {
        if (response.Headers.TryGetValues("retry-after", out var values))
        {
            var value = values.FirstOrDefault();
            if (int.TryParse(value, out var seconds))
            {
                return seconds;
            }
        }

        return null;
    }

    private static async Task HandleUpstreamErrorAsync(
        HttpResponseMessage response,
        AIAccountService aiAccountService,
        int accountId)
    {
        if (response.StatusCode is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden)
        {
            await aiAccountService.DisableAccount(accountId);
            return;
        }

        if (response.StatusCode == (HttpStatusCode)429)
        {
            var retryAfter = TryParseRetryAfterSeconds(response) ?? 120;
            await aiAccountService.MarkAccountAsRateLimited(accountId, retryAfter);
        }
    }

    private static string CreateOpenAiChunk(string id, long created, string model, JsonObject delta, string? finishReason)
    {
        var chunk = new JsonObject
        {
            ["id"] = id,
            ["object"] = "chat.completion.chunk",
            ["created"] = created,
            ["model"] = model,
            ["choices"] = new JsonArray
            {
                new JsonObject
                {
                    ["index"] = 0,
                    ["delta"] = delta,
                    ["logprobs"] = null,
                    ["finish_reason"] = finishReason
                }
            },
            ["system_fingerprint"] = null
        };

        return chunk.ToJsonString();
    }

    private static JsonObject BuildGeminiGenerateContentResponse(
        string content,
        string reasoningContent,
        IReadOnlyList<DownloadedImage> images)
    {
        var parts = new JsonArray();
        if (!string.IsNullOrEmpty(content))
        {
            parts.Add(new JsonObject { ["text"] = content });
        }

        if (!string.IsNullOrWhiteSpace(reasoningContent))
        {
            parts.Add(new JsonObject { ["thought"] = true, ["text"] = reasoningContent });
        }

        foreach (var image in images)
        {
            parts.Add(new JsonObject
            {
                ["inlineData"] = new JsonObject
                {
                    ["mimeType"] = image.MimeType,
                    ["data"] = image.Base64Data
                }
            });
        }

        return new JsonObject
        {
            ["candidates"] = new JsonArray
            {
                new JsonObject
                {
                    ["content"] = new JsonObject
                    {
                        ["role"] = "model",
                        ["parts"] = parts
                    },
                    ["finishReason"] = "STOP"
                }
            }
        };
    }

    private static string BuildGeminiStreamChunk(string text, bool isThought = false)
    {
        var part = isThought
            ? new JsonObject { ["thought"] = true, ["text"] = text }
            : new JsonObject { ["text"] = text };

        var node = new JsonObject
        {
            ["candidates"] = new JsonArray
            {
                new JsonObject
                {
                    ["content"] = new JsonObject
                    {
                        ["role"] = "model",
                        ["parts"] = new JsonArray { part }
                    }
                }
            }
        };
        return node.ToJsonString();
    }

    private static string BuildGeminiStreamFinalChunk()
    {
        var node = new JsonObject
        {
            ["candidates"] = new JsonArray
            {
                new JsonObject
                {
                    ["content"] = new JsonObject
                    {
                        ["role"] = "model",
                        ["parts"] = new JsonArray()
                    },
                    ["finishReason"] = "STOP"
                }
            }
        };
        return node.ToJsonString();
    }

    private static string BuildGeminiStreamInlineDataChunk(DownloadedImage image)
    {
        var node = new JsonObject
        {
            ["candidates"] = new JsonArray
            {
                new JsonObject
                {
                    ["content"] = new JsonObject
                    {
                        ["role"] = "model",
                        ["parts"] = new JsonArray
                        {
                            new JsonObject
                            {
                                ["inlineData"] = new JsonObject
                                {
                                    ["mimeType"] = image.MimeType,
                                    ["data"] = image.Base64Data
                                }
                            }
                        }
                    }
                }
            }
        };

        return node.ToJsonString();
    }

    private static async Task WriteSseLineAsync(HttpContext context, string line, CancellationToken cancellationToken)
    {
        await context.Response.WriteAsync(line + "\n", cancellationToken);
        await context.Response.Body.FlushAsync(cancellationToken);
    }

    private static async Task WriteOpenAiError(HttpContext context, int statusCode, string message)
    {
        context.Response.StatusCode = statusCode;
        context.Response.ContentType = "application/json; charset=utf-8";

        var node = new JsonObject
        {
            ["error"] = new JsonObject
            {
                ["message"] = message,
                ["type"] = "api_error"
            }
        };

        await context.Response.WriteAsync(node.ToJsonString());
    }

    private static string ExtractText(ThorChatMessage message)
    {
        if (!string.IsNullOrWhiteSpace(message.Content))
        {
            return message.Content!;
        }

        if (message.Contents is not { Count: > 0 })
        {
            return string.Empty;
        }

        var sb = new StringBuilder();
        foreach (var part in message.Contents)
        {
            if (part.Type == ThorMessageContentTypeConst.Text && !string.IsNullOrWhiteSpace(part.Text))
            {
                sb.Append(part.Text);
            }
        }

        return sb.ToString();
    }

    private static string BuildFullContextText(IReadOnlyList<ThorChatMessage> messages)
    {
        var sb = new StringBuilder();
        foreach (var msg in messages)
        {
            var role = msg.Role;
            var prefix = role == ThorChatMessageRoleConst.Assistant ? "Assistant" : "User";
            if (role == ThorChatMessageRoleConst.Tool)
            {
                prefix = "User";
            }

            var content = ExtractText(msg);
            sb.Append(prefix).Append(": ").Append(content).Append("\n\n");
        }

        return sb.ToString();
    }

    private static string ExtractText(GeminiContents content)
    {
        var sb = new StringBuilder();
        foreach (var part in content.Parts ?? Array.Empty<object>())
        {
            if (part is JsonElement el
                && el.ValueKind == JsonValueKind.Object
                && el.TryGetProperty("text", out var textEl)
                && textEl.ValueKind == JsonValueKind.String)
            {
                sb.Append(textEl.GetString());
            }
        }

        return sb.ToString();
    }

    private static string BuildFullContextText(GeminiInput input)
    {
        var sb = new StringBuilder();

        if (input.SystemInstruction?.Parts is { Length: > 0 })
        {
            foreach (var part in input.SystemInstruction.Parts)
            {
                if (part is JsonElement el
                    && el.ValueKind == JsonValueKind.Object
                    && el.TryGetProperty("text", out var textEl)
                    && textEl.ValueKind == JsonValueKind.String)
                {
                    sb.Append("User: ").Append(textEl.GetString()).Append("\n\n");
                }
            }
        }

        foreach (var c in input.Contents ?? Array.Empty<GeminiContents>())
        {
            var role = string.Equals(c.Role, "model", StringComparison.OrdinalIgnoreCase) ? "Assistant" : "User";
            sb.Append(role).Append(": ").Append(ExtractText(c)).Append("\n\n");
        }

        return sb.ToString();
    }

    private static string ExtractLastText(GeminiInput input)
    {
        if (input.Contents is not { Length: > 0 })
        {
            return string.Empty;
        }

        for (var i = input.Contents.Length - 1; i >= 0; i--)
        {
            var c = input.Contents[i];
            if (!string.Equals(c.Role, "user", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            var text = ExtractText(c);
            if (!string.IsNullOrWhiteSpace(text))
            {
                return text;
            }
        }

        return ExtractText(input.Contents[^1]);
    }
}
