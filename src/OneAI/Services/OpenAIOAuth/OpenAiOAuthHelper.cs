using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Web;
using OneAI.Models;

namespace OneAI.Services.OpenAIOAuth;

/// <summary>
///     OpenAI OAuth辅助类
/// </summary>
public class OpenAiOAuthHelper(ILogger<OpenAiOAuthHelper> logger)
{
    /// <summary>
    ///     生成随机状态参数
    /// </summary>
    public string GenerateState()
    {
        using var rng = RandomNumberGenerator.Create();
        var bytes = new byte[32]; // state参数保持32字节即可
        rng.GetBytes(bytes);
        return Convert.ToBase64String(bytes)
            .TrimEnd('=')
            .Replace('+', '-')
            .Replace('/', '_');
    }

    /// <summary>
    ///     生成代码验证器
    /// </summary>
    public string GenerateCodeVerifier()
    {
        using var rng = RandomNumberGenerator.Create();
        var bytes = new byte[64]; // 使用64字节，与Rust实现保持一致
        rng.GetBytes(bytes);
        return Convert.ToBase64String(bytes)
            .TrimEnd('=')
            .Replace('+', '-')
            .Replace('/', '_');
    }

    /// <summary>
    ///     生成代码挑战
    /// </summary>
    public string GenerateCodeChallenge(string codeVerifier)
    {
        using var sha256 = SHA256.Create();
        var bytes = Encoding.UTF8.GetBytes(codeVerifier);
        var hashBytes = sha256.ComputeHash(bytes);
        return Convert.ToBase64String(hashBytes)
            .TrimEnd('=')
            .Replace('+', '-')
            .Replace('/', '_');
    }

    /// <summary>
    ///     生成OAuth授权URL
    /// </summary>
    public string GenerateAuthUrl(string codeChallenge, string state, string? customClientId = null,
        string? customRedirectUri = null)
    {
        var queryParams = HttpUtility.ParseQueryString(string.Empty);
        queryParams["response_type"] = "code";
        queryParams["client_id"] = customClientId ?? OpenAiOAuthConfig.ClientId;
        queryParams["redirect_uri"] = customRedirectUri ?? OpenAiOAuthConfig.RedirectUri;
        queryParams["scope"] = OpenAiOAuthConfig.Scopes;
        queryParams["code_challenge"] = codeChallenge;
        queryParams["code_challenge_method"] = "S256";
        queryParams["state"] = state;
        queryParams["id_token_add_organizations"] = "true";
        queryParams["codex_cli_simplified_flow"] = "true";

        return $"{OpenAiOAuthConfig.AuthorizeUrl}?{queryParams}";
    }

    /// <summary>
    ///     生成OAuth参数
    /// </summary>
    public OpenAiOAuthParams GenerateOAuthParams(string? customClientId = null, string? customRedirectUri = null)
    {
        var state = GenerateState();
        var codeVerifier = GenerateCodeVerifier();
        var codeChallenge = GenerateCodeChallenge(codeVerifier);
        var authUrl = GenerateAuthUrl(codeChallenge, state, customClientId, customRedirectUri);

        return new OpenAiOAuthParams
        {
            AuthUrl = authUrl,
            CodeVerifier = codeVerifier,
            State = state,
            CodeChallenge = codeChallenge
        };
    }

    /// <summary>
    ///     解析回调URL中的授权码
    /// </summary>
    public string ParseCallbackUrl(string input)
    {
        if (string.IsNullOrWhiteSpace(input)) throw new ArgumentException("请提供有效的授权码或回调 URL");

        var trimmedInput = input.Trim();

        // 如果输入是URL，则从中提取code参数
        if (trimmedInput.StartsWith("http://") || trimmedInput.StartsWith("https://"))
            try
            {
                var uri = new Uri(trimmedInput);
                var query = HttpUtility.ParseQueryString(uri.Query);
                var authorizationCode = query["code"];

                if (string.IsNullOrEmpty(authorizationCode)) throw new ArgumentException("回调 URL 中未找到授权码 (code 参数)");

                return authorizationCode;
            }
            catch (UriFormatException)
            {
                throw new ArgumentException("无效的 URL 格式，请检查回调 URL 是否正确");
            }

        // 如果输入是纯授权码，直接返回
        var cleanedCode = trimmedInput.Split('#')[0]?.Split('&')[0] ?? trimmedInput;

        if (string.IsNullOrEmpty(cleanedCode) || cleanedCode.Length < 10)
            throw new ArgumentException("授权码格式无效，请确保复制了完整的 Authorization Code");

        return cleanedCode;
    }

