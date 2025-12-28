using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Web;
using OneAI.Models;

namespace OneAI.Services.GeminiOAuth;

/// <summary>
/// Google Gemini Antigravity OAuth helper
/// </summary>
public class GeminiAntigravityOAuthHelper(ILogger<GeminiAntigravityOAuthHelper> logger)
{
    /// <summary>
    /// Generate a random state parameter.
    /// </summary>
    public string GenerateState()
    {
        using var rng = RandomNumberGenerator.Create();
        var bytes = new byte[32];
        rng.GetBytes(bytes);
        return Convert.ToBase64String(bytes)
            .TrimEnd('=')
            .Replace('+', '-')
            .Replace('/', '_');
    }

    /// <summary>
    /// Generate OAuth authorization URL.
    /// </summary>
    public string GenerateAuthUrl(string state, string? customRedirectUri = null)
    {
        var queryParams = HttpUtility.ParseQueryString(string.Empty);
        queryParams["access_type"] = "offline";
        queryParams["client_id"] = GeminiAntigravityOAuthConfig.ClientId;
        queryParams["prompt"] = "consent";
        queryParams["redirect_uri"] = customRedirectUri ?? GeminiAntigravityOAuthConfig.RedirectUri;
        queryParams["response_type"] = "code";
        queryParams["scope"] = GeminiAntigravityOAuthConfig.GetScopesString();
        queryParams["state"] = state;

        return $"{GeminiAntigravityOAuthConfig.AuthorizeUrl}?{queryParams}";
    }

    /// <summary>
    /// Generate OAuth parameters.
    /// </summary>
    public GeminiOAuthParams GenerateOAuthParams(string? customRedirectUri = null)
    {
        var state = GenerateState();
        var authUrl = GenerateAuthUrl(state, customRedirectUri);

        return new GeminiOAuthParams
        {
            AuthUrl = authUrl,
            State = state
        };
    }

    /// <summary>
    /// Parse authorization code or callback URL.
    /// </summary>
    public string ParseCallbackUrl(string input)
    {
        if (string.IsNullOrWhiteSpace(input))
            throw new ArgumentException("Please provide a valid authorization code or callback URL");

        var trimmedInput = input.Trim();

        if (trimmedInput.StartsWith("http://") || trimmedInput.StartsWith("https://"))
            try
            {
                var uri = new Uri(trimmedInput);
                var query = HttpUtility.ParseQueryString(uri.Query);
                var authorizationCode = query["code"];

                if (string.IsNullOrEmpty(authorizationCode))
                    throw new ArgumentException("No authorization code found in callback URL (missing code parameter)");

                return authorizationCode;
            }
            catch (UriFormatException)
            {
                throw new ArgumentException("Invalid callback URL format");
            }

        var cleanedCode = trimmedInput.Split('#')[0]?.Split('&')[0] ?? trimmedInput;

        if (string.IsNullOrEmpty(cleanedCode) || cleanedCode.Length < 10)
            throw new ArgumentException("Invalid authorization code format");

        return cleanedCode;
    }

