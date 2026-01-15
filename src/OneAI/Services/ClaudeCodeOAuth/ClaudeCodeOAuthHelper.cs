using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Web;
using MihaZupan;
using OneAI.Models;

namespace OneAI.Services.ClaudeCodeOAuth;

public class ClaudeCodeOAuthHelper(ILogger<ClaudeCodeOAuthHelper> logger)
{
    public string GenerateState()
    {
        using var rng = RandomNumberGenerator.Create();
        var bytes = new byte[32];
        rng.GetBytes(bytes);
        return Convert.ToHexString(bytes).ToLowerInvariant();
    }

    public string GenerateCodeVerifier()
    {
        using var rng = RandomNumberGenerator.Create();
        var bytes = new byte[32];
        rng.GetBytes(bytes);
        return Convert.ToBase64String(bytes)
            .TrimEnd('=')
            .Replace('+', '-')
            .Replace('/', '_');
    }

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

    public string GenerateAuthUrl(string codeChallenge, string state)
    {
        var queryParams = HttpUtility.ParseQueryString(string.Empty);
        queryParams["code"] = "true";
        queryParams["client_id"] = ClaudeAiOAuthConfig.ClientId;
        queryParams["response_type"] = "code";
        queryParams["redirect_uri"] = ClaudeAiOAuthConfig.RedirectUri;
        queryParams["scope"] = ClaudeAiOAuthConfig.Scopes;
        queryParams["code_challenge"] = codeChallenge;
        queryParams["code_challenge_method"] = "S256";
        queryParams["state"] = state;

        return $"{ClaudeAiOAuthConfig.AuthorizeUrl}?{queryParams}";
    }

    public OAuthParams GenerateOAuthParams()
    {
        var state = GenerateState();
        var codeVerifier = GenerateCodeVerifier();
        var codeChallenge = GenerateCodeChallenge(codeVerifier);
        var authUrl = GenerateAuthUrl(codeChallenge, state);

        return new OAuthParams
        {
            AuthUrl = authUrl,
            CodeVerifier = codeVerifier,
            State = state,
            CodeChallenge = codeChallenge
        };
    }

    public async Task<ClaudeAiOAuth> ExchangeCodeForTokensAsync(string authorizationCode, string codeVerifier,
        string state, ProxyConfig? proxyConfig = null)
    {
        var (cleanedCode, parsedState) = ParseCodeAndState(authorizationCode);
        var requestState = string.IsNullOrWhiteSpace(parsedState) ? state : parsedState;

        var parameters = new Dictionary<string, string>
        {
            ["code"] = cleanedCode,
            ["state"] = requestState,
            ["grant_type"] = "authorization_code",
            ["client_id"] = ClaudeAiOAuthConfig.ClientId,
            ["redirect_uri"] = ClaudeAiOAuthConfig.RedirectUri,
            ["code_verifier"] = codeVerifier
        };

        using var httpClient = CreateHttpClientWithProxy(proxyConfig);

        try
        {
            logger.LogDebug("🔄 Attempting OAuth token exchange", new Dictionary<string, object>
            {
                ["url"] = ClaudeAiOAuthConfig.TokenUrl,
                ["codeLength"] = cleanedCode.Length,
                ["codePrefix"] = cleanedCode.Length > 10 ? cleanedCode[..10] + "..." : cleanedCode,
                ["hasProxy"] = proxyConfig != null,
                ["proxyType"] = proxyConfig?.Type ?? "none"
            });

            var jsonContent = JsonSerializer.Serialize(parameters);
            var content = new StringContent(jsonContent, Encoding.UTF8, "application/json");

            httpClient.Timeout = TimeSpan.FromSeconds(30);

            using var requestMessage = new HttpRequestMessage(HttpMethod.Post, ClaudeAiOAuthConfig.TokenUrl)
            {
                Content = content
            };
            requestMessage.Headers.Add("Accept", "application/json");

            var response = await httpClient.SendAsync(requestMessage);
            if (!response.IsSuccessStatusCode
                && response.StatusCode == HttpStatusCode.Forbidden
                && !string.IsNullOrWhiteSpace(ClaudeAiOAuthConfig.TokenUrlFallback))
            {
                using var fallbackRequest = new HttpRequestMessage(HttpMethod.Post, ClaudeAiOAuthConfig.TokenUrlFallback)
                {
                    Content = new StringContent(jsonContent, Encoding.UTF8, "application/json")
                };
                fallbackRequest.Headers.Add("Accept", "application/json");

                var fallbackResponse = await httpClient.SendAsync(fallbackRequest);
                if (fallbackResponse.IsSuccessStatusCode)
                {
                    response.Dispose();
                    response = fallbackResponse;
                }
                else
                {
                    fallbackResponse.Dispose();
                }
            }

            if (!response.IsSuccessStatusCode)
            {
                var errorContent = await response.Content.ReadAsStringAsync();

                // Cloudflare Bot Management / Challenge
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
                        $"Claude OAuth Token 交换被 Cloudflare 拦截（HTTP 403 / cf-mitigated: challenge）{proxyHint}"
                        + (string.IsNullOrWhiteSpace(cfRay) ? string.Empty : $"，CF-RAY: {cfRay}"));
                }

                logger.LogError("❌ OAuth token exchange failed with server error", new Dictionary<string, object>
                {
                    ["status"] = (int)response.StatusCode,
                    ["statusText"] = response.ReasonPhrase ?? "",
                    ["data"] = errorContent,
                    ["codeLength"] = cleanedCode.Length,
                    ["codePrefix"] = cleanedCode.Length > 10 ? cleanedCode[..10] + "..." : cleanedCode
                });

                var errorMessage = $"HTTP {(int)response.StatusCode}";
                if (!string.IsNullOrEmpty(errorContent))
                    try
                    {
                        using var errorDoc = JsonDocument.Parse(errorContent);
                        if (errorDoc.RootElement.TryGetProperty("error", out var error))
                        {
                            errorMessage += $": {error.GetString()}";
                            if (errorDoc.RootElement.TryGetProperty("error_description", out var description))
                                errorMessage += $" - {description.GetString()}";
                        }
                        else
                        {
                            errorMessage += $": {errorContent}";
                        }
                    }
                    catch
                    {
                        errorMessage += $": {errorContent}";
                    }

                throw new Exception($"Token exchange failed: {errorMessage}");
            }

