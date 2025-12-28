using OneAI.Constants;
using OneAI.Data;
using OneAI.Entities;

namespace OneAI.Services.OpenAIOAuth;

public class OpenAIOAuthService(
    OpenAiOAuthHelper authHelper,
    AppDbContext appDbContext,
    AccountQuotaCacheService quotaCacheService)
{
    /// <summary>
    /// 生成OpenAI OAuth授权链接
    /// </summary>
    /// <param name="request"></param>
    /// <param name="oAuthHelper"></param>
    /// <param name="sessionService"></param>
    /// <returns></returns>
    public object GenerateOpenAIOAuthUrl(
        GenerateOpenAiOAuthUrlRequest request,
        OpenAiOAuthHelper oAuthHelper,
        IOAuthSessionService sessionService)
    {
        // 如果前端没有传递 RedirectUri，使用默认配置
        var redirectUri = string.IsNullOrEmpty(request.RedirectUri)
            ? OpenAiOAuthConfig.RedirectUri
            : request.RedirectUri;

        var oAuthParams = oAuthHelper.GenerateOAuthParams(null, redirectUri);

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
    /// 处理OpenAI OAuth授权码
    /// </summary>
    /// <returns></returns>
    public async Task ExchangeOpenAIOAuthCode(
        AppDbContext dbContext,
        ExchangeOpenAiOAuthCodeRequest request,
        OpenAiOAuthHelper oAuthHelper,
        IOAuthSessionService sessionService)
    {
        // 从缓存中获取OAuth会话数据
        var sessionData = sessionService.GetSession(request.SessionId);
        if (sessionData == null)
        {
            throw new ArgumentException("OAuth会话已过期或无效，请重新获取授权链接");
        }

        // 使用会话数据中的参数进行token交换
        var tokenResponse = await oAuthHelper.ExchangeCodeForTokensAsync(
            request.AuthorizationCode,
            sessionData.CodeVerifier,
            sessionData.State,
            null, // 使用默认ClientId
            null, // 使用默认RedirectUri
            sessionData.Proxy ?? request.Proxy);

        // 获取用户信息
        var userInfo =
            await oAuthHelper.GetUserInfoAsync(tokenResponse.AccessToken, sessionData.Proxy ?? request.Proxy);

        // 格式化OAuth凭据
        var oauthCredentials = oAuthHelper.FormatOpenAiCredentials(tokenResponse, userInfo);

        var account = new AIAccount
        {
            Provider = AIProviders.OpenAI,
            ApiKey = string.Empty,
            BaseUrl = string.Empty,
            CreatedAt = DateTime.Now,
            Email = userInfo.Email,
            Name = userInfo.Name,
            IsEnabled = true
        };

        account.SetOpenAIOAuth(oauthCredentials);

        await dbContext.AIAccounts.AddAsync(account);
        await dbContext.SaveChangesAsync();

        // 清理已使用的会话数据
        sessionService.RemoveSession(request.SessionId);
    }

    public async Task RefreshOpenAiOAuthTokenAsync(AIAccount account)
    {
        if (string.IsNullOrEmpty(account.GetOpenAiOauth()?.RefreshToken))
            throw new InvalidOperationException("没有可用的OpenAI刷新令牌");


        // 使用OpenAiOAuthHelper刷新令牌
        var refreshResponse = await authHelper.TryRefreshTokenAsync(
            account.GetOpenAiOauth()!.RefreshToken);

        account.SetOpenAIOAuth(new OpenAiOauth()
        {
            RefreshToken = refreshResponse.RefreshToken,
            AccessToken = refreshResponse.AccessToken,
            Scopes = account.GetOpenAiOauth().Scopes,
            ExpiresAt = DateTimeOffset.Now.ToUnixTimeSeconds() + refreshResponse.ExpiresIn,
            IsMax = true,
            UserInfo = account.GetOpenAiOauth().UserInfo,
        });

        appDbContext.AIAccounts.Update(account);
        await appDbContext.SaveChangesAsync();

        // 清除账户列表缓存（因为账户的OAuth信息发生了变化）
        quotaCacheService.ClearAccountsCache();
    }
}