    /// <summary>
    /// Exchange authorization code for tokens.
    /// </summary>
    public async Task<GeminiTokenResponse> ExchangeCodeForTokensAsync(
        string authorizationCode,
        string state,
        string? customRedirectUri = null,
        ProxyConfig? proxyConfig = null)
    {
        var cleanedCode = ParseCallbackUrl(authorizationCode);

        var parameters = new List<KeyValuePair<string, string>>
        {
            new("grant_type", "authorization_code"),
            new("code", cleanedCode),
            new("redirect_uri", customRedirectUri ?? GeminiAntigravityOAuthConfig.RedirectUri),
            new("client_id", GeminiAntigravityOAuthConfig.ClientId),
            new("client_secret", GeminiAntigravityOAuthConfig.ClientSecret)
        };

        using var httpClient = CreateHttpClientWithProxy(proxyConfig);

        try
        {
            logger.LogDebug("Attempting Google OAuth token exchange (antigravity)");

            var content = new FormUrlEncodedContent(parameters);

            httpClient.DefaultRequestHeaders.Clear();
            httpClient.DefaultRequestHeaders.Add("User-Agent", "OneAI-OAuth/1.0");
            httpClient.DefaultRequestHeaders.Add("Accept", "application/json");
            httpClient.Timeout = TimeSpan.FromSeconds(30);

            var response = await httpClient.PostAsync(GeminiAntigravityOAuthConfig.TokenUrl, content);

            if (!response.IsSuccessStatusCode)
            {
                var errorContent = await response.Content.ReadAsStringAsync();
                logger.LogError("Google OAuth token exchange failed (antigravity): HTTP {Status} - {Error}",
                    (int)response.StatusCode, errorContent);
                throw new Exception($"Token exchange failed: HTTP {(int)response.StatusCode} - {errorContent}");
            }

            var responseContent = await response.Content.ReadAsStringAsync();
            using var document = JsonDocument.Parse(responseContent);
            var root = document.RootElement;

            logger.LogInformation("Google OAuth token exchange successful (antigravity)");

            var accessToken = root.GetProperty("access_token").GetString() ?? string.Empty;
            var refreshToken = root.TryGetProperty("refresh_token", out var refreshElement)
                ? refreshElement.GetString() ?? string.Empty
                : string.Empty;
            var idToken = root.TryGetProperty("id_token", out var idElement)
                ? idElement.GetString() ?? string.Empty
                : string.Empty;
            var expiresIn = root.TryGetProperty("expires_in", out var expiresElement)
                ? expiresElement.GetInt64()
                : 3600;

            return new GeminiTokenResponse
            {
                AccessToken = accessToken,
                RefreshToken = refreshToken,
                IdToken = idToken,
                ExpiresAt = DateTimeOffset.Now.ToUnixTimeSeconds() + expiresIn,
                Scopes = GeminiAntigravityOAuthConfig.Scopes,
                TokenType = "Bearer"
            };
        }
        catch (HttpRequestException ex)
        {
            logger.LogError("Google OAuth token exchange failed with network error (antigravity): {Message}", ex.Message);
            throw new Exception("Token exchange failed: Network error or timeout");
        }
        catch (TaskCanceledException ex)
        {
            logger.LogError("Google OAuth token exchange timed out (antigravity): {Message}", ex.Message);
            throw new Exception("Token exchange failed: Request timed out");
        }
    }

    /// <summary>
    /// Refresh access token.
    /// </summary>
    public async Task<GeminiTokenResponse> RefreshTokenAsync(
        string refreshToken,
        ProxyConfig? proxyConfig = null)
    {
        var parameters = new List<KeyValuePair<string, string>>
        {
            new("grant_type", "refresh_token"),
            new("refresh_token", refreshToken),
            new("client_id", GeminiAntigravityOAuthConfig.ClientId),
            new("client_secret", GeminiAntigravityOAuthConfig.ClientSecret)
        };

        using var httpClient = CreateHttpClientWithProxy(proxyConfig);

        try
        {
            logger.LogDebug("Attempting Google OAuth token refresh (antigravity)");

            var content = new FormUrlEncodedContent(parameters);

            httpClient.DefaultRequestHeaders.Clear();
            httpClient.DefaultRequestHeaders.Add("User-Agent", "OneAI-OAuth/1.0");
            httpClient.DefaultRequestHeaders.Add("Accept", "application/json");
            httpClient.Timeout = TimeSpan.FromSeconds(30);

            var response = await httpClient.PostAsync(GeminiAntigravityOAuthConfig.TokenUrl, content);

            if (!response.IsSuccessStatusCode)
            {
                var errorContent = await response.Content.ReadAsStringAsync();
                logger.LogError("Google OAuth token refresh failed (antigravity): HTTP {Status} - {Error}",
                    (int)response.StatusCode, errorContent);
                throw new Exception($"Token refresh failed: HTTP {(int)response.StatusCode} - {errorContent}");
            }

            var responseContent = await response.Content.ReadAsStringAsync();
            using var document = JsonDocument.Parse(responseContent);
            var root = document.RootElement;

            logger.LogInformation("Google OAuth token refresh successful (antigravity)");

            var accessToken = root.GetProperty("access_token").GetString() ?? string.Empty;
            var expiresIn = root.TryGetProperty("expires_in", out var expiresElement)
                ? expiresElement.GetInt64()
                : 3600;

            return new GeminiTokenResponse
            {
                AccessToken = accessToken,
                RefreshToken = refreshToken,
                ExpiresAt = DateTimeOffset.Now.ToUnixTimeSeconds() + expiresIn,
                Scopes = GeminiAntigravityOAuthConfig.Scopes,
                TokenType = "Bearer"
            };
        }
        catch (HttpRequestException ex)
        {
            logger.LogError("Google OAuth token refresh failed with network error (antigravity): {Message}", ex.Message);
            throw new Exception("Token refresh failed: Network error or timeout");
        }
        catch (TaskCanceledException ex)
        {
            logger.LogError("Google OAuth token refresh timed out (antigravity): {Message}", ex.Message);
            throw new Exception("Token refresh failed: Request timed out");
        }
    }