            var responseContent = await response.Content.ReadFromJsonAsync<TokenResultDto>();
            if (responseContent == null)
                throw new Exception("Token exchange failed: empty response");

            var expiresIn = responseContent.expires_in ?? 3600;
            var scopeString = string.IsNullOrWhiteSpace(responseContent.scope)
                ? ClaudeAiOAuthConfig.Scopes
                : responseContent.scope;

            return new ClaudeAiOAuth
            {
                AccessToken = responseContent.access_token,
                EmailAddress = responseContent.account?.email_address,
                RefreshToken = responseContent.refresh_token,
                ExpiresAt = DateTimeOffset.Now.ToUnixTimeSeconds() + expiresIn,
                Scopes = scopeString.Split(' '),
                IsMax = true,
            };
        }
        catch (HttpRequestException ex)
        {
            logger.LogError("❌ OAuth token exchange failed with network error", new Dictionary<string, object>
            {
                ["message"] = ex.Message,
                ["hasProxy"] = proxyConfig != null
            });
            throw new Exception("Token exchange failed: No response from server (network error or timeout)");
        }
        catch (TaskCanceledException ex)
        {
            logger.LogError("❌ OAuth token exchange failed with timeout", new Dictionary<string, object>
            {
                ["message"] = ex.Message,
                ["hasProxy"] = proxyConfig != null
            });
            throw new Exception("Token exchange failed: Request timed out");
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "❌ OAuth token exchange failed with unknown error");
            throw new Exception($"Token exchange failed: {ex.Message}");
        }
    }

    public string ParseCallbackUrl(string input)
    {
        return ParseCodeAndState(input).Code;
    }

    private (string Code, string? State) ParseCodeAndState(string input)
    {
        if (string.IsNullOrWhiteSpace(input)) throw new ArgumentException("请提供有效的授权码或回调 URL");

        var trimmedInput = input.Trim();

        if (trimmedInput.StartsWith("http://") || trimmedInput.StartsWith("https://"))
            try
            {
                var uri = new Uri(trimmedInput);
                var query = HttpUtility.ParseQueryString(uri.Query);
                var authorizationCode = query["code"];

                if (string.IsNullOrEmpty(authorizationCode)) throw new ArgumentException("回调 URL 中未找到授权码 (code 参数)");

                var urlState = query["state"];
                if (string.IsNullOrWhiteSpace(urlState) && !string.IsNullOrWhiteSpace(uri.Fragment))
                {
                    urlState = uri.Fragment.TrimStart('#');
                }

                return (authorizationCode, string.IsNullOrWhiteSpace(urlState) ? null : urlState);
            }
            catch (UriFormatException)
            {
                throw new ArgumentException("无效的 URL 格式，请检查回调 URL 是否正确");
            }

        var parts = trimmedInput.Split('#', 2);
        var codePart = parts[0];
        var statePart = parts.Length > 1 ? parts[1] : null;

        var cleanedCode = codePart.Split('&')[0] ?? codePart;

        if (string.IsNullOrEmpty(cleanedCode) || cleanedCode.Length < 10)
            throw new ArgumentException("授权码格式无效，请确保复制了完整的 Authorization Code");

        var validCodePattern = new Regex(@"^[A-Za-z0-9_-]+$");
        if (!validCodePattern.IsMatch(cleanedCode))
            throw new ArgumentException("授权码包含无效字符，请检查是否复制了正确的 Authorization Code");

        return (cleanedCode, string.IsNullOrWhiteSpace(statePart) ? null : statePart);
    }

    public ClaudeCredentials FormatClaudeCredentials(ClaudeAiOAuth tokenData)
    {
        return new ClaudeCredentials
        {
            ClaudeAiOauth = new ClaudeAiOAuth
            {
                AccessToken = tokenData.AccessToken,
                RefreshToken = tokenData.RefreshToken,
                ExpiresAt = tokenData.ExpiresAt,
                Scopes = tokenData.Scopes,
                IsMax = tokenData.IsMax
            }
        };
    }

    internal HttpClient CreateHttpClientWithProxy(ProxyConfig? proxyConfig)
    {
        if (proxyConfig == null) return new HttpClient();

        try
        {
            if (string.IsNullOrWhiteSpace(proxyConfig.Host) || proxyConfig.Port <= 0)
            {
                throw new ArgumentException("ProxyConfig 缺少 Host/Port");
            }

            var proxyType = string.IsNullOrWhiteSpace(proxyConfig.Type)
                ? "http"
                : proxyConfig.Type.Trim().ToLowerInvariant();

            HttpClientHandler handler;
            if (proxyType is "socks5" or "socks")
            {
                var socksProxy = string.IsNullOrEmpty(proxyConfig.Username) ||
                                 string.IsNullOrEmpty(proxyConfig.Password)
                    ? new HttpToSocks5Proxy(proxyConfig.Host, proxyConfig.Port)
                    : new HttpToSocks5Proxy(proxyConfig.Host, proxyConfig.Port, proxyConfig.Username,
                        proxyConfig.Password);

                handler = new HttpClientHandler
                {
                    Proxy = socksProxy,
                    UseProxy = true,
                    AutomaticDecompression = DecompressionMethods.All
                };
            }
            else
            {
                var proxyUri = new UriBuilder(proxyType, proxyConfig.Host, proxyConfig.Port).Uri;
                var webProxy = new WebProxy(proxyUri);

                if (!string.IsNullOrEmpty(proxyConfig.Username) && !string.IsNullOrEmpty(proxyConfig.Password))
                {
                    webProxy.Credentials = new NetworkCredential(proxyConfig.Username, proxyConfig.Password);
                }

                handler = new HttpClientHandler
                {
                    Proxy = webProxy,
                    UseProxy = true,
                    AutomaticDecompression = DecompressionMethods.All
                };
            }

            return new HttpClient(handler);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "⚠️ Invalid proxy configuration");
            return new HttpClient();
        }
    }
}

