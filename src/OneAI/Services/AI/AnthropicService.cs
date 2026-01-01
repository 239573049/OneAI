using System.Net;
using System.Text;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Net.Security;
using System.Security.Authentication;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Linq;
using OneAI.Constants;
using OneAI.Entities;
using OneAI.Services.AI.Anthropic;
using OneAI.Services.ClaudeCodeOAuth;
using OneAI.Services.GeminiOAuth;
using OneAI.Services.Logging;
using Thor.Abstractions.Anthropic;

namespace OneAI.Services.AI;

public class AnthropicService(
    AccountQuotaCacheService quotaCache,
    AIRequestLogService requestLogService,
    ILogger<AnthropicService> logger,
    IConfiguration configuration,
    IModelMappingService modelMappingService,
    ClaudeCodeOAuthService claudeCodeOAuthService,
    GeminiAntigravityOAuthService geminiAntigravityOAuthService)
{
    private static readonly HttpClient HttpClient = CreateHttpClient();

    private static readonly bool SkipTlsValidation =
        Environment.GetEnvironmentVariable("ANTIGRAVITY_SKIP_TLS_VALIDATE")
            ?.Equals("true", StringComparison.OrdinalIgnoreCase) == true;

    private const int MaxRetries = 15;

    private static readonly string[] ClientErrorKeywords =
    [
        "invalid_request_error",
        "missing_required_parameter"
    ];

    private static HttpClient CreateHttpClient()
    {
        var handler = new SocketsHttpHandler
        {
            AutomaticDecompression = DecompressionMethods.All,
            PooledConnectionLifetime = TimeSpan.FromMinutes(10),
            AllowAutoRedirect = false,
            SslOptions = new SslClientAuthenticationOptions
            {
                EnabledSslProtocols = SslProtocols.Tls12 | SslProtocols.Tls13,
                RemoteCertificateValidationCallback = SkipTlsValidation
                    ? new RemoteCertificateValidationCallback((_, _, _, _) => true)
                    : null
            }
        };

        var client = new HttpClient(handler)
        {
            Timeout = TimeSpan.FromMinutes(30),
            DefaultRequestVersion = HttpVersion.Version11,
            DefaultVersionPolicy = HttpVersionPolicy.RequestVersionOrLower
        };

        client.DefaultRequestHeaders.AcceptEncoding.Add(new StringWithQualityHeaderValue("gzip"));
        client.DefaultRequestHeaders.AcceptEncoding.Add(new StringWithQualityHeaderValue("deflate"));

        return client;
    }

    public async Task Execute(HttpContext context, AnthropicInput input, AIAccountService aiAccountService)
    {
        if (string.IsNullOrWhiteSpace(input.Model) || input.MaxTokens == null || input.Messages == null)
        {
            await WriteAnthropicError(
                context,
                StatusCodes.Status400BadRequest,
                "缺少必填字段：model / max_tokens / messages",
                "invalid_request_error");
            return;
        }

        AIProviderAsyncLocal.AIProviderIds = new List<int>();
        var (logId, stopwatch) = await requestLogService.CreateRequestLog(
            context,
            input.Model,
            input.Stream,
            null,
            false);

        string? lastErrorMessage = null;
        HttpStatusCode? lastStatusCode = null;

        var returnThoughts = configuration.GetValue("Antigravity:ReturnThoughts", true);
        var estimatedInputTokens = EstimateInputTokens(input);
        var conversationId = BuildConversationStickyKey(input);
        var isClaudeCodeRequest = IsClaudeCodeRequest(context);
        var preferredProvider = isClaudeCodeRequest ? AIProviders.Claude : AIProviders.GeminiAntigravity;

        try
        {
            for (var attempt = 1; attempt <= MaxRetries; attempt++)
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

                logger.LogDebug(
                    "尝试第 {Attempt}/{MaxRetries} 次获取账户 (首选: {PreferredProvider})",
                    attempt,
                    MaxRetries,
                    preferredProvider);

                var account = await GetClaudeOrAntigravityAccount(
                    conversationId,
                    preferredProvider,
                    aiAccountService);

                if (account == null)
                {
                    const string noAccountMessage = "账户池都无可用";
                    logger.LogWarning(noAccountMessage);

                    await requestLogService.RecordFailure(
                        logId,
                        stopwatch,
                        StatusCodes.Status503ServiceUnavailable,
                        noAccountMessage);

                    await WriteAnthropicError(
                        context,
                        StatusCodes.Status503ServiceUnavailable,
                        noAccountMessage,
                        "api_error");

                    return;
                }

                AIProviderAsyncLocal.AIProviderIds.Add(account.Id);
                await requestLogService.UpdateRetry(logId, attempt, account.Id);

                if (account.Provider == AIProviders.Claude)
                {
                    var claudeOauth = account.GetClaudeOauth();

                    if (claudeOauth == null || string.IsNullOrWhiteSpace(claudeOauth.AccessToken))
                    {
                        lastErrorMessage = $"账户 {account.Id} 没有有效的 Claude Oauth 凭证";
                        lastStatusCode = HttpStatusCode.Unauthorized;
                        logger.LogWarning(lastErrorMessage);

                        await aiAccountService.DisableAccount(account.Id);

                        if (attempt < MaxRetries)
                        {
                            continue;
                        }

                        break;
                    }

                    if (IsClaudeTokenExpired(claudeOauth))
                    {
                        try
                        {
                            await claudeCodeOAuthService.RefreshClaudeOAuthTokenAsync(account);
                        }
                        catch (Exception ex)
                        {
                            logger.LogWarning(
                                ex,
                                "Claude 账户 {AccountId} Token 已过期且刷新失败，已禁用账户",
                                account.Id);

                            lastErrorMessage = $"账户 {account.Id} Token 刷新失败: {ex.Message}";
                            lastStatusCode = HttpStatusCode.Unauthorized;

                            await aiAccountService.DisableAccount(account.Id);

                            if (attempt < MaxRetries)
                            {
                                continue;
                            }

                            break;
                        }

                        claudeOauth = account.GetClaudeOauth();
                        if (claudeOauth == null || string.IsNullOrWhiteSpace(claudeOauth.AccessToken))
                        {
                            lastErrorMessage = $"账户 {account.Id} Claude Token 刷新后仍无效";
                            lastStatusCode = HttpStatusCode.Unauthorized;
                            logger.LogWarning(lastErrorMessage);

                            await aiAccountService.DisableAccount(account.Id);

                            if (attempt < MaxRetries)
                            {
                                continue;
                            }

                            break;
                        }
                    }

                    // 准备请求头和代理配置
                    var headers = new Dictionary<string, string>
                    {
                        { "Authorization", "Bearer " + claudeOauth.AccessToken },
                        { "anthropic-version", "2023-06-01" }
                    };

                    if (isClaudeCodeRequest)
                    {
                        // 复制context的请求头
                        foreach (var header in context.Request.Headers)
                        {
                            // 不要覆盖已有的Authorization和Content-Type头
                            if (header.Key.Equals("Authorization", StringComparison.OrdinalIgnoreCase) ||
                                header.Key.Equals("Content-Type", StringComparison.OrdinalIgnoreCase) ||
                                header.Key.Equals("Content-Length", StringComparison.OrdinalIgnoreCase) ||
                                header.Key.Equals("Connection", StringComparison.OrdinalIgnoreCase) ||
                                header.Key.Equals("Host", StringComparison.OrdinalIgnoreCase) ||
                                header.Key.Equals("x-api-key", StringComparison.OrdinalIgnoreCase))
                                continue;

                            headers[header.Key] = header.Value.ToString();
                        }

                        headers["Host"] = "api.anthropic.com";
                    }
                    else
                    {
                        headers["User-Agent"] = "claude-cli/2.0.5 (external, cli)";
                        headers["Accept-Encoding"] = "gzip, deflate, br";
                        headers["Accept-Language"] = "*";
                        headers["X-Stainless-Retry-Count"] = "0";
                        headers["x-Stainless-Timeout"] = "60";
                        headers["X-Stainless-Lang"] = "js";
                        headers["X-Stainless-Package-Version"] = "0.55.1";
                        headers["X-Stainless-OS"] = "Windows";
                        headers["X-Stainless-Arch"] = "x64";
                        headers["X-Stainless-Runtime"] = "node";
                        headers["X-Stainless-Runtime-Version"] = "v22.15.0";
                        headers["anthropic-dangerous-direct-browser-access"] = "true";
                        headers["x-app"] = "cli";
                        headers["sec-fetch-mode"] = "cors";
                        headers["sec-fetch-site"] = "cross-site";
                        headers["sec-fetch-dest"] = "empty";
                        headers["anthropic-beta"] =
                            "oauth-2025-04-20,claude-code-20250219,interleaved-thinking-2025-05-14,fine-grained-tool-streaming-2025-05-14";
                    }

                    using var request =
                        new HttpRequestMessage(HttpMethod.Post, BuildClaudeMessagesUrl(account.BaseUrl));
                    foreach (var header in headers)
                    {
                        request.Headers.Add(header.Key, header.Value);
                    }

                    var json = JsonSerializer.Serialize(input, new JsonSerializerOptions
                    {
                        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
                    });

                    request.Content = new StringContent(json, Encoding.UTF8, "application/json");

                    using var response = await HttpClient.SendAsync(
                        request,
                        input.Stream
                            ? HttpCompletionOption.ResponseHeadersRead
                            : HttpCompletionOption.ResponseContentRead,
                        context.RequestAborted);

                    if (response.StatusCode >= HttpStatusCode.BadRequest)
                    {
                        var error = await response.Content.ReadAsStringAsync(context.RequestAborted);
                        lastErrorMessage = error;
                        lastStatusCode = response.StatusCode;

                        logger.LogError(
                            "Claude 请求失败 (账户: {AccountId}, 状态码: {StatusCode}, 尝试: {Attempt}/{MaxRetries}): {Error}",
                            account.Id,
                            response.StatusCode,
                            attempt,
                            MaxRetries,
                            error);

                        if (response.StatusCode is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden)
                        {
                            await aiAccountService.DisableAccount(account.Id);
                        }

                        var isClientError =
                            response.StatusCode == HttpStatusCode.BadRequest
                            || ClientErrorKeywords.Any(keyword =>
                                error.Contains(keyword, StringComparison.OrdinalIgnoreCase));

                        if (isClientError)
                        {
                            await requestLogService.RecordFailure(
                                logId,
                                stopwatch,
                                (int)response.StatusCode,
                                error);

                            await WriteAnthropicError(
                                context,
                                (int)response.StatusCode,
                                error,
                                "invalid_request_error");

                            return;
                        }

                        if (attempt < MaxRetries)
                        {
                            continue;
                        }

                        break;
                    }

                    if (!string.IsNullOrEmpty(conversationId))
                    {
                        quotaCache.SetConversationAccount(conversationId, account.Id);
                    }

                    if (input.Stream)
                    {
                        await requestLogService.RecordSuccess(
                            logId,
                            stopwatch,
                            (int)response.StatusCode,
                            timeToFirstByteMs: stopwatch.ElapsedMilliseconds);

                        context.Response.StatusCode = (int)response.StatusCode;
                        context.Response.ContentType = "text/event-stream;charset=utf-8";
                        context.Response.Headers.TryAdd("Cache-Control", "no-cache");
                        context.Response.Headers.TryAdd("Connection", "keep-alive");
                        context.Response.Headers.TryAdd("X-Accel-Buffering", "no");

                        await context.Response.Body.FlushAsync(context.RequestAborted);

                        await using var stream = await response.Content.ReadAsStreamAsync(context.RequestAborted);
                        using var reader = new StreamReader(stream, Encoding.UTF8);

                        while (await reader.ReadLineAsync().ConfigureAwait(false) is { } line)
                        {
                            await context.Response.WriteAsync(line).ConfigureAwait(false);
                            if (line.StartsWith("data:"))
                            {
                                await context.Response.Body.WriteAsync(OpenAIConstant.DoubleNewLine);
                            }
                            else
                            {
                                await context.Response.Body.WriteAsync(OpenAIConstant.NewLine);
                            }

                            await context.Response.Body.FlushAsync(context.RequestAborted);
                        }

                        return;
                    }

                    var body = await response.Content.ReadAsStringAsync(context.RequestAborted);

                    await requestLogService.RecordSuccess(
                        logId,
                        stopwatch,
                        (int)response.StatusCode,
                        timeToFirstByteMs: stopwatch.ElapsedMilliseconds);

                    context.Response.StatusCode = (int)response.StatusCode;
                    context.Response.ContentType =
                        response.Content.Headers.ContentType?.ToString() ?? "application/json";
                    await context.Response.WriteAsync(body, Encoding.UTF8);
                    return;
                }
                else if (account.Provider == AIProviders.Factory)
                {
                    var factory = account.GetFactoryOauth();

                    if (factory == null || string.IsNullOrWhiteSpace(factory.AccessToken))
                    {
                        lastErrorMessage = $"账户 {account.Id} 没有有效的 Factory Oauth 凭证";
                        lastStatusCode = HttpStatusCode.Unauthorized;
                        logger.LogWarning(lastErrorMessage);

                        await aiAccountService.DisableAccount(account.Id);

                        if (attempt < MaxRetries)
                        {
                            continue;
                        }

                        break;
                    }

                    if (input.Systems != null)
                    {
                        // Factory AI 特殊处理
                        foreach (var system in input.Systems)
                        {
                            if (system.Text?.Equals("You are Claude Code, Anthropic's official CLI for Claude.",
                                    StringComparison.OrdinalIgnoreCase) == true || system.Text?.Equals(
                                    "You are a Claude agent, built on Anthropic's Claude Agent SDK.",
                                    StringComparison.OrdinalIgnoreCase) == true)
                            {
                                system.Text = "You are Droid, an AI software engineering agent built by Factory.";

                                // 在"You are Droid, an AI software engineering agent built by Factory."后面添加一句claude cli提示，注意
                                var claudeCliPrompt = new AnthropicMessageContent
                                {
                                    Type = "text",
                                    Text =
                                        "You work within an interactive cli tool and you are focused on helping users with any software engineering tasks.\nGuidelines:\n- Use tools when necessary.\n- Don't stop until all user tasks are completed.\n- Never use emojis in replies unless specifically requested by the user.\n- Only add absolutely necessary comments to the code you generate.\n- Your replies should be concise and you should preserve users tokens.\n- Never create or update documentations and readme files unless specifically requested by the user.\n- Replies must be concise but informative, try to fit the answer into less than 1-4 sentences not counting tools usage and code generation.\n- Never retry tool calls that were cancelled by the user, unless user explicitly asks you to do so.\n- Use FetchUrl to fetch Factory docs (https://docs.factory.ai/factory-docs-map.md) when:\n  - Asks questions in the second person (eg. \"are you able...\", \"can you do...\")\n  - User asks about Droid capabilities or features\n  - User needs help with Droid commands, configuration, or settings\n  - User asks about skills, MCP, hooks, custom droids, BYOK, or other Factory specific features\nFocus on the task at hand, don't try to jump to related but not requested tasks.\nOnce you are done with the task, you can summarize the changes you made in a 1-4 sentences, don't go into too much detail.\nIMPORTANT: do not stop until user requests are fulfilled, but be mindful of the token usage.\n\nResponse Guidelines - Do exactly what the user asks, no more, no less:\n\nExamples of correct responses:\n- User: \"read file X\" → Use Read tool, then provide minimal summary of what was found\n- User: \"list files in directory Y\" → Use LS tool, show results with brief context\n- User: \"search for pattern Z\" → Use Grep tool, present findings concisely\n- User: \"create file A with content B\" → Use Create tool, confirm creation\n- User: \"edit line 5 in file C to say D\" → Use Edit tool, confirm change made\n\nExamples of what NOT to do:\n- Don't suggest additional improvements unless asked\n- Don't explain alternatives unless the user asks \"how should I...\"\n- Don't add extra analysis unless specifically requested\n- Don't offer to do related tasks unless the user asks for suggestions\n- No hacks. No unreasonable shortcuts.\n- Do not give up if you encounter unexpected problems. Reason about alternative solutions and debug systematically to get back on track.\nDon't immediately jump into the action when user asks how to approach a task, first try to explain the approach, then ask if user wants you to proceed with the implementation.\nIf user asks you to do something in a clear way, you can proceed with the implementation without asking for confirmation.\nCoding conventions:\n- Never start coding without figuring out the existing codebase structure and conventions.\n- When editing a code file, pay attention to the surrounding code and try to match the existing coding style.\n- Follow approaches and use already used libraries and patterns. Always check that a given library is already installed in the project before using it. Even most popular libraries can be missing in the project.\n- Be mindful about all security implications of the code you generate, never expose any sensitive data and user secrets or keys, even in logs.\n- Before ANY git commit or push operation:\n    - Run 'git diff --cached' to review ALL changes being committed\n    - Run 'git status' to confirm all files being included\n    - Examine the diff for secrets, credentials, API keys, or sensitive data (especially in config files, logs, environment files, and build outputs) \n    - if detected, STOP and warn the user\nTesting and verification:\nBefore completing the task, always verify that the code you generated works as expected. Explore project documentation and scripts to find how lint, typecheck and unit tests are run. Make sure to run all of them before completing the task, unless user explicitly asks you not to do so. Make sure to fix all diagnostics and errors that you see in the system reminder messages <system-reminder>. System reminders will contain relevant contextual information gathered for your consideration.",
                                };
                                input.Systems.Insert(1, claudeCliPrompt);
                                break;
                            }
                        }
                    }
                    else
                    {
                        input.Systems = new List<AnthropicMessageContent>
                        {
                            new AnthropicMessageContent
                            {
                                Type = "text",
                                Text = "You are Droid, an AI software engineering agent built by Factory."
                            },
                            new AnthropicMessageContent
                            {
                                Type = "text",
                                Text =
                                    "You work within an interactive cli tool and you are focused on helping users with any software engineering tasks.\nGuidelines:\n- Use tools when necessary.\n- Don't stop until all user tasks are completed.\n- Never use emojis in replies unless specifically requested by the user.\n- Only add absolutely necessary comments to the code you generate.\n- Your replies should be concise and you should preserve users tokens.\n- Never create or update documentations and readme files unless specifically requested by the user.\n- Replies must be concise but informative, try to fit the answer into less than 1-4 sentences not counting tools usage and code generation.\n- Never retry tool calls that were cancelled by the user, unless user explicitly asks you to do so.\n- Use FetchUrl to fetch Factory docs (https://docs.factory.ai/factory-docs-map.md) when:\n  - Asks questions in the second person (eg. \"are you able...\", \"can you do...\")\n  - User asks about Droid capabilities or features\n  - User needs help with Droid commands, configuration, or settings\n  - User asks about skills, MCP, hooks, custom droids, BYOK, or other Factory specific features\nFocus on the task at hand, don't try to jump to related but not requested tasks.\nOnce you are done with the task, you can summarize the changes you made in a 1-4 sentences, don't go into too much detail.\nIMPORTANT: do not stop until user requests are fulfilled, but be mindful of the token usage.\n\nResponse Guidelines - Do exactly what the user asks, no more, no less:\n\nExamples of correct responses:\n- User: \"read file X\" → Use Read tool, then provide minimal summary of what was found\n- User: \"list files in directory Y\" → Use LS tool, show results with brief context\n- User: \"search for pattern Z\" → Use Grep tool, present findings concisely\n- User: \"create file A with content B\" → Use Create tool, confirm creation\n- User: \"edit line 5 in file C to say D\" → Use Edit tool, confirm change made\n\nExamples of what NOT to do:\n- Don't suggest additional improvements unless asked\n- Don't explain alternatives unless the user asks \"how should I...\"\n- Don't add extra analysis unless specifically requested\n- Don't offer to do related tasks unless the user asks for suggestions\n- No hacks. No unreasonable shortcuts.\n- Do not give up if you encounter unexpected problems. Reason about alternative solutions and debug systematically to get back on track.\nDon't immediately jump into the action when user asks how to approach a task, first try to explain the approach, then ask if user wants you to proceed with the implementation.\nIf user asks you to do something in a clear way, you can proceed with the implementation without asking for confirmation.\nCoding conventions:\n- Never start coding without figuring out the existing codebase structure and conventions.\n- When editing a code file, pay attention to the surrounding code and try to match the existing coding style.\n- Follow approaches and use already used libraries and patterns. Always check that a given library is already installed in the project before using it. Even most popular libraries can be missing in the project.\n- Be mindful about all security implications of the code you generate, never expose any sensitive data and user secrets or keys, even in logs.\n- Before ANY git commit or push operation:\n    - Run 'git diff --cached' to review ALL changes being committed\n    - Run 'git status' to confirm all files being included\n    - Examine the diff for secrets, credentials, API keys, or sensitive data (especially in config files, logs, environment files, and build outputs) \n    - if detected, STOP and warn the user\nTesting and verification:\nBefore completing the task, always verify that the code you generated works as expected. Explore project documentation and scripts to find how lint, typecheck and unit tests are run. Make sure to run all of them before completing the task, unless user explicitly asks you not to do so. Make sure to fix all diagnostics and errors that you see in the system reminder messages <system-reminder>. System reminders will contain relevant contextual information gathered for your consideration.",
                            }
                        };
                    }
                    // if (IsClaudeTokenExpired(claudeOauth))
                    // {
                    //     try
                    //     {
                    //         await claudeCodeOAuthService.RefreshClaudeOAuthTokenAsync(account);
                    //     }
                    //     catch (Exception ex)
                    //     {
                    //         logger.LogWarning(
                    //             ex,
                    //             "Claude 账户 {AccountId} Token 已过期且刷新失败，已禁用账户",
                    //             account.Id);
                    //
                    //         lastErrorMessage = $"账户 {account.Id} Token 刷新失败: {ex.Message}";
                    //         lastStatusCode = HttpStatusCode.Unauthorized;
                    //
                    //         await aiAccountService.DisableAccount(account.Id);
                    //
                    //         if (attempt < MaxRetries)
                    //         {
                    //             continue;
                    //         }
                    //
                    //         break;
                    //     }
                    //
                    //     claudeOauth = account.GetClaudeOauth();
                    //     if (claudeOauth == null || string.IsNullOrWhiteSpace(claudeOauth.AccessToken))
                    //     {
                    //         lastErrorMessage = $"账户 {account.Id} Claude Token 刷新后仍无效";
                    //         lastStatusCode = HttpStatusCode.Unauthorized;
                    //         logger.LogWarning(lastErrorMessage);
                    //
                    //         await aiAccountService.DisableAccount(account.Id);
                    //
                    //         if (attempt < MaxRetries)
                    //         {
                    //             continue;
                    //         }
                    //
                    //         break;
                    //     }
                    // }

                    // 准备请求头和代理配置
                    var headers = new Dictionary<string, string>
                    {
                        { "Authorization", "Bearer " + factory.AccessToken },
                    };

                    headers["User-Agent"] = "factory-cli/0.41.0";
                    headers["Accept-Encoding"] = "gzip, deflate, br";
                    headers["Accept-Language"] = "*";
                    headers["accept"] = "text/event-stream";
                    headers["x-factory-client"] = "cli";
                    headers["x-session-id"] = Guid.NewGuid().ToString();
                    headers["x-assistant-message-id"] = Guid.NewGuid().ToString();
                    headers["x-factory-client"] = "cli";
                    headers["x-stainless-arch"] = "x64";
                    headers["x-stainless-helper-method"] = "stream";
                    headers["x-stainless-lang"] = "js";
                    headers["x-stainless-os"] = "Windows";
                    headers["x-stainless-package-version"] = "0.70.0";
                    headers["x-stainless-retry-count"] = "0";
                    headers["x-stainless-runtime"] = "node";
                    headers["x-stainless-runtime-version"] = "v24.3.0";
                    headers["anthropic-dangerous-direct-browser-access"] = "true";
                    headers["anthropic-beta"] =
                        "oauth-2025-04-20,claude-code-20250219,interleaved-thinking-2025-05-14,fine-grained-tool-streaming-2025-05-14";

                    using var request =
                        new HttpRequestMessage(HttpMethod.Post, "https://app.factory.ai/api/llm/a/v1/messages");
                    foreach (var header in headers)
                    {
                        request.Headers.Add(header.Key, header.Value);
                    }

                    foreach (var message in input.Messages)
                    {
                        if (message.Contents?.Count > 0)
                        {
                            foreach (var messageContent in message.Contents.Where(x =>
                                         x.Type == "text"))
                            {
                                if (messageContent.Text?.StartsWith("<system-reminder>\nThis is a reminder") == true)
                                {
                                    messageContent.Text = messageContent.Text.Replace(
                                        "<system-reminder>\nThis is a reminder that your todo list is currently empty. DO NOT mention this to the user explicitly because they are already aware. If you are working on tasks that would benefit from a todo list please use the TodoWrite tool to create one. If not, please feel free to ignore. Again do not mention this message to the user.\n</system-reminder>",
                                        "<system-reminder>\n这是一个提醒，您的待办事项列表目前为空。不要明确告诉用户这一点，因为他们已经知道。如果您正在处理的任务需要待办事项列表，请使用 TodoWrite 工具创建一个。如果不需要，请随意忽略。再次提醒，不要向用户提及此消息。\n</system-reminder>");
                                }
                                else if (messageContent.Text?.StartsWith(
                                             "<system-reminder>\nCalled the Read tool with the following input:") ==
                                         true)
                                {
                                    messageContent.Text = messageContent.Text.Replace(
                                        "<system-reminder>\nCalled the Read tool with the following input:",
                                        "<system-reminder>\n使用以下输入调用了读取工具：");
                                }
                                else if (messageContent.Text?.StartsWith(
                                             "<system-reminder>\nResult of calling the Read tool:") ==
                                         true)
                                {
                                    messageContent.Text = messageContent.Text.Replace(
                                        "<system-reminder>\nResult of calling the Read tool:",
                                        "<system-reminder>\n调用读取工具的结果：");
                                }
                                else if (messageContent.Text?.StartsWith(
                                             "<system-reminder>\nAs you answer the user's questions, you can use the following context:\n# claudeMd\nCodebase and user instructions are shown below. Be sure to adhere to these instructions. IMPORTANT: These instructions OVERRIDE any default behavior and you MUST follow them exactly as written.\n\nContents of ") ==
                                         true)
                                {
                                    messageContent.Text = messageContent.Text.Replace(
                                        "<system-reminder>\nAs you answer the user's questions, you can use the following context:\n# claudeMd\nCodebase and user instructions are shown below. Be sure to adhere to these instructions. IMPORTANT: These instructions OVERRIDE any default behavior and you MUST follow them exactly as written.\n\nContents of ",
                                        "<system-reminder>\n在回答用户问题时，您可以使用以下上下文：\n# claudeMd\n代码库和用户说明如下。务必遵守这些说明。重要提示：这些说明优先于任何默认行为，您必须严格按照说明执行。\n\n内容如下");
                                }
                            }
                        }
                    }

                    request.Headers.Referrer = new Uri("https://app.factory.ai/");

                    var json = JsonSerializer.Serialize(input, new JsonSerializerOptions
                    {
                        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
                    });

                    request.Content = new StringContent(json, Encoding.UTF8, "application/json");

                    using var response = await HttpClient.SendAsync(
                        request,
                        input.Stream
                            ? HttpCompletionOption.ResponseHeadersRead
                            : HttpCompletionOption.ResponseContentRead,
                        context.RequestAborted);

                    if (response.StatusCode >= HttpStatusCode.BadRequest)
                    {
                        var error = await response.Content.ReadAsStringAsync(context.RequestAborted);
                        lastErrorMessage = error;
                        lastStatusCode = response.StatusCode;

                        logger.LogError(
                            "Claude 请求失败 (账户: {AccountId}, 状态码: {StatusCode}, 尝试: {Attempt}/{MaxRetries}): {Error}",
                            account.Id,
                            response.StatusCode,
                            attempt,
                            MaxRetries,
                            error);

                        if (response.StatusCode is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden)
                        {
                            await aiAccountService.DisableAccount(account.Id);
                        }

                        var isClientError =
                            response.StatusCode == HttpStatusCode.BadRequest
                            || ClientErrorKeywords.Any(keyword =>
                                error.Contains(keyword, StringComparison.OrdinalIgnoreCase));

                        if (isClientError)
                        {
                            await requestLogService.RecordFailure(
                                logId,
                                stopwatch,
                                (int)response.StatusCode,
                                error);

                            await WriteAnthropicError(
                                context,
                                (int)response.StatusCode,
                                error,
                                "invalid_request_error");

                            return;
                        }

                        if (attempt < MaxRetries)
                        {
                            continue;
                        }

                        break;
                    }

                    if (!string.IsNullOrEmpty(conversationId))
                    {
                        quotaCache.SetConversationAccount(conversationId, account.Id);
                    }

                    // 提取 Factory (Anthropic) 限流信息
                    var quotaInfo = AccountQuotaCacheService.ExtractFromAnthropicHeaders(account.Id, response.Headers);
                    if (quotaInfo != null)
                    {
                        quotaCache.UpdateQuota(quotaInfo);
                    }

                    if (input.Stream)
                    {
                        await requestLogService.RecordSuccess(
                            logId,
                            stopwatch,
                            (int)response.StatusCode,
                            timeToFirstByteMs: stopwatch.ElapsedMilliseconds);

                        context.Response.StatusCode = (int)response.StatusCode;
                        context.Response.ContentType = "text/event-stream;charset=utf-8";
                        context.Response.Headers.TryAdd("Cache-Control", "no-cache");
                        context.Response.Headers.TryAdd("Connection", "keep-alive");
                        context.Response.Headers.TryAdd("X-Accel-Buffering", "no");

                        await context.Response.Body.FlushAsync(context.RequestAborted);

                        await using var stream = await response.Content.ReadAsStreamAsync(context.RequestAborted);
                        using var reader = new StreamReader(stream, Encoding.UTF8);

                        while (await reader.ReadLineAsync().ConfigureAwait(false) is { } line)
                        {
                            await context.Response.WriteAsync(line).ConfigureAwait(false);
                            if (line.StartsWith("data:"))
                            {
                                await context.Response.Body.WriteAsync(OpenAIConstant.DoubleNewLine);
                            }
                            else
                            {
                                await context.Response.Body.WriteAsync(OpenAIConstant.NewLine);
                            }

                            await context.Response.Body.FlushAsync(context.RequestAborted);
                        }

                        return;
                    }

                    var body = await response.Content.ReadAsStringAsync(context.RequestAborted);

                    await requestLogService.RecordSuccess(
                        logId,
                        stopwatch,
                        (int)response.StatusCode,
                        timeToFirstByteMs: stopwatch.ElapsedMilliseconds);

                    context.Response.StatusCode = (int)response.StatusCode;
                    context.Response.ContentType =
                        response.Content.Headers.ContentType?.ToString() ?? "application/json";
                    await context.Response.WriteAsync(body, Encoding.UTF8);
                    return;
                }
                else
                {
                    var geminiOAuth = account.GetGeminiOauth();
                    if (geminiOAuth == null)
                    {
                        lastErrorMessage = $"账户 {account.Id} 没有有效的 Gemini OAuth 凭证";
                        lastStatusCode = HttpStatusCode.Unauthorized;
                        logger.LogWarning(lastErrorMessage);

                        await aiAccountService.DisableAccount(account.Id);

                        if (attempt < MaxRetries)
                        {
                            continue;
                        }

                        break;
                    }

                    if (IsGeminiTokenExpired(geminiOAuth))
                    {
                        try
                        {
                            await geminiAntigravityOAuthService.RefreshGeminiAntigravityOAuthTokenAsync(account);
                        }
                        catch (Exception ex)
                        {
                            logger.LogWarning(
                                ex,
                                "Gemini Antigravity 账户 {AccountId} Token 刷新失败，已禁用账户",
                                account.Id);

                            lastErrorMessage = $"账户 {account.Id} Token 刷新失败: {ex.Message}";
                            lastStatusCode = HttpStatusCode.Unauthorized;

                            await aiAccountService.DisableAccount(account.Id);

                            if (attempt < MaxRetries)
                            {
                                continue;
                            }

                            break;
                        }

                        geminiOAuth = account.GetGeminiOauth();
                        if (geminiOAuth == null || string.IsNullOrWhiteSpace(geminiOAuth.Token))
                        {
                            lastErrorMessage = $"账户 {account.Id} Gemini Token 刷新后仍无效";
                            lastStatusCode = HttpStatusCode.Unauthorized;
                            logger.LogWarning(lastErrorMessage);

                            await aiAccountService.DisableAccount(account.Id);

                            if (attempt < MaxRetries)
                            {
                                continue;
                            }

                            break;
                        }
                    }

                    var resolvedModel = await ResolveAnthropicModelAsync(input.Model);
                    var components = ConvertAnthropicRequestToAntigravityComponents(input, resolvedModel);
                    if (components.Contents.Count == 0)
                    {
                        await WriteAnthropicError(
                            context,
                            StatusCodes.Status400BadRequest,
                            "messages 不能为空；text 内容块必须包含非空白文本",
                            "invalid_request_error");
                        return;
                    }

                    var requestBody = BuildAntigravityRequestBody(
                        components,
                        geminiOAuth.ProjectId,
                        sessionId: $"session-{Guid.NewGuid():N}");

                    var baseUrl = account.BaseUrl;
                    if (string.IsNullOrWhiteSpace(baseUrl))
                    {
                        baseUrl = GeminiAntigravityOAuthConfig.AntigravityApiUrl;
                    }

                    using var response = await SendAntigravityRequest(
                        baseUrl,
                        requestBody,
                        geminiOAuth.Token,
                        input.Stream,
                        context.RequestAborted);

                    if (response.StatusCode >= HttpStatusCode.BadRequest)
                    {
                        var error = await response.Content.ReadAsStringAsync();
                        lastErrorMessage = error;
                        lastStatusCode = response.StatusCode;

                        logger.LogError(
                            "Antigravity 请求失败 (账户: {AccountId}, 状态码: {StatusCode}, 尝试: {Attempt}/{MaxRetries}): {Error}",
                            account.Id,
                            response.StatusCode,
                            attempt,
                            MaxRetries,
                            error);

                        if (response.StatusCode is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden)
                        {
                            await aiAccountService.DisableAccount(account.Id);

                            if (attempt < MaxRetries)
                            {
                                continue;
                            }
                        }

                        var isClientError =
                            response.StatusCode == HttpStatusCode.BadRequest
                            || ClientErrorKeywords.Any(keyword =>
                                error.Contains(keyword, StringComparison.OrdinalIgnoreCase));

                        if (isClientError)
                        {
                            await requestLogService.RecordFailure(
                                logId,
                                stopwatch,
                                (int)response.StatusCode,
                                error);

                            await WriteAnthropicError(
                                context,
                                (int)response.StatusCode,
                                error,
                                "invalid_request_error");

                            return;
                        }

                        if (attempt < MaxRetries)
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

                    if (!string.IsNullOrEmpty(conversationId))
                    {
                        quotaCache.SetConversationAccount(conversationId, account.Id);
                    }

                    if (input.Stream)
                    {
                        await requestLogService.RecordSuccess(
                            logId,
                            stopwatch,
                            (int)response.StatusCode,
                            timeToFirstByteMs: stopwatch.ElapsedMilliseconds);

                        await StreamAnthropicResponse(
                            context,
                            response,
                            input.Model,
                            estimatedInputTokens,
                            returnThoughts);

                        return;
                    }

                    var responseValue = await response.Content.ReadFromJsonAsync<JsonDocument>();
                    if (responseValue == null)
                    {
                        lastErrorMessage = "Antigravity 响应体为空";
                        lastStatusCode = HttpStatusCode.BadGateway;
                        break;
                    }

                    var messageId = $"msg_{Guid.NewGuid():N}";
                    var anthropicResponse = ConvertAntigravityResponseToAnthropicMessage(
                        responseValue.RootElement,
                        input.Model,
                        messageId,
                        estimatedInputTokens,
                        returnThoughts,
                        out var promptTokens,
                        out var completionTokens,
                        out var totalTokens);

                    await requestLogService.RecordSuccess(
                        logId,
                        stopwatch,
                        (int)response.StatusCode,
                        timeToFirstByteMs: stopwatch.ElapsedMilliseconds,
                        promptTokens: promptTokens,
                        completionTokens: completionTokens,
                        totalTokens: totalTokens);

                    context.Response.StatusCode = (int)response.StatusCode;
                    context.Response.ContentType = "application/json";
                    await context.Response.WriteAsync(JsonSerializer.Serialize(anthropicResponse));
                }

                return;
            }

            context.Response.StatusCode = (int)(lastStatusCode ?? HttpStatusCode.ServiceUnavailable);
            var finalErrorMessage = lastErrorMessage ?? $"所有 {MaxRetries} 次重试均失败，无法完成请求";
            logger.LogError("Antigravity 请求失败: {ErrorMessage}", finalErrorMessage);

            await requestLogService.RecordFailure(
                logId,
                stopwatch,
                context.Response.StatusCode,
                finalErrorMessage);

            await WriteAnthropicError(context, context.Response.StatusCode, finalErrorMessage, "api_error");
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Antigravity 请求处理过程中发生未捕获的异常");
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
                await WriteAnthropicError(context, 500, $"服务器内部错误: {ex.Message}", "api_error");
            }
        }
    }

    /// <summary>
    ///     判断当前请求是否 Claude Code 发起，用于选择 Claude 渠道 + 透传请求头
    /// </summary>
    /// <returns></returns>
    private bool IsClaudeCodeRequest(HttpContext httpContext)
    {
        // 检查是否有特定的请求头或标识符来判断是否是Claude Code发起的请求
        // 这里可以根据实际情况调整
        return httpContext.Request.Headers.UserAgent.ToString()
            .Contains("claude-cli", StringComparison.OrdinalIgnoreCase);
    }

    private async Task<AIAccount?> GetClaudeOrAntigravityAccount(
        string? conversationId,
        string preferredProvider,
        AIAccountService aiAccountService)
    {
        AIAccount? account = null;

        if (!string.IsNullOrEmpty(conversationId))
        {
            var lastAccountId = quotaCache.GetConversationAccount(conversationId);
            if (lastAccountId.HasValue)
            {
                account = await aiAccountService.TryGetAccountById(lastAccountId.Value);
                if (account is { Provider: AIProviders.Claude or AIProviders.GeminiAntigravity or AIProviders.Factory })
                {
                    if (!string.Equals(account.Provider, preferredProvider, StringComparison.Ordinal))
                    {
                        account = null;
                    }
                }
                else
                {
                    account = null;
                }
            }
        }

        if (account != null)
        {
            return account;
        }

        if (string.Equals(preferredProvider, AIProviders.Claude, StringComparison.Ordinal))
        {
            account = await aiAccountService.GetAIAccountByProvider(AIProviders.Claude);
            if (account == null)
            {
                account = await aiAccountService.GetAIAccountByProvider(AIProviders.GeminiAntigravity);
                if (account != null)
                {
                    return account;
                }
            }
        }

        if (string.Equals(preferredProvider, AIProviders.GeminiAntigravity, StringComparison.Ordinal))
        {
            account = await aiAccountService.GetAIAccountByProvider(AIProviders.GeminiAntigravity);
            if (account == null)
            {
                account = await aiAccountService.GetAIAccountByProvider(AIProviders.Claude);
                if (account != null)
                {
                    return account;
                }
            }
        }

        account = await aiAccountService.GetAIAccountByProvider(AIProviders.Factory);
        if (account == null)
        {
            account = await aiAccountService.GetAIAccountByProvider(AIProviders.Claude);
            if (account != null)
            {
                return account;
            }
        }
        else
        {
            return account;
        }

        return await aiAccountService.GetAIAccountByProvider(AIProviders.Claude, AIProviders.GeminiAntigravity);
    }

    private static string BuildClaudeMessagesUrl(string? baseUrl)
    {
        if (string.IsNullOrWhiteSpace(baseUrl))
        {
            return "https://api.anthropic.com/v1/messages?beta=true";
        }

        var trimmed = baseUrl.Trim();
        if (trimmed.EndsWith("/v1/messages", StringComparison.OrdinalIgnoreCase))
        {
            return trimmed+"?beta=true";
        }

        trimmed = trimmed.TrimEnd('/');

        if (trimmed.EndsWith("/v1", StringComparison.OrdinalIgnoreCase))
        {
            return trimmed + "/messages?beta=true";
        }

        return trimmed + "/v1/messages?beta=true";
    }


    public async Task CountTokens(HttpContext context, AnthropicInput input)
    {
        if (string.IsNullOrWhiteSpace(input.Model) || input.Messages == null)
        {
            await WriteAnthropicError(
                context,
                StatusCodes.Status400BadRequest,
                "缺少必填字段：model / messages",
                "invalid_request_error");
            return;
        }

        var tokens = EstimateInputTokens(input);
        context.Response.ContentType = "application/json";
        await context.Response.WriteAsJsonAsync(new { input_tokens = tokens });
    }

    private async Task StreamAnthropicResponse(
        HttpContext context,
        HttpResponseMessage response,
        string model,
        int fallbackInputTokens,
        bool returnThoughts)
    {
        context.Response.StatusCode = (int)response.StatusCode;
        context.Response.ContentType = "text/event-stream;charset=utf-8";
        context.Response.Headers.TryAdd("Cache-Control", "no-cache");
        context.Response.Headers.TryAdd("Connection", "keep-alive");
        context.Response.Headers.TryAdd("X-Accel-Buffering", "no");

        await context.Response.Body.FlushAsync(context.RequestAborted);

        await using var stream = await response.Content.ReadAsStreamAsync(context.RequestAborted);
        using var reader = new StreamReader(stream, Encoding.UTF8);

        var messageId = $"msg_{Guid.NewGuid():N}";
        var state = new StreamingState(model, messageId);
        var pending = new List<string>();

        async Task WriteRaw(string payload)
        {
            await context.Response.WriteAsync(payload);
            await context.Response.Body.FlushAsync();
        }

        async Task FlushPending()
        {
            foreach (var item in pending)
            {
                await WriteRaw(item);
            }

            pending.Clear();
        }

        string BuildEvent(string eventName, object data)
        {
            var json = JsonSerializer.Serialize(data);
            return $"event: {eventName}\n" + $"data: {json}\n\n";
        }

        async Task SendMessageStart(int inputTokens)
        {
            if (state.MessageStartSent)
            {
                return;
            }

            state.MessageStartSent = true;
            var payload = BuildEvent("message_start", new Dictionary<string, object?>
            {
                ["type"] = "message_start",
                ["message"] = new Dictionary<string, object?>
                {
                    ["id"] = messageId,
                    ["type"] = "message",
                    ["role"] = "assistant",
                    ["model"] = model,
                    ["content"] = Array.Empty<object>(),
                    ["stop_reason"] = null,
                    ["stop_sequence"] = null,
                    ["usage"] = new Dictionary<string, object?>
                    {
                        ["input_tokens"] = inputTokens,
                        ["output_tokens"] = 0
                    }
                }
            });

            await WriteRaw(payload);
            await FlushPending();
        }

        try
        {
            while (true)
            {
                var line = await reader.ReadLineAsync();
                if (line == null)
                {
                    break;
                }

                if (string.IsNullOrWhiteSpace(line))
                {
                    continue;
                }

                var raw = line;
                if (!raw.StartsWith(OpenAIConstant.Data, StringComparison.Ordinal))
                {
                    continue;
                }

                raw = raw[OpenAIConstant.Data.Length..].TrimStart();
                if (raw == OpenAIConstant.Done)
                {
                    break;
                }

                if (!TryParseJson(raw, out var doc))
                {
                    continue;
                }

                using (doc)
                {
                    var root = doc.RootElement;
                    if (!TryGetResponseAndCandidate(root, out var responseElement, out var candidate))
                    {
                        continue;
                    }

                    UpdateUsageFromResponse(state, responseElement, candidate);
                    if (!state.MessageStartSent)
                    {
                        await SendMessageStart(state.HasInputTokens ? state.InputTokens : fallbackInputTokens);
                    }

                    if (TryGetArray(candidate, "content", "parts", out var parts))
                    {
                        foreach (var part in parts.EnumerateArray())
                        {
                            if (!part.ValueKind.Equals(JsonValueKind.Object))
                            {
                                continue;
                            }

                            if (returnThoughts
                                && state.CurrentBlockType == StreamingBlockType.Thinking
                                && !state.HasThinkingSignature
                                && part.TryGetProperty("thoughtSignature", out var standaloneSignatureProp)
                                && standaloneSignatureProp.ValueKind == JsonValueKind.String)
                            {
                                var standaloneSignature = standaloneSignatureProp.GetString();
                                if (!string.IsNullOrWhiteSpace(standaloneSignature))
                                {
                                    pending.Add(state.EmitSignatureDelta(standaloneSignature));
                                }
                            }

                            if (part.TryGetProperty("thought", out var thoughtProp)
                                && thoughtProp.ValueKind == JsonValueKind.True)
                            {
                                if (!returnThoughts)
                                {
                                    continue;
                                }

                                var signature = part.TryGetProperty("thoughtSignature", out var sigProp)
                                                && sigProp.ValueKind == JsonValueKind.String
                                    ? sigProp.GetString()
                                    : null;

                                if (state.CurrentBlockType != StreamingBlockType.Thinking)
                                {
                                    var stopEvent = state.CloseBlock();
                                    if (stopEvent != null)
                                    {
                                        pending.Add(stopEvent);
                                    }

                                    pending.Add(state.OpenThinkingBlock(signature));
                                }

                                var thinkingText = part.TryGetProperty("text", out var textProp)
                                                   && textProp.ValueKind == JsonValueKind.String
                                    ? textProp.GetString()
                                    : string.Empty;

                                if (!string.IsNullOrEmpty(thinkingText))
                                {
                                    pending.Add(state.EmitThinkingDelta(thinkingText));
                                }

                                continue;
                            }

                            if (part.TryGetProperty("text", out var textValue))
                            {
                                var text = textValue.ValueKind == JsonValueKind.String
                                    ? textValue.GetString()
                                    : textValue.ToString();
                                text ??= string.Empty;
                                if (string.IsNullOrWhiteSpace(text))
                                {
                                    continue;
                                }

                                if (state.CurrentBlockType != StreamingBlockType.Text)
                                {
                                    var stopEvent = state.CloseBlock();
                                    if (stopEvent != null)
                                    {
                                        pending.Add(stopEvent);
                                    }

                                    pending.Add(state.OpenTextBlock());
                                }

                                pending.Add(state.EmitTextDelta(text));
                                continue;
                            }

                            if (part.TryGetProperty("inlineData", out var inlineData))
                            {
                                var stopEvent = state.CloseBlock();
                                if (stopEvent != null)
                                {
                                    pending.Add(stopEvent);
                                }

                                var mimeType = inlineData.TryGetProperty("mimeType", out var mimeProp)
                                               && mimeProp.ValueKind == JsonValueKind.String
                                    ? mimeProp.GetString()
                                    : "image/png";
                                var data = inlineData.TryGetProperty("data", out var dataProp)
                                           && dataProp.ValueKind == JsonValueKind.String
                                    ? dataProp.GetString()
                                    : string.Empty;

                                pending.Add(state.EmitImageBlock(mimeType, data));
                                continue;
                            }

                            if (part.TryGetProperty("functionCall", out var functionCall))
                            {
                                var stopEvent = state.CloseBlock();
                                if (stopEvent != null)
                                {
                                    pending.Add(stopEvent);
                                }

                                state.HasToolUse = true;
                                var toolId = functionCall.TryGetProperty("id", out var idProp)
                                             && idProp.ValueKind == JsonValueKind.String
                                    ? idProp.GetString()
                                    : null;
                                toolId = string.IsNullOrWhiteSpace(toolId) ? $"toolu_{Guid.NewGuid():N}" : toolId;

                                var toolName = functionCall.TryGetProperty("name", out var nameProp)
                                               && nameProp.ValueKind == JsonValueKind.String
                                    ? nameProp.GetString()
                                    : string.Empty;
                                toolName ??= string.Empty;
                                var args = functionCall.TryGetProperty("args", out var argsProp)
                                    ? argsProp
                                    : default;

                                pending.Add(state.EmitToolUseBlock(toolId, toolName, args));
                            }
                        }
                    }

                    if (candidate.TryGetProperty("finishReason", out var finishReasonProp)
                        && finishReasonProp.ValueKind == JsonValueKind.String)
                    {
                        state.FinishReason = finishReasonProp.GetString();
                        break;
                    }
                }

                if (state.MessageStartSent)
                {
                    await FlushPending();
                }
            }

            var stopBlock = state.CloseBlock();
            if (stopBlock != null)
            {
                pending.Add(stopBlock);
            }

            if (!state.MessageStartSent)
            {
                await SendMessageStart(fallbackInputTokens);
            }
            else
            {
                await FlushPending();
            }

            var stopReason = state.HasToolUse ? "tool_use" : "end_turn";
            if (string.Equals(state.FinishReason, "MAX_TOKENS", StringComparison.OrdinalIgnoreCase)
                && !state.HasToolUse)
            {
                stopReason = "max_tokens";
            }

            var messageDelta = BuildEvent("message_delta", new Dictionary<string, object?>
            {
                ["type"] = "message_delta",
                ["delta"] = new Dictionary<string, object?>
                {
                    ["stop_reason"] = stopReason,
                    ["stop_sequence"] = null
                },
                ["usage"] = new Dictionary<string, object?>
                {
                    ["input_tokens"] = state.HasInputTokens ? state.InputTokens : fallbackInputTokens,
                    ["output_tokens"] = state.HasOutputTokens ? state.OutputTokens : 0
                }
            });

            await WriteRaw(messageDelta);
            await WriteRaw(BuildEvent("message_stop", new Dictionary<string, object?> { ["type"] = "message_stop" }));
        }
        catch (OperationCanceledException)
        {
            logger.LogInformation("客户端取消 Antigravity 流式请求");
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Antigravity 流式传输过程中发生异常");
        }
    }

    private async Task<HttpResponseMessage> SendAntigravityRequest(
        string baseUrl,
        Dictionary<string, object?> requestBody,
        string accessToken,
        bool isStream,
        CancellationToken cancellationToken)
    {
        var url = baseUrl.TrimEnd('/') +
                  (isStream ? "/v1internal:streamGenerateContent?alt=sse" : "/v1internal:generateContent");

        var json = JsonSerializer.Serialize(requestBody, new JsonSerializerOptions
        {
            DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
        });

        using var request = new HttpRequestMessage(HttpMethod.Post, url);
        request.Content = new StringContent(json, Encoding.UTF8, "application/json");

        request.Version = HttpVersion.Version11;
        request.VersionPolicy = HttpVersionPolicy.RequestVersionOrLower;
        request.Headers.Add("Authorization", $"Bearer {accessToken}");
        request.Headers.Add("User-Agent", GeminiAntigravityOAuthConfig.UserAgent);

        return await HttpClient.SendAsync(
            request,
            isStream ? HttpCompletionOption.ResponseHeadersRead : HttpCompletionOption.ResponseContentRead,
            cancellationToken);
    }

    private static string BuildConversationStickyKey(AnthropicInput input)
    {
        var seed = BuildConversationStickySeed(input);
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(seed));
        return "anthropic_" + Convert.ToHexString(bytes).ToLowerInvariant();
    }

    private static string BuildConversationStickySeed(AnthropicInput input)
    {
        var builder = new StringBuilder(2048);
        builder.Append("anthropic_sticky_v1|");

        var userId = TryGetMetadataString(input.Metadata, "user_id", "userId", "user");
        if (!string.IsNullOrWhiteSpace(userId))
        {
            builder.Append("u=");
            builder.Append(NormalizeAndTruncate(userId, 128));
            builder.Append('|');
        }

        // If the client provides an explicit conversation/thread id in metadata, use it as a strong signal.
        var explicitConversationId = TryGetMetadataString(
            input.Metadata,
            "conversation_id",
            "conversationId",
            "session_id",
            "sessionId",
            "thread_id",
            "threadId");
        if (!string.IsNullOrWhiteSpace(explicitConversationId))
        {
            builder.Append("t=");
            builder.Append(NormalizeAndTruncate(explicitConversationId, 128));
            builder.Append('|');
        }

        // Include stable, early context. Messages grow over time, so we deliberately anchor on the earliest user message.
        var systemText = GetSystemTextForStickiness(input);
        if (!string.IsNullOrWhiteSpace(systemText))
        {
            builder.Append("s=");
            builder.Append(NormalizeAndTruncate(systemText, 512));
            builder.Append('|');
        }

        var toolSignature = GetToolsSignatureForStickiness(input.Tools);
        if (!string.IsNullOrWhiteSpace(toolSignature))
        {
            builder.Append("tools=");
            builder.Append(toolSignature);
            builder.Append('|');
        }

        var firstUserMessage = GetFirstUserMessageTextForStickiness(input.Messages);
        builder.Append("m=");
        builder.Append(NormalizeAndTruncate(firstUserMessage, 1024));

        return builder.ToString();
    }

    private static string GetSystemTextForStickiness(AnthropicInput input)
    {
        if (!string.IsNullOrWhiteSpace(input.System))
        {
            return input.System!;
        }

        if (input.Systems == null || input.Systems.Count == 0)
        {
            return string.Empty;
        }

        var builder = new StringBuilder();
        foreach (var system in input.Systems)
        {
            if (system == null)
            {
                continue;
            }

            if (string.Equals(system.Type, "text", StringComparison.OrdinalIgnoreCase)
                && !string.IsNullOrWhiteSpace(system.Text))
            {
                if (builder.Length > 0)
                {
                    builder.Append('\n');
                }

                builder.Append(system.Text);
            }
        }

        return builder.ToString();
    }

    private static string GetToolsSignatureForStickiness(IList<AnthropicMessageTool>? tools)
    {
        if (tools == null || tools.Count == 0)
        {
            return string.Empty;
        }

        var names = new List<string>();
        foreach (var tool in tools)
        {
            var name = tool?.Name?.Trim();
            if (!string.IsNullOrWhiteSpace(name))
            {
                names.Add(name);
            }
        }

        if (names.Count == 0)
        {
            return string.Empty;
        }

        names.Sort(StringComparer.Ordinal);
        return NormalizeAndTruncate(string.Join(",", names), 256);
    }

    private static string GetFirstUserMessageTextForStickiness(IList<AnthropicMessageInput> messages)
    {
        if (messages == null || messages.Count == 0)
        {
            return string.Empty;
        }

        // Prefer the earliest user message, since Anthropic messages are typically appended over time.
        foreach (var message in messages)
        {
            if (message == null)
            {
                continue;
            }

            if (!string.Equals(message.Role, "user", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            var text = ExtractMessageTextForStickiness(message);
            if (!string.IsNullOrWhiteSpace(text))
            {
                return text;
            }
        }

        // Fallback: first non-empty message (handles edge cases where roles are missing/malformed)
        foreach (var message in messages)
        {
            if (message == null)
            {
                continue;
            }

            var text = ExtractMessageTextForStickiness(message);
            if (!string.IsNullOrWhiteSpace(text))
            {
                return text;
            }
        }

        return string.Empty;
    }

    private static string ExtractMessageTextForStickiness(AnthropicMessageInput message)
    {
        if (!string.IsNullOrWhiteSpace(message.Content))
        {
            return message.Content!;
        }

        if (message.Contents == null || message.Contents.Count == 0)
        {
            return string.Empty;
        }

        var builder = new StringBuilder();
        foreach (var content in message.Contents)
        {
            if (content == null)
            {
                continue;
            }

            if (string.Equals(content.Type, "text", StringComparison.OrdinalIgnoreCase)
                && !string.IsNullOrWhiteSpace(content.Text))
            {
                if (builder.Length > 0)
                {
                    builder.Append('\n');
                }

                builder.Append(content.Text);
            }
            else if (string.Equals(content.Type, "image", StringComparison.OrdinalIgnoreCase))
            {
                // Do not hash raw base64 image data; just include a small marker.
                if (builder.Length > 0)
                {
                    builder.Append('\n');
                }

                builder.Append("[image]");
            }
        }

        return builder.ToString();
    }

    private static string? TryGetMetadataString(Dictionary<string, object>? metadata, params string[] keys)
    {
        if (metadata == null || metadata.Count == 0 || keys.Length == 0)
        {
            return null;
        }

        foreach (var key in keys)
        {
            if (string.IsNullOrWhiteSpace(key))
            {
                continue;
            }

            object? value = null;

            if (metadata.TryGetValue(key, out var direct))
            {
                value = direct;
            }
            else
            {
                var match = metadata.FirstOrDefault(kvp =>
                    string.Equals(kvp.Key, key, StringComparison.OrdinalIgnoreCase));
                value = match.Value;
            }

            if (value == null)
            {
                continue;
            }

            if (value is string str)
            {
                return str;
            }

            if (value is JsonElement element)
            {
                return element.ValueKind == JsonValueKind.String
                    ? element.GetString()
                    : element.ToString();
            }

            return value.ToString();
        }

        return null;
    }

    private static string NormalizeAndTruncate(string? value, int maxChars)
    {
        if (string.IsNullOrEmpty(value) || maxChars <= 0)
        {
            return string.Empty;
        }

        var normalized = value
            .Replace("\r\n", "\n", StringComparison.Ordinal)
            .Replace("\r", "\n", StringComparison.Ordinal)
            .Replace("\0", string.Empty, StringComparison.Ordinal)
            .Trim();

        return normalized.Length <= maxChars
            ? normalized
            : normalized[..maxChars];
    }

    private static async Task WriteAnthropicError(
        HttpContext context,
        int statusCode,
        string message,
        string errorType)
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

    private static Dictionary<string, object?> BuildAntigravityRequestBody(
        AntigravityComponents components,
        string projectId,
        string sessionId)
    {
        if (string.IsNullOrWhiteSpace(projectId))
        {
            throw new InvalidOperationException("Project ID is required for Antigravity API");
        }

        var request = new Dictionary<string, object?>
        {
            ["contents"] = components.Contents,
            ["session_id"] = sessionId
        };

        if (components.SystemInstruction != null)
        {
            request["systemInstruction"] = components.SystemInstruction;
        }

        if (components.Tools != null)
        {
            request["tools"] = components.Tools;
            request["toolConfig"] = new Dictionary<string, object?>
            {
                ["functionCallingConfig"] = new Dictionary<string, object?>
                {
                    ["mode"] = "VALIDATED"
                }
            };
        }

        if (components.GenerationConfig != null)
        {
            request["generationConfig"] = components.GenerationConfig;
        }

        return new Dictionary<string, object?>
        {
            ["project"] = projectId,
            ["requestId"] = $"req-{Guid.NewGuid()}",
            ["model"] = components.Model,
            ["userAgent"] = "antigravity",
            ["request"] = request
        };
    }

    private async Task<string> ResolveAnthropicModelAsync(string model)
    {
        var mapping = await modelMappingService.ResolveAnthropicAsync(model);
        if (mapping == null)
        {
            return MapClaudeModelToGemini(model);
        }

        if (!string.IsNullOrWhiteSpace(mapping.TargetProvider)
            && !string.Equals(mapping.TargetProvider, AIProviders.GeminiAntigravity, StringComparison.Ordinal))
        {
            logger.LogWarning(
                "Anthropic 模型映射指定的提供商不是 Gemini-Antigravity，将忽略 provider={Provider}",
                mapping.TargetProvider);
        }

        return mapping.TargetModel;
    }

    private static AntigravityComponents ConvertAnthropicRequestToAntigravityComponents(
        AnthropicInput input,
        string resolvedModel)
    {
        var model = string.IsNullOrWhiteSpace(resolvedModel)
            ? MapClaudeModelToGemini(input.Model)
            : resolvedModel;
        var contents = ConvertMessagesToContents(input.Messages);
        contents = ReorganizeToolMessages(contents);

        var systemInstruction = BuildSystemInstruction(input);
        var tools = ConvertTools(input.Tools);
        var generationConfig = BuildGenerationConfig(input);

        return new AntigravityComponents(
            model,
            contents,
            systemInstruction,
            tools,
            generationConfig);
    }

    private static string MapClaudeModelToGemini(string model)
    {
        var normalized = (model ?? string.Empty).Trim();
        if (string.IsNullOrEmpty(normalized))
        {
            return "claude-sonnet-4-5";
        }

        if (normalized.StartsWith("claude-opus-4-5-", StringComparison.OrdinalIgnoreCase))
        {
            normalized = "claude-opus-4-5";
        }

        if (normalized.StartsWith("claude-sonnet-4-5-", StringComparison.OrdinalIgnoreCase))
        {
            normalized = "claude-sonnet-4-5";
        }

        if (normalized.StartsWith("claude-haiku-4-5-", StringComparison.OrdinalIgnoreCase))
        {
            normalized = "claude-haiku-4-5";
        }

        return normalized switch
        {
            "claude-opus-4-5" => "claude-opus-4-5-thinking",
            "claude-sonnet-4-5" => "claude-sonnet-4-5",
            "claude-haiku-4-5" => "gemini-2.5-flash",
            "claude-sonnet-4.5" => "claude-sonnet-4-5",
            "claude-3-5-sonnet-20241022" => "claude-sonnet-4-5",
            "claude-3-5-sonnet-20240620" => "claude-sonnet-4-5",
            "claude-opus-4" => "gemini-3-pro-high",
            "claude-haiku-4" => "claude-haiku-4.5",
            "claude-3-haiku-20240307" => "gemini-2.5-flash",
            _ => normalized
        };
    }

    private static List<Dictionary<string, object?>> ConvertMessagesToContents(IList<AnthropicMessageInput> messages)
    {
        var contents = new List<Dictionary<string, object?>>();
        foreach (var message in messages)
        {
            var role = string.Equals(message.Role, "assistant", StringComparison.OrdinalIgnoreCase)
                ? "model"
                : "user";

            var parts = BuildPartsFromMessage(message);
            if (parts.Count == 0)
            {
                continue;
            }

            contents.Add(new Dictionary<string, object?>
            {
                ["role"] = role,
                ["parts"] = parts
            });
        }

        return contents;
    }

    private static List<Dictionary<string, object?>> BuildPartsFromMessage(AnthropicMessageInput message)
    {
        var parts = new List<Dictionary<string, object?>>();

        if (!string.IsNullOrWhiteSpace(message.Content))
        {
            parts.Add(new Dictionary<string, object?> { ["text"] = message.Content });
            return parts;
        }

        if (message.Contents == null)
        {
            return parts;
        }

        foreach (var item in message.Contents)
        {
            if (item == null)
            {
                continue;
            }

            var type = item.Type ?? "text";
            switch (type)
            {
                case "thinking":
                {
                    if (string.IsNullOrWhiteSpace(item.Signature))
                    {
                        continue;
                    }

                    var thinkingText = item.Thinking ?? item.Text ?? string.Empty;
                    parts.Add(new Dictionary<string, object?>
                    {
                        ["text"] = thinkingText,
                        ["thought"] = true,
                        ["thoughtSignature"] = item.Signature
                    });
                    break;
                }
                case "redacted_thinking":
                {
                    if (string.IsNullOrWhiteSpace(item.Signature))
                    {
                        continue;
                    }

                    var thinkingText = item.Thinking ?? item.Text ?? item.Content?.ToString() ?? string.Empty;
                    parts.Add(new Dictionary<string, object?>
                    {
                        ["text"] = thinkingText,
                        ["thought"] = true,
                        ["thoughtSignature"] = item.Signature
                    });
                    break;
                }
                case "text":
                {
                    if (string.IsNullOrWhiteSpace(item.Text))
                    {
                        continue;
                    }

                    parts.Add(new Dictionary<string, object?> { ["text"] = item.Text });
                    break;
                }
                case "image":
                {
                    if (item.Source is not { Type: "base64" })
                    {
                        continue;
                    }

                    parts.Add(new Dictionary<string, object?>
                    {
                        ["inlineData"] = new Dictionary<string, object?>
                        {
                            ["mimeType"] = item.Source.MediaType ?? "image/png",
                            ["data"] = item.Source.Data ?? string.Empty
                        }
                    });
                    break;
                }
                case "tool_use":
                {
                    var toolUseId = string.IsNullOrWhiteSpace(item.Id) ? $"toolu_{Guid.NewGuid():N}" : item.Id;
                    parts.Add(new Dictionary<string, object?>
                    {
                        ["functionCall"] = new Dictionary<string, object?>
                        {
                            ["id"] = toolUseId,
                            ["name"] = item.Name ?? string.Empty,
                            ["args"] = RemoveNulls(item.Input) ?? new Dictionary<string, object?>()
                        }
                    });
                    break;
                }
                case "tool_result":
                {
                    if (string.IsNullOrWhiteSpace(item.ToolUseId))
                    {
                        break;
                    }

                    var output = ExtractToolResultOutput(item.Content);
                    parts.Add(new Dictionary<string, object?>
                    {
                        ["functionResponse"] = new Dictionary<string, object?>
                        {
                            ["id"] = item.ToolUseId,
                            ["name"] = item.Name ?? string.Empty,
                            ["response"] = new Dictionary<string, object?>
                            {
                                ["output"] = output
                            }
                        }
                    });
                    break;
                }
                default:
                {
                    parts.Add(new Dictionary<string, object?>
                    {
                        ["text"] = JsonSerializer.Serialize(item)
                    });
                    break;
                }
            }
        }

        return parts;
    }

    private static List<Dictionary<string, object?>> ReorganizeToolMessages(
        List<Dictionary<string, object?>> contents)
    {
        var toolResults = new Dictionary<string, Dictionary<string, object?>>();
        foreach (var message in contents)
        {
            if (!message.TryGetValue("parts", out var partsObj) ||
                partsObj is not List<Dictionary<string, object?>> parts)
            {
                continue;
            }

            foreach (var part in parts)
            {
                if (part.TryGetValue("functionResponse", out var responseObj)
                    && responseObj is Dictionary<string, object?> response)
                {
                    if (response.TryGetValue("id", out var idObj) && idObj != null)
                    {
                        toolResults[idObj.ToString() ?? string.Empty] = part;
                    }
                }
            }
        }

        var flattened = new List<Dictionary<string, object?>>();
        foreach (var message in contents)
        {
            if (!message.TryGetValue("role", out var roleObj) || roleObj == null)
            {
                continue;
            }

            if (!message.TryGetValue("parts", out var partsObj) ||
                partsObj is not List<Dictionary<string, object?>> parts)
            {
                continue;
            }

            foreach (var part in parts)
            {
                flattened.Add(new Dictionary<string, object?>
                {
                    ["role"] = roleObj,
                    ["parts"] = new List<Dictionary<string, object?>> { part }
                });
            }
        }

        var reordered = new List<Dictionary<string, object?>>();
        foreach (var message in flattened)
        {
            if (!message.TryGetValue("parts", out var partsObj) || partsObj is not List<Dictionary<string, object?>>
                                                                    parts
                                                                || parts.Count == 0)
            {
                continue;
            }

            var part = parts[0];
            if (part.ContainsKey("functionResponse"))
            {
                continue;
            }

            if (part.TryGetValue("functionCall", out var callObj)
                && callObj is Dictionary<string, object?> call
                && call.TryGetValue("id", out var idObj)
                && idObj != null)
            {
                reordered.Add(new Dictionary<string, object?>
                {
                    ["role"] = "model",
                    ["parts"] = new List<Dictionary<string, object?>> { part }
                });

                var key = idObj.ToString() ?? string.Empty;
                if (!string.IsNullOrWhiteSpace(key) && toolResults.TryGetValue(key, out var toolResult))
                {
                    reordered.Add(new Dictionary<string, object?>
                    {
                        ["role"] = "user",
                        ["parts"] = new List<Dictionary<string, object?>> { toolResult }
                    });
                }
            }
            else
            {
                reordered.Add(message);
            }
        }

        return reordered;
    }

    private static Dictionary<string, object?>? BuildSystemInstruction(AnthropicInput input)
    {
        if (!string.IsNullOrWhiteSpace(input.System))
        {
            return new Dictionary<string, object?>
            {
                ["role"] = "user",
                ["parts"] = new List<Dictionary<string, object?>>
                {
                    new() { ["text"] = input.System }
                }
            };
        }

        if (input.Systems == null || input.Systems.Count == 0)
        {
            return null;
        }

        var parts = new List<Dictionary<string, object?>>();
        foreach (var item in input.Systems)
        {
            if (item.Type == "text" && !string.IsNullOrWhiteSpace(item.Text))
            {
                parts.Add(new Dictionary<string, object?> { ["text"] = item.Text });
            }
        }

        if (parts.Count == 0)
        {
            return null;
        }

        return new Dictionary<string, object?>
        {
            ["role"] = "user",
            ["parts"] = parts
        };
    }

    private static List<Dictionary<string, object?>>? ConvertTools(IList<AnthropicMessageTool>? tools)
    {
        if (tools == null || tools.Count == 0)
        {
            return null;
        }

        var result = new List<Dictionary<string, object?>>();
        foreach (var tool in tools)
        {
            if (string.IsNullOrWhiteSpace(tool.Name))
            {
                continue;
            }

            var parameters = ConvertInputSchema(tool.InputSchema);
            var declarations = new Dictionary<string, object?>
            {
                ["name"] = tool.Name,
                ["description"] = tool.Description ?? string.Empty,
                ["parameters"] = parameters
            };

            result.Add(new Dictionary<string, object?>
            {
                ["functionDeclarations"] = new List<Dictionary<string, object?>> { declarations }
            });
        }

        return result.Count == 0 ? null : result;
    }

    private static Dictionary<string, object?> ConvertInputSchema(Input_schema? schema)
    {
        if (schema == null)
        {
            return new Dictionary<string, object?> { ["type"] = "object" };
        }

        var result = new Dictionary<string, object?>
        {
            ["type"] = schema.Type ?? "object"
        };

        if (schema.Properties != null && schema.Properties.Count > 0)
        {
            var properties = new Dictionary<string, object?>();
            foreach (var (key, value) in schema.Properties)
            {
                var prop = new Dictionary<string, object?>
                {
                    ["type"] = value.type ?? "string",
                    ["description"] = value.description ?? string.Empty
                };

                if (value.items != null)
                {
                    prop["items"] = new Dictionary<string, object?>
                    {
                        ["type"] = value.items.type ?? "string"
                    };
                }

                properties[key] = prop;
            }

            result["properties"] = properties;
        }

        if (schema.Required != null && schema.Required.Length > 0)
        {
            result["required"] = schema.Required;
        }

        return result;
    }

    private static Dictionary<string, object?> BuildGenerationConfig(AnthropicInput input)
    {
        var config = new Dictionary<string, object?>
        {
            ["topP"] = 1,
            ["topK"] = 40,
            ["candidateCount"] = 1,
            ["stopSequences"] = new List<string>
            {
                "<|user|>",
                "<|bot|>",
                "<|context_request|>",
                "<|endoftext|>",
                "<|end_of_turn|>"
            }
        };

        config["temperature"] = input.Temperature ?? 0.4;

        if (input.MaxTokens.HasValue)
        {
            config["maxOutputTokens"] = input.MaxTokens.Value;
        }

        if (input.Thinking != null)
        {
            var thinkingConfig = new Dictionary<string, object?>
            {
                ["includeThoughts"] = string.Equals(input.Thinking.Type, "enabled", StringComparison.OrdinalIgnoreCase),
                ["thinkingBudget"] = input.Thinking.BudgetTokens
            };

            if ((bool)thinkingConfig["includeThoughts"]!)
            {
                var lastAssistant = input.Messages?.LastOrDefault(m => m.Role == "assistant");
                var firstBlockType = lastAssistant?.Contents?.FirstOrDefault()?.Type;
                if (firstBlockType != null
                    && firstBlockType is not "thinking"
                    && firstBlockType is not "redacted_thinking")
                {
                    return config;
                }

                if (input.MaxTokens.HasValue && input.MaxTokens.Value <= input.Thinking.BudgetTokens)
                {
                    var adjustedBudget = input.MaxTokens.Value - 1;
                    if (adjustedBudget <= 0)
                    {
                        return config;
                    }

                    thinkingConfig["thinkingBudget"] = adjustedBudget;
                }
            }

            config["thinkingConfig"] = thinkingConfig;
        }

        return config;
    }

    private static Dictionary<string, object?> ConvertAntigravityResponseToAnthropicMessage(
        JsonElement responseData,
        string model,
        string messageId,
        int fallbackInputTokens,
        bool returnThoughts,
        out int? promptTokens,
        out int? completionTokens,
        out int? totalTokens)
    {
        promptTokens = null;
        completionTokens = null;
        totalTokens = null;

        if (!TryGetResponseAndCandidate(responseData, out var responseElement, out var candidate))
        {
            return new Dictionary<string, object?>
            {
                ["id"] = messageId,
                ["type"] = "message",
                ["role"] = "assistant",
                ["model"] = model,
                ["content"] = Array.Empty<object>(),
                ["stop_reason"] = "end_turn",
                ["stop_sequence"] = null,
                ["usage"] = new Dictionary<string, object?>
                {
                    ["input_tokens"] = fallbackInputTokens,
                    ["output_tokens"] = 0
                }
            };
        }

        var parts = new List<Dictionary<string, object?>>();
        var hasToolUse = false;

        if (TryGetArray(candidate, "content", "parts", out var partsElement))
        {
            foreach (var part in partsElement.EnumerateArray())
            {
                if (part.ValueKind != JsonValueKind.Object)
                {
                    continue;
                }

                if (part.TryGetProperty("thought", out var thoughtProp) && thoughtProp.ValueKind == JsonValueKind.True)
                {
                    if (!returnThoughts)
                    {
                        continue;
                    }

                    var block = new Dictionary<string, object?>
                    {
                        ["type"] = "thinking",
                        ["thinking"] = part.TryGetProperty("text", out var textProp) &&
                                       textProp.ValueKind == JsonValueKind.String
                            ? textProp.GetString()
                            : string.Empty
                    };

                    if (part.TryGetProperty("thoughtSignature", out var signatureProp)
                        && signatureProp.ValueKind == JsonValueKind.String)
                    {
                        block["signature"] = signatureProp.GetString();
                    }

                    parts.Add(block);
                    continue;
                }

                if (part.TryGetProperty("text", out var textValue))
                {
                    var text = textValue.ValueKind == JsonValueKind.String
                        ? textValue.GetString()
                        : textValue.ToString();
                    text ??= string.Empty;
                    parts.Add(new Dictionary<string, object?>
                    {
                        ["type"] = "text",
                        ["text"] = text
                    });
                    continue;
                }

                if (part.TryGetProperty("functionCall", out var functionCall))
                {
                    hasToolUse = true;
                    var toolBlock = new Dictionary<string, object?>
                    {
                        ["type"] = "tool_use",
                        ["id"] = functionCall.TryGetProperty("id", out var idProp)
                                 && idProp.ValueKind == JsonValueKind.String
                            ? idProp.GetString() ?? $"toolu_{Guid.NewGuid():N}"
                            : $"toolu_{Guid.NewGuid():N}",
                        ["name"] = functionCall.TryGetProperty("name", out var nameProp)
                                   && nameProp.ValueKind == JsonValueKind.String
                            ? nameProp.GetString() ?? string.Empty
                            : string.Empty,
                        ["input"] = functionCall.TryGetProperty("args", out var argsProp)
                            ? RemoveNulls(argsProp) ?? new Dictionary<string, object?>()
                            : new Dictionary<string, object?>()
                    };
                    parts.Add(toolBlock);
                    continue;
                }

                if (part.TryGetProperty("inlineData", out var inlineData))
                {
                    parts.Add(new Dictionary<string, object?>
                    {
                        ["type"] = "image",
                        ["source"] = new Dictionary<string, object?>
                        {
                            ["type"] = "base64",
                            ["media_type"] = inlineData.TryGetProperty("mimeType", out var mimeProp)
                                             && mimeProp.ValueKind == JsonValueKind.String
                                ? mimeProp.GetString()
                                : "image/png",
                            ["data"] = inlineData.TryGetProperty("data", out var dataProp)
                                       && dataProp.ValueKind == JsonValueKind.String
                                ? dataProp.GetString()
                                : string.Empty
                        }
                    });
                }
            }
        }

        if (TryExtractUsage(responseElement, candidate, out var usage))
        {
            promptTokens = usage.PromptTokens;
            completionTokens = usage.CompletionTokens;
            totalTokens = usage.TotalTokens;
        }

        var inputTokens = promptTokens ?? fallbackInputTokens;
        var outputTokens = completionTokens ?? 0;

        var finishReason = candidate.TryGetProperty("finishReason", out var finishProp)
            ? finishProp.GetString()
            : null;

        var stopReason = hasToolUse ? "tool_use" : "end_turn";
        if (string.Equals(finishReason, "MAX_TOKENS", StringComparison.OrdinalIgnoreCase) && !hasToolUse)
        {
            stopReason = "max_tokens";
        }

        return new Dictionary<string, object?>
        {
            ["id"] = messageId,
            ["type"] = "message",
            ["role"] = "assistant",
            ["model"] = model,
            ["content"] = parts,
            ["stop_reason"] = stopReason,
            ["stop_sequence"] = null,
            ["usage"] = new Dictionary<string, object?>
            {
                ["input_tokens"] = inputTokens,
                ["output_tokens"] = outputTokens
            }
        };
    }

    private static bool TryExtractUsage(
        JsonElement response,
        JsonElement candidate,
        out UsageInfo usage)
    {
        usage = default;
        if (!TryGetUsageMetadata(response, candidate, out var promptTokens, out var completionTokens,
                out var totalTokens))
        {
            return false;
        }

        usage = new UsageInfo(promptTokens, completionTokens, totalTokens);
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

    private static bool TryGetArray(
        JsonElement element,
        string firstProperty,
        string secondProperty,
        out JsonElement arrayElement)
    {
        arrayElement = default;
        if (!element.TryGetProperty(firstProperty, out var first) || first.ValueKind != JsonValueKind.Object)
        {
            return false;
        }

        if (!first.TryGetProperty(secondProperty, out var array) || array.ValueKind != JsonValueKind.Array)
        {
            return false;
        }

        arrayElement = array;
        return true;
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
            doc = null!;
            return false;
        }
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

    private static bool IsClaudeTokenExpired(ClaudeAiOAuth claudeOauth)
    {
        if (claudeOauth.ExpiresAt <= 0)
        {
            return false;
        }

        // 提前一点刷新，避免临界点卡住（尤其是流式请求）
        const int expirySkewSeconds = 60;
        var nowUnixSeconds = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        return claudeOauth.ExpiresAt <= nowUnixSeconds + expirySkewSeconds;
    }


    private static void UpdateUsageFromResponse(StreamingState state, JsonElement response, JsonElement candidate)
    {
        if (!TryGetUsageMetadata(response, candidate, out var promptTokens, out var completionTokens, out _))
        {
            return;
        }

        if (promptTokens.HasValue)
        {
            state.InputTokens = promptTokens.Value;
            state.HasInputTokens = true;
        }

        if (completionTokens.HasValue)
        {
            state.OutputTokens = completionTokens.Value;
            state.HasOutputTokens = true;
        }
    }

    private static int EstimateInputTokens(AnthropicInput input)
    {
        var totalChars = 0;
        var imageCount = 0;

        void CountString(string? value)
        {
            if (!string.IsNullOrEmpty(value))
            {
                totalChars += value.Length;
            }
        }

        if (!string.IsNullOrWhiteSpace(input.System))
        {
            CountString(input.System);
        }

        if (input.Systems != null)
        {
            foreach (var system in input.Systems)
            {
                if (system?.Type == "text")
                {
                    CountString(system.Text);
                }
            }
        }

        if (input.Messages != null)
        {
            foreach (var message in input.Messages)
            {
                CountString(message.Content);
                if (message.Contents == null)
                {
                    continue;
                }

                foreach (var content in message.Contents)
                {
                    if (content == null)
                    {
                        continue;
                    }

                    CountString(content.Text);
                    CountString(content.Thinking);
                    if (content.Type == "image")
                    {
                        imageCount++;
                    }

                    if (content.Input != null)
                    {
                        CountString(JsonSerializer.Serialize(content.Input));
                    }

                    if (content.Content != null)
                    {
                        CountString(content.Content.ToString());
                    }
                }
            }
        }

        var tokenEstimate = totalChars / 4 + imageCount * 300;
        return Math.Max(1, tokenEstimate);
    }

    private static string ExtractToolResultOutput(object? content)
    {
        if (content == null)
        {
            return string.Empty;
        }

        if (content is string str)
        {
            return str;
        }

        if (content is IEnumerable<AnthropicMessageContent> list)
        {
            var first = list.FirstOrDefault();
            if (first?.Type == "text")
            {
                return first.Text ?? string.Empty;
            }
        }

        if (content is JsonElement json)
        {
            if (json.ValueKind == JsonValueKind.String)
            {
                return json.GetString() ?? string.Empty;
            }

            if (json.ValueKind == JsonValueKind.Array && json.GetArrayLength() > 0)
            {
                var first = json[0];
                if (first.ValueKind == JsonValueKind.Object
                    && first.TryGetProperty("type", out var typeProp)
                    && typeProp.GetString() == "text"
                    && first.TryGetProperty("text", out var textProp))
                {
                    return textProp.GetString() ?? string.Empty;
                }
            }
        }

        return content.ToString() ?? string.Empty;
    }

    private static object? RemoveNulls(object? value)
    {
        if (value == null)
        {
            return null;
        }

        if (value is JsonElement element)
        {
            if (element.ValueKind == JsonValueKind.Object)
            {
                var dict = new Dictionary<string, object?>();
                foreach (var prop in element.EnumerateObject())
                {
                    if (prop.Value.ValueKind == JsonValueKind.Null)
                    {
                        continue;
                    }

                    dict[prop.Name] = RemoveNulls(prop.Value);
                }

                return dict;
            }

            if (element.ValueKind == JsonValueKind.Array)
            {
                var list = new List<object?>();
                foreach (var item in element.EnumerateArray())
                {
                    if (item.ValueKind == JsonValueKind.Null)
                    {
                        continue;
                    }

                    list.Add(RemoveNulls(item));
                }

                return list;
            }

            return element.Deserialize<object>();
        }

        if (value is IDictionary<string, object?> dictValue)
        {
            var cleaned = new Dictionary<string, object?>();
            foreach (var (key, val) in dictValue)
            {
                if (val == null)
                {
                    continue;
                }

                cleaned[key] = RemoveNulls(val);
            }

            return cleaned;
        }

        if (value is IEnumerable<object?> listValue)
        {
            var cleaned = new List<object?>();
            foreach (var item in listValue)
            {
                if (item == null)
                {
                    continue;
                }

                cleaned.Add(RemoveNulls(item));
            }

            return cleaned;
        }

        return value;
    }

    private sealed record AntigravityComponents(
        string Model,
        List<Dictionary<string, object?>> Contents,
        Dictionary<string, object?>? SystemInstruction,
        List<Dictionary<string, object?>>? Tools,
        Dictionary<string, object?> GenerationConfig);

    private readonly record struct UsageInfo(int? PromptTokens, int? CompletionTokens, int? TotalTokens);

    private enum StreamingBlockType
    {
        None,
        Text,
        Thinking
    }

    private sealed class StreamingState
    {
        public StreamingState(string model, string messageId)
        {
            Model = model;
            MessageId = messageId;
        }

        public string Model { get; }
        public string MessageId { get; }

        public StreamingBlockType CurrentBlockType { get; private set; } = StreamingBlockType.None;
        public int CurrentBlockIndex { get; private set; } = -1;
        public bool HasToolUse { get; set; }
        public bool HasInputTokens { get; set; }
        public bool HasOutputTokens { get; set; }
        public int InputTokens { get; set; }
        public int OutputTokens { get; set; }
        public string? FinishReason { get; set; }
        public bool MessageStartSent { get; set; }
        public string? CurrentThinkingSignature { get; private set; }
        public bool HasThinkingSignature => !string.IsNullOrWhiteSpace(CurrentThinkingSignature);

        public string? CloseBlock()
        {
            if (CurrentBlockType == StreamingBlockType.None)
            {
                return null;
            }

            var payload = BuildEvent("content_block_stop", new Dictionary<string, object?>
            {
                ["type"] = "content_block_stop",
                ["index"] = CurrentBlockIndex
            });

            CurrentBlockType = StreamingBlockType.None;
            CurrentThinkingSignature = null;
            return payload;
        }

        public string OpenTextBlock()
        {
            CurrentBlockIndex++;
            CurrentBlockType = StreamingBlockType.Text;
            CurrentThinkingSignature = null;
            return BuildEvent("content_block_start", new Dictionary<string, object?>
            {
                ["type"] = "content_block_start",
                ["index"] = CurrentBlockIndex,
                ["content_block"] = new Dictionary<string, object?>
                {
                    ["type"] = "text",
                    ["text"] = string.Empty
                }
            });
        }

        public string OpenThinkingBlock(string? signature)
        {
            CurrentBlockIndex++;
            CurrentBlockType = StreamingBlockType.Thinking;
            CurrentThinkingSignature = string.IsNullOrWhiteSpace(signature) ? null : signature;
            var contentBlock = new Dictionary<string, object?>
            {
                ["type"] = "thinking",
                ["thinking"] = string.Empty
            };

            if (!string.IsNullOrWhiteSpace(signature))
            {
                contentBlock["signature"] = signature;
            }

            return BuildEvent("content_block_start", new Dictionary<string, object?>
            {
                ["type"] = "content_block_start",
                ["index"] = CurrentBlockIndex,
                ["content_block"] = contentBlock
            });
        }

        public string EmitTextDelta(string text)
        {
            return BuildEvent("content_block_delta", new Dictionary<string, object?>
            {
                ["type"] = "content_block_delta",
                ["index"] = CurrentBlockIndex,
                ["delta"] = new Dictionary<string, object?>
                {
                    ["type"] = "text_delta",
                    ["text"] = text
                }
            });
        }

        public string EmitThinkingDelta(string text)
        {
            return BuildEvent("content_block_delta", new Dictionary<string, object?>
            {
                ["type"] = "content_block_delta",
                ["index"] = CurrentBlockIndex,
                ["delta"] = new Dictionary<string, object?>
                {
                    ["type"] = "thinking_delta",
                    ["thinking"] = text
                }
            });
        }

        public string EmitSignatureDelta(string signature)
        {
            CurrentThinkingSignature = signature;
            return BuildEvent("content_block_delta", new Dictionary<string, object?>
            {
                ["type"] = "content_block_delta",
                ["index"] = CurrentBlockIndex,
                ["delta"] = new Dictionary<string, object?>
                {
                    ["type"] = "signature_delta",
                    ["signature"] = signature
                }
            });
        }

        public string EmitImageBlock(string? mimeType, string? data)
        {
            CurrentBlockIndex++;
            var block = BuildEvent("content_block_start", new Dictionary<string, object?>
            {
                ["type"] = "content_block_start",
                ["index"] = CurrentBlockIndex,
                ["content_block"] = new Dictionary<string, object?>
                {
                    ["type"] = "image",
                    ["source"] = new Dictionary<string, object?>
                    {
                        ["type"] = "base64",
                        ["media_type"] = mimeType ?? "image/png",
                        ["data"] = data ?? string.Empty
                    }
                }
            });

            var stop = BuildEvent("content_block_stop", new Dictionary<string, object?>
            {
                ["type"] = "content_block_stop",
                ["index"] = CurrentBlockIndex
            });

            return block + stop;
        }

        public string EmitToolUseBlock(string? toolId, string? toolName, JsonElement args)
        {
            CurrentBlockIndex++;
            var start = BuildEvent("content_block_start", new Dictionary<string, object?>
            {
                ["type"] = "content_block_start",
                ["index"] = CurrentBlockIndex,
                ["content_block"] = new Dictionary<string, object?>
                {
                    ["type"] = "tool_use",
                    ["id"] = toolId,
                    ["name"] = toolName,
                    ["input"] = new Dictionary<string, object?>()
                }
            });

            var inputJson = args.ValueKind == JsonValueKind.Undefined
                ? "{}"
                : JsonSerializer.Serialize(RemoveNulls(args));

            var delta = BuildEvent("content_block_delta", new Dictionary<string, object?>
            {
                ["type"] = "content_block_delta",
                ["index"] = CurrentBlockIndex,
                ["delta"] = new Dictionary<string, object?>
                {
                    ["type"] = "input_json_delta",
                    ["partial_json"] = inputJson
                }
            });

            var stop = BuildEvent("content_block_stop", new Dictionary<string, object?>
            {
                ["type"] = "content_block_stop",
                ["index"] = CurrentBlockIndex
            });

            return start + delta + stop;
        }

        private static string BuildEvent(string eventName, object data)
        {
            var json = JsonSerializer.Serialize(data);
            return $"event: {eventName}\n" + $"data: {json}\n\n";
        }
    }
}