    /// <summary>
    /// Fetch Google user info.
    /// </summary>
    public async Task<GeminiUserInfo> GetUserInfoAsync(string accessToken, ProxyConfig? proxyConfig = null)
    {
        using var httpClient = CreateHttpClientWithProxy(proxyConfig);

        try
        {
            logger.LogDebug("Fetching Google user info (antigravity)");

            httpClient.DefaultRequestHeaders.Clear();
            httpClient.DefaultRequestHeaders.Add("Authorization", $"Bearer {accessToken}");
            httpClient.DefaultRequestHeaders.Add("User-Agent", "OneAI-OAuth/1.0");
            httpClient.Timeout = TimeSpan.FromSeconds(30);

            var response = await httpClient.GetAsync(GeminiAntigravityOAuthConfig.UserInfoUrl);

            if (!response.IsSuccessStatusCode)
            {
                var errorContent = await response.Content.ReadAsStringAsync();
                logger.LogError("Failed to get Google user info (antigravity): HTTP {Status} - {Error}",
                    (int)response.StatusCode, errorContent);
                throw new Exception($"Failed to get user info: HTTP {(int)response.StatusCode}");
            }

            var responseContent = await response.Content.ReadAsStringAsync();
            var userInfo = JsonSerializer.Deserialize<GeminiUserInfo>(responseContent, new JsonSerializerOptions
            {
                PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower
            });

            logger.LogInformation("Successfully fetched Google user info (antigravity)");
            return userInfo ?? new GeminiUserInfo();
        }
        catch (HttpRequestException ex)
        {
            logger.LogError("Failed to get Google user info (antigravity): Network error - {Message}", ex.Message);
            throw;
        }
        catch (TaskCanceledException ex)
        {
            logger.LogError("Failed to get Google user info (antigravity): Timeout - {Message}", ex.Message);
            throw new Exception("Request timed out");
        }
    }

    /// <summary>
    /// Fetch project ID using Antigravity endpoints.
    /// </summary>
    public async Task<string?> FetchProjectIdAsync(string accessToken, ProxyConfig? proxyConfig = null)
    {
        var headers = new Dictionary<string, string>
        {
            ["User-Agent"] = GeminiAntigravityOAuthConfig.UserAgent,
            ["Authorization"] = $"Bearer {accessToken}"
        };

        try
        {
            logger.LogInformation("Antigravity: fetching project_id from loadCodeAssist");
            var projectId = await TryLoadCodeAssistAsync(GeminiAntigravityOAuthConfig.AntigravityApiUrl, headers, proxyConfig);
            if (!string.IsNullOrEmpty(projectId))
            {
                logger.LogInformation("Project_id fetched from loadCodeAssist: {ProjectId}", projectId);
                return projectId;
            }

            logger.LogWarning("loadCodeAssist did not return project_id, fallback to onboardUser");
        }
        catch (Exception ex)
        {
            logger.LogWarning("loadCodeAssist failed: {Message}", ex.Message);
        }

        try
        {
            var projectId = await TryOnboardUserAsync(GeminiAntigravityOAuthConfig.AntigravityApiUrl, headers, proxyConfig);
            if (!string.IsNullOrEmpty(projectId))
            {
                logger.LogInformation("Project_id fetched from onboardUser: {ProjectId}", projectId);
                return projectId;
            }

            logger.LogError("Unable to fetch project_id from loadCodeAssist and onboardUser");
            return null;
        }
        catch (Exception ex)
        {
            logger.LogError("onboardUser failed: {Message}", ex.Message);
            return null;
        }
    }

