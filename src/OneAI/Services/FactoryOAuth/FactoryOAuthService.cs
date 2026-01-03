using System.Net;
using System.Text.Json;
using OneAI.Constants;
using OneAI.Data;
using OneAI.Entities;
using OneAI.Models;
using OneAI.Services.OpenAIOAuth;

namespace OneAI.Services.FactoryOAuth;

/// <summary>
/// Factory OAuth (WorkOS Device Authorization Flow) 服务
/// </summary>
public class FactoryOAuthService(
    ILogger<FactoryOAuthService> logger,
    AppDbContext appDbContext)
{
    /// <summary>
    /// 生成 Factory OAuth Device Code 授权信息（并写入会话缓存）
    /// </summary>
    public async Task<object> GenerateFactoryOAuthDeviceCode(
        GenerateFactoryOAuthDeviceCodeRequest request,
        IOAuthSessionService sessionService)
    {
        var deviceCode = await GenerateDeviceCodeAsync(request.Proxy);
        var sessionId = Guid.NewGuid().ToString("N");

        var sessionData = new OAuthSessionData
        {
            CodeVerifier = string.Empty,
            State = sessionId,
            CodeChallenge = string.Empty,
            Proxy = request.Proxy,
            CreatedAt = DateTime.UtcNow,
            ExpiresAt = DateTime.UtcNow.AddSeconds(deviceCode.ExpiresIn),
            DeviceCode = deviceCode.DeviceCode,
            DeviceUserCode = deviceCode.UserCode,
            DeviceVerificationUri = deviceCode.VerificationUri,
            DeviceVerificationUriComplete = deviceCode.VerificationUriComplete,
            DeviceIntervalSeconds = deviceCode.Interval,
            DeviceExpiresInSeconds = deviceCode.ExpiresIn
        };

        sessionService.StoreSession(sessionId, sessionData);

        return new
        {
            sessionId,
            userCode = deviceCode.UserCode,
            verificationUri = deviceCode.VerificationUri,
            verificationUriComplete = deviceCode.VerificationUriComplete,
            expiresIn = deviceCode.ExpiresIn,
            interval = deviceCode.Interval,
            message = "请在浏览器中打开链接完成授权，授权完成后回到此处点击“完成授权”"
        };
    }

    /// <summary>
    /// 完成 Factory OAuth Device Code 授权并创建账户
    /// </summary>
    public async Task<AIAccount> ExchangeFactoryOAuthDeviceCode(
        AppDbContext dbContext,
        ExchangeFactoryOAuthDeviceCodeRequest request,
        IOAuthSessionService sessionService)
    {
        var sessionData = sessionService.GetSession(request.SessionId);
        if (sessionData == null)
        {
            throw new ArgumentException("OAuth会话已过期或无效，请重新获取授权信息");
        }

        if (string.IsNullOrWhiteSpace(sessionData.DeviceCode))
        {
            throw new ArgumentException("Device Code 会话数据不完整，请重新获取授权信息");
        }

        var intervalSeconds = sessionData.DeviceIntervalSeconds ?? 5;
        var remainingSeconds = (int)Math.Max(0, (sessionData.ExpiresAt - DateTime.UtcNow).TotalSeconds);
        if (remainingSeconds <= 0)
        {
            throw new ArgumentException("Device Code 已过期，请重新获取授权信息");
        }

        // 参考 FactoryOAuthHelper 默认 60 次 * 5 秒（约 5 分钟），同时避免超出 Device Code 有效期
        var maxAttempts = Math.Min(
            60,
            Math.Max(1, (int)Math.Ceiling(remainingSeconds / (double)Math.Max(1, intervalSeconds))));

        var tokenResponse = await PollDeviceAuthorizationAsync(
            sessionData.DeviceCode,
            sessionData.Proxy ?? request.Proxy,
            maxAttempts,
            intervalSeconds);

        var oauthCredentials = FormatFactoryCredentials(tokenResponse);

        var email = tokenResponse.User.Email;
        var displayName = $"{tokenResponse.User.FirstName} {tokenResponse.User.LastName}".Trim();
        if (string.IsNullOrWhiteSpace(displayName))
        {
            displayName = email;
        }

        var account = new AIAccount
        {
            Provider = AIProviders.Factory,
            ApiKey = string.Empty,
            BaseUrl = string.Empty,
            CreatedAt = DateTime.Now,
            Email = email,
            Name = string.IsNullOrWhiteSpace(request.AccountName) ? displayName : request.AccountName,
            IsEnabled = true
        };

        account.SetFactoryOAuth(oauthCredentials);

        await dbContext.AIAccounts.AddAsync(account);
        await dbContext.SaveChangesAsync();

        // 清理已使用的会话数据
        sessionService.RemoveSession(request.SessionId);

        return account;
    }

    /// <summary>
    /// 生成 Factory Device Code
    /// </summary>
    public async Task<FactoryDeviceCodeResponse> GenerateDeviceCodeAsync(ProxyConfig? proxyConfig = null)
    {
        using var httpClient = CreateHttpClientWithProxy(proxyConfig);

        try
        {
            logger.LogDebug("Generating Factory device code");

            var parameters = new List<KeyValuePair<string, string>>
            {
                new("client_id", FactoryOAuthConfig.ClientId)
            };

            using var content = new FormUrlEncodedContent(parameters);

            httpClient.DefaultRequestHeaders.Clear();
            httpClient.DefaultRequestHeaders.Add("User-Agent", "Factory-CLI/1.0");
            httpClient.DefaultRequestHeaders.Add("Accept", "application/json");
            httpClient.Timeout = TimeSpan.FromSeconds(30);

            using var response = await httpClient.PostAsync(FactoryOAuthConfig.DeviceAuthUrl, content);

            if (!response.IsSuccessStatusCode)
            {
                var errorContent = await response.Content.ReadAsStringAsync();
                logger.LogError("Factory device code generation failed: HTTP {Status} - {Error}",
                    (int)response.StatusCode, errorContent);
                throw new Exception($"Device code generation failed: HTTP {(int)response.StatusCode}");
            }

            var responseContent = await response.Content.ReadAsStringAsync();
            using var document = JsonDocument.Parse(responseContent);
            var root = document.RootElement;

            logger.LogInformation("Factory device code generated successfully");

            return new FactoryDeviceCodeResponse
            {
                DeviceCode = root.GetProperty("device_code").GetString() ?? string.Empty,
                UserCode = root.GetProperty("user_code").GetString() ?? string.Empty,
                VerificationUri = root.GetProperty("verification_uri").GetString() ?? string.Empty,
                VerificationUriComplete = root.GetProperty("verification_uri_complete").GetString() ?? string.Empty,
                ExpiresIn = root.GetProperty("expires_in").GetInt32(),
                Interval = root.TryGetProperty("interval", out var interval) ? interval.GetInt32() : 5
            };
        }
        catch (HttpRequestException ex)
        {
            logger.LogError(ex, "Factory device code generation failed with network error: {Message}", ex.Message);
            throw new Exception("Device code generation failed: Network error");
        }
        catch (TaskCanceledException ex)
        {
            logger.LogError(ex, "Factory device code generation timed out: {Message}", ex.Message);
            throw new Exception("Device code generation failed: Request timed out");
        }
    }

    /// <summary>
    /// 轮询设备授权状态，直到用户完成授权并返回 token
    /// </summary>
    public async Task<FactoryTokenResponse> PollDeviceAuthorizationAsync(
        string deviceCode,
        ProxyConfig? proxyConfig = null,
        int maxAttempts = 60,
        int intervalSeconds = 5)
    {
        using var httpClient = CreateHttpClientWithProxy(proxyConfig);

        try
        {
            logger.LogDebug("Starting Factory device authorization polling");

            var parameters = new List<KeyValuePair<string, string>>
            {
                new("grant_type", "urn:ietf:params:oauth:grant-type:device_code"),
                new("device_code", deviceCode),
                new("client_id", FactoryOAuthConfig.ClientId)
            };

            for (var attempt = 0; attempt < maxAttempts; attempt++)
            {
                await Task.Delay(intervalSeconds * 1000);

                using var content = new FormUrlEncodedContent(parameters);

                httpClient.DefaultRequestHeaders.Clear();
                httpClient.DefaultRequestHeaders.Add("User-Agent", "Factory-CLI/1.0");
                httpClient.DefaultRequestHeaders.Add("Accept", "application/json");
                httpClient.Timeout = TimeSpan.FromSeconds(30);

                using var response = await httpClient.PostAsync(FactoryOAuthConfig.TokenUrl, content);
                var responseContent = await response.Content.ReadAsStringAsync();

                if (response.IsSuccessStatusCode)
                {
                    logger.LogInformation("Factory authorization successful");

                    var tokenResponse = JsonSerializer.Deserialize<FactoryTokenResponse>(
                        responseContent,
                        new JsonSerializerOptions
                        {
                            PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower
                        });

                    if (tokenResponse == null)
                    {
                        throw new Exception("Failed to deserialize token response");
                    }

                    return tokenResponse;
                }

                using var document = JsonDocument.Parse(responseContent);
                if (document.RootElement.TryGetProperty("error", out var errorElement))
                {
                    var error = errorElement.GetString();
                    if (string.Equals(error, "authorization_pending", StringComparison.Ordinal))
                    {
                        logger.LogDebug("Waiting for user authorization (attempt {Attempt}/{MaxAttempts})",
                            attempt + 1, maxAttempts);
                        continue;
                    }

                    if (string.Equals(error, "slow_down", StringComparison.Ordinal))
                    {
                        intervalSeconds += 5;
                        logger.LogDebug("Slowing down polling interval to {Interval} seconds", intervalSeconds);
                        continue;
                    }

                    logger.LogError("Factory authorization failed: {Error}", error);
                    throw new Exception($"Authorization failed: {error}");
                }
            }

            throw new Exception("Authorization timeout: User did not authorize within the time limit");
        }
        catch (HttpRequestException ex)
        {
            logger.LogError(ex, "Factory authorization polling failed with network error: {Message}", ex.Message);
            throw new Exception($"Authorization polling failed: Network error - {ex.Message}");
        }
        catch (TaskCanceledException ex)
        {
            logger.LogError(ex, "Factory authorization polling timed out: {Message}", ex.Message);
            throw new Exception("Authorization polling failed: Request timed out");
        }
        catch (JsonException ex)
        {
            logger.LogError(ex, "Factory authorization response parsing failed: {Message}", ex.Message);
            throw new Exception($"Authorization polling failed: Invalid response format - {ex.Message}");
        }
    }

    /// <summary>
    /// 将 token 响应格式化为 OneAI 可持久化的 OAuth 凭据对象
    /// </summary>
    public FactoryOauth FormatFactoryCredentials(FactoryTokenResponse tokenData)
    {
        // WorkOS 文档中 access_token 通常为 3600 秒有效期（JWT）
        var expiresAt = DateTimeOffset.Now.ToUnixTimeSeconds() + 3600;

        return new FactoryOauth
        {
            OrganizationId = tokenData.OrganizationId,
            AccessToken = tokenData.AccessToken,
            RefreshToken = tokenData.RefreshToken,
            AuthenticationMethod = tokenData.AuthenticationMethod,
            ExpiresAt = expiresAt,
            Scopes = ["openid", "profile", "email"],
            UserInfo = tokenData.User
        };
    }

    /// <summary>
    /// 刷新 Factory Access Token
    /// </summary>
    public async Task<FactoryOauth> RefreshTokenAsync(
        string refreshToken,
        string? organizationId = null,
        ProxyConfig? proxyConfig = null)
    {
        using var httpClient = CreateHttpClientWithProxy(proxyConfig);

        try
        {
            logger.LogDebug("Refreshing Factory access token");

            var parameters = new List<KeyValuePair<string, string>>
            {
                new("grant_type", "refresh_token"),
                new("refresh_token", refreshToken),
                new("client_id", FactoryOAuthConfig.ClientId)
            };

            if (!string.IsNullOrEmpty(organizationId))
            {
                parameters.Add(new KeyValuePair<string, string>("organization_id", organizationId));
            }

            using var content = new FormUrlEncodedContent(parameters);

            httpClient.DefaultRequestHeaders.Clear();
            httpClient.DefaultRequestHeaders.Add("User-Agent", "Factory-CLI/1.0");
            httpClient.DefaultRequestHeaders.Add("Accept", "application/json");
            httpClient.Timeout = TimeSpan.FromSeconds(30);

            using var response = await httpClient.PostAsync(FactoryOAuthConfig.TokenUrl, content);

            if (!response.IsSuccessStatusCode)
            {
                var errorContent = await response.Content.ReadAsStringAsync();
                logger.LogError("Factory token refresh failed: HTTP {Status} - {Error}",
                    (int)response.StatusCode, errorContent);
                throw new Exception($"Token refresh failed: HTTP {(int)response.StatusCode}");
            }

            var responseContent = await response.Content.ReadAsStringAsync();
            var tokenResponse = JsonSerializer.Deserialize<FactoryTokenResponse>(
                responseContent,
                new JsonSerializerOptions
                {
                    PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower
                });

            if (tokenResponse == null)
            {
                throw new Exception("Failed to deserialize refresh token response");
            }

            logger.LogInformation("Factory token refresh successful");

            return FormatFactoryCredentials(tokenResponse);
        }
        catch (HttpRequestException ex)
        {
            logger.LogError(ex, "Factory token refresh failed with network error: {Message}", ex.Message);
            throw new Exception($"Token refresh failed: Network error - {ex.Message}");
        }
        catch (TaskCanceledException ex)
        {
            logger.LogError(ex, "Factory token refresh timed out: {Message}", ex.Message);
            throw new Exception("Token refresh failed: Request timed out");
        }
        catch (JsonException ex)
        {
            logger.LogError(ex, "Factory token refresh response parsing failed: {Message}", ex.Message);
            throw new Exception($"Token refresh failed: Invalid response format - {ex.Message}");
        }
    }

    /// <summary>
    /// 刷新 Factory OAuth Token 并更新账户（类似于 ClaudeCodeOAuthService）
    /// </summary>
    public async Task RefreshFactoryOAuthTokenAsync(AIAccount account, ProxyConfig? proxyConfig = null)
    {
        var currentOauth = account.GetFactoryOauth();
        if (string.IsNullOrEmpty(currentOauth?.RefreshToken))
        {
            throw new InvalidOperationException("没有可用的 Factory 刷新令牌");
        }

        var updatedOauth = await RefreshTokenAsync(
            currentOauth.RefreshToken,
            currentOauth.OrganizationId,
            proxyConfig);

        account.SetFactoryOAuth(updatedOauth);

        if (updatedOauth.UserInfo != null && !string.IsNullOrWhiteSpace(updatedOauth.UserInfo.Email))
        {
            account.Email = updatedOauth.UserInfo.Email;
            account.Name ??= updatedOauth.UserInfo.Email;
        }

        appDbContext.AIAccounts.Update(account);
        await appDbContext.SaveChangesAsync();

        logger.LogInformation("Factory 账户 {AccountId} Token 刷新成功", account.Id);
    }

    /// <summary>
    /// 创建带代理的 HttpClient
    /// </summary>
    private HttpClient CreateHttpClientWithProxy(ProxyConfig? proxyConfig)
    {
        if (proxyConfig == null)
        {
            return new HttpClient();
        }

        try
        {
            var handler = new HttpClientHandler();
            var proxyUri = $"{proxyConfig.Type}://{proxyConfig.Host}:{proxyConfig.Port}";

            if (!string.IsNullOrEmpty(proxyConfig.Username) && !string.IsNullOrEmpty(proxyConfig.Password))
            {
                proxyUri =
                    $"{proxyConfig.Type}://{proxyConfig.Username}:{proxyConfig.Password}@{proxyConfig.Host}:{proxyConfig.Port}";
            }

            handler.Proxy = new WebProxy(proxyUri);
            handler.UseProxy = true;
            handler.AutomaticDecompression =
                DecompressionMethods.GZip | DecompressionMethods.Deflate | DecompressionMethods.Brotli;

            return new HttpClient(handler);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Invalid proxy configuration");
            return new HttpClient();
        }
    }
}