    /// <summary>
    ///     使用授权码交换令牌
    /// </summary>
    public async Task<OpenAiTokenResponse> ExchangeCodeForTokensAsync(
        string authorizationCode,
        string codeVerifier,
        string state,
        string? customClientId = null,
        string? customRedirectUri = null,
        ProxyConfig? proxyConfig = null)
    {
        var cleanedCode = ParseCallbackUrl(authorizationCode);

        var parameters = new List<KeyValuePair<string, string>>
        {
            new("grant_type", "authorization_code"),
            new("code", cleanedCode),
            new("redirect_uri", customRedirectUri ?? OpenAiOAuthConfig.RedirectUri),
            new("client_id", customClientId ?? OpenAiOAuthConfig.ClientId),
            new("code_verifier", codeVerifier)
        };

        using var httpClient = CreateHttpClientWithProxy(proxyConfig);

        try
        {
            logger.LogDebug("🔄 Attempting OpenAI OAuth token exchange", new Dictionary<string, object>
            {
                ["url"] = OpenAiOAuthConfig.TokenUrl,
                ["codeLength"] = cleanedCode.Length,
                ["codePrefix"] = cleanedCode.Length > 10 ? cleanedCode[..10] + "..." : cleanedCode,
                ["hasProxy"] = proxyConfig != null
            });

            var content = new FormUrlEncodedContent(parameters);

            httpClient.DefaultRequestHeaders.Clear();
            httpClient.DefaultRequestHeaders.Add("User-Agent", "OpenAI-CLI/1.0");
            httpClient.DefaultRequestHeaders.Add("Accept", "application/json");
            httpClient.Timeout = TimeSpan.FromSeconds(30);

            var response = await httpClient.PostAsync(OpenAiOAuthConfig.TokenUrl, content);

            if (!response.IsSuccessStatusCode)
            {
                var errorContent = await response.Content.ReadAsStringAsync();
                logger.LogError("❌ OpenAI OAuth token exchange failed", new Dictionary<string, object>
                {
                    ["status"] = (int)response.StatusCode,
                    ["statusText"] = response.ReasonPhrase ?? "",
                    ["data"] = errorContent
                });

                throw new Exception($"Token exchange failed: HTTP {(int)response.StatusCode} - {errorContent}");
            }

            var responseContent = await response.Content.ReadAsStringAsync();
            using var document = JsonDocument.Parse(responseContent);
            var root = document.RootElement;

            logger.LogInformation("✅ OpenAI OAuth token exchange successful");

            var accessToken = root.GetProperty("access_token").GetString() ?? "";
            var refreshToken = root.TryGetProperty("refresh_token", out var refreshElement)
                ? refreshElement.GetString() ?? ""
                : "";
            var idToken = root.TryGetProperty("id_token", out var idElement)
                ? idElement.GetString() ?? ""
                : "";
            var expiresIn = root.TryGetProperty("expires_in", out var expiresElement)
                ? expiresElement.GetInt64()
                : 3600;
            var scopeString = root.TryGetProperty("scope", out var scope)
                ? scope.GetString()
                : OpenAiOAuthConfig.Scopes;

            return new OpenAiTokenResponse
            {
                AccessToken = accessToken,
                RefreshToken = refreshToken,
                IdToken = idToken,
                ExpiresAt = DateTimeOffset.Now.ToUnixTimeSeconds() + expiresIn,
                Scopes = scopeString?.Split(' ') ?? new[] { "openid", "profile", "email" },
                TokenType = "Bearer"
            };
        }
        catch (HttpRequestException ex)
        {
            logger.LogError("❌ OpenAI OAuth token exchange failed with network error: {Message}", ex.Message);
            throw new Exception("Token exchange failed: Network error or timeout");
        }
        catch (TaskCanceledException ex)
        {
            logger.LogError("❌ OpenAI OAuth token exchange timed out: {Message}", ex.Message);
            throw new Exception("Token exchange failed: Request timed out");
        }
    }

