using OneAI.Constants;
using OneAI.Data;
using OneAI.Entities;
using OneAI.Services.OpenAIOAuth;

namespace OneAI.Services.GeminiOAuth;

/// <summary>
/// Google Gemini Antigravity OAuth service
/// </summary>
public class GeminiAntigravityOAuthService(
    GeminiAntigravityOAuthHelper authHelper,
    AppDbContext appDbContext,
    AccountQuotaCacheService quotaCacheService)
{
    /// <summary>
    /// Generate Gemini Antigravity OAuth authorization URL.
    /// </summary>
    public object GenerateGeminiAntigravityOAuthUrl(
        GenerateGeminiOAuthUrlRequest request,
        GeminiAntigravityOAuthHelper oAuthHelper,
        IOAuthSessionService sessionService)
    {
        var redirectUri = string.IsNullOrEmpty(request.RedirectUri)
            ? GeminiAntigravityOAuthConfig.RedirectUri
            : request.RedirectUri;

        var oAuthParams = oAuthHelper.GenerateOAuthParams(redirectUri);

        var sessionData = new OAuthSessionData
        {
            CodeVerifier = oAuthParams.State,
            State = oAuthParams.State,
            CodeChallenge = oAuthParams.State,
            Proxy = request.Proxy,
            CreatedAt = DateTime.Now,
            ExpiresAt = DateTime.Now.AddMinutes(10)
        };

        sessionService.StoreSession(oAuthParams.State, sessionData);

        return new
        {
            authUrl = oAuthParams.AuthUrl,
            sessionId = oAuthParams.State,
            state = oAuthParams.State,
            codeVerifier = oAuthParams.State, // For Gemini, codeVerifier is the same as state
            message = "Open this URL in your browser to authorize and obtain the authorization code"
        };
    }

    /// <summary>
    /// Exchange Gemini Antigravity OAuth code and create account.
    /// </summary>
    public async Task<AIAccount> ExchangeGeminiAntigravityOAuthCode(
        AppDbContext dbContext,
        ExchangeGeminiOAuthCodeRequest request,
        GeminiAntigravityOAuthHelper oAuthHelper,
        IOAuthSessionService sessionService)
    {
        var sessionData = sessionService.GetSession(request.SessionId);
        if (sessionData == null)
        {
            throw new ArgumentException("OAuth session expired or invalid, please regenerate the authorization URL");
        }

        var tokenResponse = await oAuthHelper.ExchangeCodeForTokensAsync(
            request.AuthorizationCode,
            sessionData.State,
            null,
            sessionData.Proxy ?? request.Proxy);

        var userInfo = await oAuthHelper.GetUserInfoAsync(
            tokenResponse.AccessToken,
            sessionData.Proxy ?? request.Proxy);

        string? detectedProjectId = null;
        bool autoDetected = false;

        if (string.IsNullOrEmpty(request.ProjectId))
        {
            try
            {
                detectedProjectId = await oAuthHelper.FetchProjectIdAsync(
                    tokenResponse.AccessToken,
                    sessionData.Proxy ?? request.Proxy);

                if (!string.IsNullOrEmpty(detectedProjectId))
                {
                    autoDetected = true;
                }
                else
                {
                    var projects = await oAuthHelper.GetProjectsAsync(
                        tokenResponse.AccessToken,
                        sessionData.Proxy ?? request.Proxy);

                    if (projects != null && projects.Any())
                    {
                        detectedProjectId = projects.First().ProjectId;
                        autoDetected = true;
                    }
                    else
                    {
                        throw new InvalidOperationException(
                            "No accessible GCP projects detected, please check permissions or provide a project ID");
                    }
                }
            }
            catch (Exception ex)
            {
                throw new InvalidOperationException(
                    $"Auto-detecting project ID failed: {ex.Message}, please provide a project ID manually", ex);
            }
        }
        else
        {
            detectedProjectId = request.ProjectId;
            autoDetected = false;
        }

        var expiresAt = DateTimeOffset.FromUnixTimeSeconds(tokenResponse.ExpiresAt);
        var expiryString = expiresAt.UtcDateTime.ToString("O");

        var account = new AIAccount
        {
            Provider = AIProviders.Gemini,
            ApiKey = string.Empty,
            BaseUrl = string.Empty,
            CreatedAt = DateTime.Now,
            Email = userInfo.Email,
            Name = userInfo.Name ?? request.AccountName,
            IsEnabled = true
        };

        account.SetGeminiOAuth(new GeminiOAuthCredentialsDto
        {
            ClientId = GeminiAntigravityOAuthConfig.ClientId,
            ClientSecret = GeminiAntigravityOAuthConfig.ClientSecret,
            Token = tokenResponse.AccessToken,
            RefreshToken = tokenResponse.RefreshToken,
            Scopes = tokenResponse.Scopes,
            TokenUri = GeminiAntigravityOAuthConfig.TokenUrl,
            ProjectId = detectedProjectId ?? string.Empty,
            Expiry = expiryString,
            AutoDetectedProject = autoDetected
        });

        await dbContext.AIAccounts.AddAsync(account);
        await dbContext.SaveChangesAsync();

        sessionService.RemoveSession(request.SessionId);

        return account;
    }

    /// <summary>
    /// Refresh Gemini Antigravity OAuth token.
    /// </summary>
    public async Task RefreshGeminiAntigravityOAuthTokenAsync(AIAccount account)
    {
        var currentCredentials = account.GetGeminiOauth();
        if (string.IsNullOrEmpty(currentCredentials?.RefreshToken))
            throw new InvalidOperationException("No Gemini Antigravity refresh token available");

        var refreshResponse = await authHelper.RefreshTokenAsync(currentCredentials.RefreshToken);

        var expiresAt = DateTimeOffset.FromUnixTimeSeconds(refreshResponse.ExpiresAt);
        var expiryString = expiresAt.UtcDateTime.ToString("O");

        account.SetGeminiOAuth(new GeminiOAuthCredentialsDto
        {
            ClientId = GeminiAntigravityOAuthConfig.ClientId,
            ClientSecret = GeminiAntigravityOAuthConfig.ClientSecret,
            Token = refreshResponse.AccessToken,
            RefreshToken = refreshResponse.RefreshToken,
            Scopes = refreshResponse.Scopes ?? currentCredentials.Scopes,
            TokenUri = GeminiAntigravityOAuthConfig.TokenUrl,
            ProjectId = currentCredentials.ProjectId,
            Expiry = expiryString,
            AutoDetectedProject = currentCredentials.AutoDetectedProject
        });

        appDbContext.AIAccounts.Update(account);
        await appDbContext.SaveChangesAsync();

        quotaCacheService.ClearAccountsCache();
    }
}
