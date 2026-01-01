using System.Net;
using OneAI.Constants;
using OneAI.Data;
using OneAI.Entities;
using OneAI.Models;
using OneAI.Services.OpenAIOAuth;

namespace OneAI.Services.ClaudeCodeOAuth;

public class ClaudeCodeOAuthService(
    ClaudeCodeOAuthHelper authHelper,
    AppDbContext appDbContext,
    AccountQuotaCacheService quotaCacheService)
{
    /// <summary>
    /// 生成 Claude Code OAuth 授权链接
    /// </summary>
    public object GenerateClaudeCodeOAuthUrl(
        GenerateClaudeCodeOAuthUrlRequest request,
        ClaudeCodeOAuthHelper oAuthHelper,
        IOAuthSessionService sessionService)
    {
        var oAuthParams = oAuthHelper.GenerateOAuthParams();

        // 将OAuth会话数据存储到缓存中，用于后续验证
        var sessionData = new OAuthSessionData
        {
            CodeVerifier = oAuthParams.CodeVerifier,
            State = oAuthParams.State,
            CodeChallenge = oAuthParams.CodeChallenge,
            Proxy = request.Proxy,
            CreatedAt = DateTime.Now,
            ExpiresAt = DateTime.Now.AddMinutes(10) // 10分钟过期
        };

        // 存储会话数据
        sessionService.StoreSession(oAuthParams.State, sessionData);

        return new
        {
            authUrl = oAuthParams.AuthUrl,
            sessionId = oAuthParams.State, // 使用state作为sessionId
            state = oAuthParams.State,
            codeVerifier = oAuthParams.CodeVerifier,
            message = "请复制此链接到浏览器中进行授权，授权完成后将获得Authorization Code"
        };
    }

    /// <summary>
    /// 处理 Claude Code OAuth 授权码
    /// </summary>
    public async Task<AIAccount> ExchangeClaudeCodeOAuthCode(
        AppDbContext dbContext,
        ExchangeClaudeCodeOAuthCodeRequest request,
        ClaudeCodeOAuthHelper oAuthHelper,
        IOAuthSessionService sessionService)
    {
        // 从缓存中获取OAuth会话数据
        var sessionData = sessionService.GetSession(request.SessionId);
        if (sessionData == null)
        {
            throw new ArgumentException("OAuth会话已过期或无效，请重新获取授权链接");
        }

        // 使用会话数据中的参数进行 token 交换（支持直接粘贴回调 URL / code / code#state）
        var oauthToken = await oAuthHelper.ExchangeCodeForTokensAsync(
            request.AuthorizationCode,
            sessionData.CodeVerifier,
            sessionData.State,
            sessionData.Proxy ?? request.Proxy);

        var account = new AIAccount
        {
            Provider = AIProviders.Claude,
            ApiKey = string.Empty,
            BaseUrl = string.Empty,
            CreatedAt = DateTime.Now,
            Email = oauthToken.EmailAddress,
            Name = request.AccountName ?? oauthToken.EmailAddress,
            IsEnabled = true
        };

        account.SetClaudeOAuth(oauthToken);

        await dbContext.AIAccounts.AddAsync(account);
        await dbContext.SaveChangesAsync();

        // 清理已使用的会话数据
        sessionService.RemoveSession(request.SessionId);

        return account;
    }

    /// <summary>
    /// 刷新 Claude OAuth Token
    /// </summary>
    public async Task RefreshClaudeOAuthTokenAsync(AIAccount account, ProxyConfig? proxyConfig = null)
    {
        var currentOauth = account.GetClaudeOauth();
        if (string.IsNullOrEmpty(currentOauth?.RefreshToken))
            throw new InvalidOperationException("没有可用的 Claude 刷新令牌");

        var requestBody = new Dictionary<string, object?>
        {
            ["client_id"] = ClaudeAiOAuthConfig.ClientId,
            ["grant_type"] = "refresh_token",
            ["refresh_token"] = currentOauth.RefreshToken
        };

        using var httpClient = authHelper.CreateHttpClientWithProxy(proxyConfig);
        httpClient.Timeout = TimeSpan.FromSeconds(30);

        var response = await SendRefreshRequestAsync(httpClient, ClaudeAiOAuthConfig.TokenUrl, requestBody);
        if (!response.IsSuccessStatusCode
            && response.StatusCode == HttpStatusCode.Forbidden
            && !string.IsNullOrWhiteSpace(ClaudeAiOAuthConfig.TokenUrlFallback))
        {
            var fallbackResponse = await SendRefreshRequestAsync(
                httpClient,
                ClaudeAiOAuthConfig.TokenUrlFallback,
                requestBody);

            if (fallbackResponse.IsSuccessStatusCode)
            {
                response = fallbackResponse;
            }
        }

        if (!response.IsSuccessStatusCode)
        {
            var errorContent = await response.Content.ReadAsStringAsync();

            if (response.StatusCode == HttpStatusCode.Forbidden
                && response.Headers.TryGetValues("cf-mitigated", out var mitigated)
                && mitigated.Any(x => x.Contains("challenge", StringComparison.OrdinalIgnoreCase)))
            {
                var cfRay = response.Headers.TryGetValues("CF-RAY", out var rays)
                    ? rays.FirstOrDefault()
                    : null;

                var proxyHint = proxyConfig == null
                    ? "（当前未使用代理，请在请求中传递 ProxyConfig，建议使用可访问 console.anthropic.com 的住宅/海外代理）"
                    : "（当前已使用代理，如仍被拦截请更换出口 IP / 代理类型，或尝试住宅代理）";

                throw new Exception(
                    $"Claude OAuth Token 刷新被 Cloudflare 拦截（HTTP 403 / cf-mitigated: challenge）{proxyHint}"
                    + (string.IsNullOrWhiteSpace(cfRay) ? string.Empty : $"，CF-RAY: {cfRay}"));
            }

            throw new Exception($"Token refresh failed: HTTP {(int)response.StatusCode} - {errorContent}");
        }

        var responseContent = await response.Content.ReadFromJsonAsync<TokenResultDto>();
        if (responseContent == null)
            throw new Exception("Token refresh failed: empty response");

        var expiresIn = responseContent.expires_in ?? 3600;
        var scopeString = string.IsNullOrWhiteSpace(responseContent.scope)
            ? ClaudeAiOAuthConfig.Scopes
            : responseContent.scope;

        var updatedOauth = new ClaudeAiOAuth
        {
            AccessToken = responseContent.access_token,
            RefreshToken = responseContent.refresh_token,
            ExpiresAt = DateTimeOffset.Now.ToUnixTimeSeconds() + expiresIn,
            Scopes = scopeString.Split(' '),
            IsMax = true,
            EmailAddress = responseContent.account?.email_address
        };

        account.SetClaudeOAuth(updatedOauth);

        if (!string.IsNullOrWhiteSpace(updatedOauth.EmailAddress))
        {
            account.Email = updatedOauth.EmailAddress;
            account.Name ??= updatedOauth.EmailAddress;
        }

        appDbContext.AIAccounts.Update(account);
        await appDbContext.SaveChangesAsync();

        quotaCacheService.ClearAccountsCache();
    }

    private static async Task<HttpResponseMessage> SendRefreshRequestAsync(
        HttpClient httpClient,
        string url,
        Dictionary<string, object?> requestBody)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, url)
        {
            Content = JsonContent.Create(requestBody)
        };

        request.Headers.Add("Accept", "application/json");

        return await httpClient.SendAsync(request);
    }
}