    /// <summary>
    ///     刷新访问令牌
    /// </summary>
    public async Task<OpenAiRefreshResponse> TryRefreshTokenAsync(
        string refreshToken,
        string? customClientId = null,
        ProxyConfig? proxyConfig = null)
    {
        var refreshRequest = new OpenAiRefreshRequest
        {
            ClientId = customClientId ?? OpenAiOAuthConfig.ClientId,
            GrantType = "refresh_token",
            RefreshToken = refreshToken,
            Scope = "openid profile email"
        };

        using var httpClient = CreateHttpClientWithProxy(proxyConfig);

        try
        {
            httpClient.DefaultRequestHeaders.Clear();
            httpClient.DefaultRequestHeaders.Add("User-Agent", "OpenAI-CLI/1.0");
            httpClient.Timeout = TimeSpan.FromSeconds(30);

            var json = JsonSerializer.Serialize(refreshRequest, JsonSerializerOptions.Web);
            var content = new StringContent(json, Encoding.UTF8, "application/json");

            var response = await httpClient.PostAsync(OpenAiOAuthConfig.TokenUrl, content);

            if (!response.IsSuccessStatusCode)
            {
                var errorContent = await response.Content.ReadAsStringAsync();
                logger.LogError("❌ OpenAI token refresh failed {e}", new Dictionary<string, object>
                {
                    ["status"] = (int)response.StatusCode,
                    ["statusText"] = response.ReasonPhrase ?? "",
                    ["data"] = errorContent
                });

                throw new Exception($"Failed to refresh token: HTTP {(int)response.StatusCode} - {errorContent}");
            }

            var responseContent = await response.Content.ReadAsStringAsync();
            var refreshResponse = JsonSerializer.Deserialize<OpenAiRefreshResponse>(responseContent,
                new JsonSerializerOptions
                {
                    PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower
                });

            if (refreshResponse == null)
            {
                throw new Exception("Failed to deserialize refresh token response");
            }

            logger.LogInformation("✅ OpenAI token refresh successful");

            return refreshResponse;
        }
        catch (HttpRequestException ex)
        {
            logger.LogError("❌ OpenAI token refresh failed with network error: {Message}", ex.Message);
            throw new Exception($"Token refresh failed: Network error - {ex.Message}");
        }
        catch (TaskCanceledException ex)
        {
            logger.LogError("❌ OpenAI token refresh timed out: {Message}", ex.Message);
            throw new Exception("Token refresh failed: Request timed out");
        }
        catch (JsonException ex)
        {
            logger.LogError("❌ OpenAI token refresh response parsing failed: {Message}", ex.Message);
            throw new Exception($"Token refresh failed: Invalid response format - {ex.Message}");
        }
    }

    /// <summary>
    ///     获取用户信息
    /// </summary>
    public async Task<OpenAiUserInfo> GetUserInfoAsync(string accessToken, ProxyConfig? proxyConfig = null)
    {
        using var httpClient = CreateHttpClientWithProxy(proxyConfig);

        try
        {
            httpClient.DefaultRequestHeaders.Clear();
            httpClient.DefaultRequestHeaders.Add("Authorization", $"Bearer {accessToken}");
            httpClient.DefaultRequestHeaders.Add("User-Agent", "OpenAI-CLI/1.0");
            httpClient.Timeout = TimeSpan.FromSeconds(30);

            var response = await httpClient.GetAsync(OpenAiOAuthConfig.UserInfoUrl);

            if (!response.IsSuccessStatusCode)
            {
                var errorContent = await response.Content.ReadAsStringAsync();
                logger.LogError("❌ Failed to get OpenAI user info: HTTP {Status} - {Error}",
                    (int)response.StatusCode, errorContent);
                throw new Exception($"Failed to get user info: HTTP {(int)response.StatusCode}");
            }

            var responseContent = await response.Content.ReadAsStringAsync();
            var userInfo = JsonSerializer.Deserialize<OpenAiUserInfo>(responseContent, new JsonSerializerOptions
            {
                PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower
            });

            return userInfo ?? new OpenAiUserInfo();
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "❌ Failed to get OpenAI user info");
            throw;
        }
    }

    /// <summary>
    ///     格式化OpenAI凭据
    /// </summary>
    public OpenAiOauth FormatOpenAiCredentials(OpenAiTokenResponse tokenData, OpenAiUserInfo userInfo)
    {
        return new OpenAiOauth
        {
            AccessToken = tokenData.AccessToken,
            RefreshToken = tokenData.RefreshToken,
            ExpiresAt = tokenData.ExpiresAt,
            Scopes = tokenData.Scopes,
            IsMax = true,
            UserInfo = userInfo
        };
    }

    /// <summary>
    ///     创建带代理的HttpClient
    /// </summary>
    private HttpClient CreateHttpClientWithProxy(ProxyConfig? proxyConfig)
    {
        if (proxyConfig == null) return new HttpClient();

        try
        {
            var handler = new HttpClientHandler();
            var proxyUri = $"{proxyConfig.Type}://{proxyConfig.Host}:{proxyConfig.Port}";

            if (!string.IsNullOrEmpty(proxyConfig.Username) && !string.IsNullOrEmpty(proxyConfig.Password))
                proxyUri =
                    $"{proxyConfig.Type}://{proxyConfig.Username}:{proxyConfig.Password}@{proxyConfig.Host}:{proxyConfig.Port}";

            handler.Proxy = new WebProxy(proxyUri);
            handler.UseProxy = true;

            return new HttpClient(handler);
        }
        catch (Exception ex)
        {
            logger.LogWarning("⚠️ Invalid proxy configuration: {Error}", ex.Message);
            return new HttpClient();
        }
    }
}