public class ClaudeCredentials
{
    public ClaudeAiOAuth ClaudeAiOauth { get; set; } = new();
}

public class ClaudeAiOAuth
{
    public string AccessToken { get; set; } = string.Empty;
    public string RefreshToken { get; set; } = string.Empty;
    public long ExpiresAt { get; set; }
    public string[] Scopes { get; set; } = Array.Empty<string>();
    public bool IsMax { get; set; } = true;

    public string? EmailAddress { get; set; }
}

public class OAuthParams
{
    public string AuthUrl { get; set; } = string.Empty;
    public string CodeVerifier { get; set; } = string.Empty;
    public string State { get; set; } = string.Empty;
    public string CodeChallenge { get; set; } = string.Empty;
}

public class TokenResultDto
{
    public string token_type { get; set; }
    public string access_token { get; set; }
    public int? expires_in { get; set; }
    public string refresh_token { get; set; }
    
    public string scope { get; set; }
    
    public TokenResultDtoOrganization? organization { get; set; }
    
    public TokenResultDtoAccount? account { get; set; }
}

public class TokenResultDtoOrganization
{
    public string uuid { get; set; }
    public string name { get; set; }
}

public class TokenResultDtoAccount
{
    public string uuid { get; set; }
    public string email_address { get; set; }
}