    private async Task<string?> TryLoadCodeAssistAsync(
        string apiBaseUrl,
        Dictionary<string, string> headers,
        ProxyConfig? proxyConfig = null)
    {
        var requestUrl = $"{apiBaseUrl.TrimEnd('/')}/v1internal:loadCodeAssist";
        var requestBody = new
        {
            metadata = new
            {
                ideType = "ANTIGRAVITY",
                platform = "PLATFORM_UNSPECIFIED",
                pluginType = "GEMINI"
            }
        };

        using var httpClient = CreateHttpClientWithProxy(proxyConfig);

        httpClient.DefaultRequestHeaders.Clear();
        foreach (var header in headers)
            httpClient.DefaultRequestHeaders.Add(header.Key, header.Value);
        httpClient.Timeout = TimeSpan.FromSeconds(30);

        var content = new StringContent(
            JsonSerializer.Serialize(requestBody),
            Encoding.UTF8,
            "application/json");

        var response = await httpClient.PostAsync(requestUrl, content);

        if (response.IsSuccessStatusCode)
        {
            var responseText = await response.Content.ReadAsStringAsync();

            using var document = JsonDocument.Parse(responseText);
            var root = document.RootElement;

            if (root.TryGetProperty("currentTier", out _))
            {
                if (root.TryGetProperty("cloudaicompanionProject", out var projectElement))
                {
                    var projectId = projectElement.GetString();
                    if (!string.IsNullOrEmpty(projectId))
                    {
                        return projectId;
                    }
                }

                return null;
            }

            return null;
        }

        var errorText = await response.Content.ReadAsStringAsync();
        throw new Exception($"HTTP {(int)response.StatusCode}: {(errorText.Length > 200 ? errorText.Substring(0, 200) : errorText)}");
    }

    private async Task<string?> TryOnboardUserAsync(
        string apiBaseUrl,
        Dictionary<string, string> headers,
        ProxyConfig? proxyConfig = null)
    {
        var requestUrl = $"{apiBaseUrl.TrimEnd('/')}/v1internal:onboardUser";

        var tierId = await GetOnboardTierAsync(apiBaseUrl, headers, proxyConfig);
        if (string.IsNullOrEmpty(tierId))
        {
            return null;
        }

        var requestBody = new
        {
            tierId,
            metadata = new
            {
                ideType = "ANTIGRAVITY",
                platform = "PLATFORM_UNSPECIFIED",
                pluginType = "GEMINI"
            }
        };

        using var httpClient = CreateHttpClientWithProxy(proxyConfig);

        const int maxAttempts = 5;
        var attempt = 0;

        while (attempt < maxAttempts)
        {
            attempt++;

            httpClient.DefaultRequestHeaders.Clear();
            foreach (var header in headers)
                httpClient.DefaultRequestHeaders.Add(header.Key, header.Value);
            httpClient.Timeout = TimeSpan.FromSeconds(30);

            var content = new StringContent(
                JsonSerializer.Serialize(requestBody),
                Encoding.UTF8,
                "application/json");

            var response = await httpClient.PostAsync(requestUrl, content);

            if (response.IsSuccessStatusCode)
            {
                var responseText = await response.Content.ReadAsStringAsync();
                using var document = JsonDocument.Parse(responseText);
                var root = document.RootElement;

                if (root.TryGetProperty("done", out var doneElement) && doneElement.GetBoolean())
                {
                    if (root.TryGetProperty("response", out var responseData))
                    {
                        if (responseData.TryGetProperty("cloudaicompanionProject", out var projectElement))
                        {
                            string? projectId = null;

                            if (projectElement.ValueKind == JsonValueKind.Object)
                            {
                                if (projectElement.TryGetProperty("id", out var idElement))
                                    projectId = idElement.GetString();
                            }
                            else if (projectElement.ValueKind == JsonValueKind.String)
                            {
                                projectId = projectElement.GetString();
                            }

                            if (!string.IsNullOrEmpty(projectId))
                            {
                                return projectId;
                            }
                        }
                    }

                    return null;
                }

                await Task.Delay(2000);
            }
            else
            {
                var errorText = await response.Content.ReadAsStringAsync();
                throw new Exception(
                    $"HTTP {(int)response.StatusCode}: {(errorText.Length > 200 ? errorText.Substring(0, 200) : errorText)}");
            }
        }

        return null;
    }