/// <summary>
/// Factory OAuth 配置（WorkOS User Management）
/// </summary>
public static class FactoryOAuthConfig
{
    public const string ClientId = "client_01HNM792M5G5G1A2THWPXKFMXB";

    public const string DeviceAuthUrl = "https://api.workos.com/user_management/authorize/device";

    public const string TokenUrl = "https://api.workos.com/user_management/authenticate";
}

/// <summary>
/// Factory Device Code 响应
/// </summary>
public class FactoryDeviceCodeResponse
{
    public string DeviceCode { get; set; } = string.Empty;

    public string UserCode { get; set; } = string.Empty;

    public string VerificationUri { get; set; } = string.Empty;

    public string VerificationUriComplete { get; set; } = string.Empty;

    public int ExpiresIn { get; set; }

    public int Interval { get; set; } = 5;
}

/// <summary>
/// Factory Token 响应
/// </summary>
public class FactoryTokenResponse
{
    public UserInfo User { get; set; } = new();

    public string OrganizationId { get; set; } = string.Empty;

    public string AccessToken { get; set; } = string.Empty;

    public string RefreshToken { get; set; } = string.Empty;

    public string AuthenticationMethod { get; set; } = string.Empty;

    public class UserInfo
    {
        public string Object { get; set; } = "user";

        public string Id { get; set; } = string.Empty;

        public string Email { get; set; } = string.Empty;

        public bool EmailVerified { get; set; }

        public string FirstName { get; set; } = string.Empty;

        public string LastName { get; set; } = string.Empty;

        public string ProfilePictureUrl { get; set; } = string.Empty;

        public string LastSignInAt { get; set; } = string.Empty;

        public string Locale { get; set; } = string.Empty;

        public string CreatedAt { get; set; } = string.Empty;

        public string UpdatedAt { get; set; } = string.Empty;

        public string? ExternalId { get; set; }
    }
}

/// <summary>
/// Factory OAuth 凭据（可用于持久化到 AIAccount.OAuthToken）
/// </summary>
public class FactoryOauth
{
    public string OrganizationId { get; set; } = string.Empty;

    public string AccessToken { get; set; } = string.Empty;

    public string RefreshToken { get; set; } = string.Empty;

    public string AuthenticationMethod { get; set; } = string.Empty;

    public long ExpiresAt { get; set; }

    public string[] Scopes { get; set; } = Array.Empty<string>();

    public FactoryTokenResponse.UserInfo? UserInfo { get; set; }
}