    private async Task<string?> GetOnboardTierAsync(
        string apiBaseUrl,
        Dictionary<string, string> headers,
        ProxyConfig? proxyConfig = null)
    {
        var requestUrl = $"{apiBaseUrl.TrimEnd('/')}/v1internal:loadCodeAssist";
        var requestBody = new
        {
            metadata = new
            {
                ideType = "ANTIGRAVITY",
                platform = "PLATFORM_UNSPECIFIED",
                pluginType = "GEMINI"
            }
        };

        using var httpClient = CreateHttpClientWithProxy(proxyConfig);

        httpClient.DefaultRequestHeaders.Clear();
        foreach (var header in headers)
            httpClient.DefaultRequestHeaders.Add(header.Key, header.Value);
        httpClient.Timeout = TimeSpan.FromSeconds(30);

        var content = new StringContent(
            JsonSerializer.Serialize(requestBody),
            Encoding.UTF8,
            "application/json");

        var response = await httpClient.PostAsync(requestUrl, content);

        if (response.IsSuccessStatusCode)
        {
            var responseText = await response.Content.ReadAsStringAsync();
            using var document = JsonDocument.Parse(responseText);
            var root = document.RootElement;

            if (root.TryGetProperty("allowedTiers", out var allowedTiers))
            {
                foreach (var tier in allowedTiers.EnumerateArray())
                {
                    if (tier.TryGetProperty("isDefault", out var isDefault) && isDefault.GetBoolean())
                    {
                        if (tier.TryGetProperty("id", out var idElement))
                        {
                            var tierId = idElement.GetString();
                            return tierId;
                        }
                    }
                }
            }

            return "LEGACY";
        }

        return null;
    }

    /// <summary>
    /// Fetch GCP projects list.
    /// </summary>
    public async Task<List<GeminiProject>?> GetProjectsAsync(string accessToken, ProxyConfig? proxyConfig = null)
    {
        using var httpClient = CreateHttpClientWithProxy(proxyConfig);

        try
        {
            logger.LogDebug("Fetching Google Cloud projects (antigravity)");

            httpClient.DefaultRequestHeaders.Clear();
            httpClient.DefaultRequestHeaders.Add("Authorization", $"Bearer {accessToken}");
            httpClient.DefaultRequestHeaders.Add("User-Agent", "geminicli-oauth/1.0");
            httpClient.Timeout = TimeSpan.FromSeconds(30);

            var response = await httpClient.GetAsync(GeminiOAuthConfig.ProjectsUrl);

            if (!response.IsSuccessStatusCode)
            {
                logger.LogWarning("Failed to fetch projects (antigravity): HTTP {Status}",
                    (int)response.StatusCode);
                return null;
            }

            var responseContent = await response.Content.ReadAsStringAsync();
            using var document = JsonDocument.Parse(responseContent);
            var root = document.RootElement;

            var projects = new List<GeminiProject>();

            if (root.TryGetProperty("projects", out var projectsElement))
            {
                foreach (var projectJson in projectsElement.EnumerateArray())
                {
                    var project = new GeminiProject
                    {
                        ProjectId = projectJson.TryGetProperty("projectId", out var id)
                            ? id.GetString()
                            : null,
                        ProjectName = projectJson.TryGetProperty("name", out var name)
                            ? name.GetString()
                            : null,
                        ProjectNumber = projectJson.TryGetProperty("projectNumber", out var number)
                            ? number.GetString()
                            : null,
                        State = projectJson.TryGetProperty("lifecycleState", out var state)
                            ? state.GetString()
                            : null
                    };
                    projects.Add(project);
                }
            }

            logger.LogInformation("Fetched {Count} Google Cloud projects (antigravity)", projects.Count);
            return projects;
        }
        catch (HttpRequestException ex)
        {
            logger.LogWarning("Failed to fetch projects (antigravity): Network error - {Message}", ex.Message);
            return null;
        }
        catch (TaskCanceledException ex)
        {
            logger.LogWarning("Failed to fetch projects (antigravity): Timeout - {Message}", ex.Message);
            return null;
        }
    }

    private HttpClient CreateHttpClientWithProxy(ProxyConfig? proxyConfig)
    {
        if (proxyConfig == null)
            return new HttpClient();

        try
        {
            var handler = new HttpClientHandler();
            var proxyUri = $"{proxyConfig.Type}://{proxyConfig.Host}:{proxyConfig.Port}";

            if (!string.IsNullOrEmpty(proxyConfig.Username) && !string.IsNullOrEmpty(proxyConfig.Password))
                proxyUri =
                    $"{proxyConfig.Type}://{proxyConfig.Username}:{proxyConfig.Password}@{proxyConfig.Host}:{proxyConfig.Port}";

            handler.Proxy = new WebProxy(proxyUri);
            handler.UseProxy = true;
            handler.AutomaticDecompression = DecompressionMethods.GZip | DecompressionMethods.Deflate | DecompressionMethods.Brotli;

            return new HttpClient(handler);
        }
        catch (Exception ex)
        {
            logger.LogWarning("Invalid proxy configuration: {Error}", ex.Message);
            return new HttpClient();
        }
    }
}
