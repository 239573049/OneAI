using System.Text;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using OneAI.Constants;
using OneAI.Data;
using OneAI.Entities;

namespace OneAI.Services.KiroOAuth;

/// <summary>
/// Kiro credentials import service
/// </summary>
public class KiroOAuthService(
    AppDbContext appDbContext,
    AccountQuotaCacheService quotaCacheService)
{
    private const string KiroVersion = "0.7.5";

    /// <summary>
    /// Import Kiro credentials and create account.
    /// </summary>
    public async Task<AIAccount> ImportKiroCredentialsAsync(
        AppDbContext dbContext,
        ImportKiroCredentialsRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.Credentials))
        {
            throw new ArgumentException("Kiro credentials are required");
        }

        var credentials = ParseCredentials(request.Credentials);
        if (string.IsNullOrWhiteSpace(credentials.AccessToken)
            && string.IsNullOrWhiteSpace(credentials.RefreshToken))
        {
            throw new ArgumentException("Kiro credentials missing accessToken/refreshToken");
        }

        credentials.Region = string.IsNullOrWhiteSpace(credentials.Region)
            ? "us-east-1"
            : credentials.Region.Trim();

        var account = new AIAccount
        {
            Provider = AIProviders.Kiro,
            ApiKey = string.Empty,
            BaseUrl = string.Empty,
            CreatedAt = DateTime.Now,
            Email = string.IsNullOrWhiteSpace(request.Email) ? null : request.Email.Trim(),
            Name = string.IsNullOrWhiteSpace(request.AccountName)
                ? "Kiro"
                : request.AccountName.Trim(),
            IsEnabled = true
        };

        account.SetKiroOAuth(credentials);

        await dbContext.AIAccounts.AddAsync(account);
        await dbContext.SaveChangesAsync();

        quotaCacheService.ClearAccountsCache();

        return account;
    }

    /// <summary>
    /// Import Kiro credentials in batch and create accounts.
    /// </summary>
    public async Task<ImportKiroBatchResult> ImportKiroBatchAsync(
        AppDbContext dbContext,
        ImportKiroBatchRequest request)
    {
        var result = new ImportKiroBatchResult();
        var existingEmails = new HashSet<string>();

        // 如果需要跳过已存在的账户，先获取所有已存在的Kiro账户邮箱
        if (request.SkipExisting)
        {
            var existingAccounts = await dbContext.AIAccounts
                .Where(a => a.Provider == AIProviders.Kiro && a.Email != null)
                .Select(a => a.Email!)
                .ToListAsync();

            foreach (var email in existingAccounts)
            {
                existingEmails.Add(email.ToLowerInvariant());
            }
        }

        foreach (var item in request.Accounts)
        {
            try
            {
                // 检查是否需要跳过
                if (request.SkipExisting && !string.IsNullOrWhiteSpace(item.Email))
                {
                    if (existingEmails.Contains(item.Email.ToLowerInvariant()))
                    {
                        result.SkippedCount++;
                        continue;
                    }
                }

                // 验证凭证
                if (string.IsNullOrWhiteSpace(item.AccessToken) && string.IsNullOrWhiteSpace(item.RefreshToken))
                {
                    throw new ArgumentException("Missing accessToken/refreshToken");
                }

                // 转换为KiroOAuthCredentialsDto
                var credentials = new KiroOAuthCredentialsDto
                {
                    AccessToken = item.AccessToken,
                    RefreshToken = item.RefreshToken,
                    ProfileArn = item.ProfileArn,
                    ExpiresAt = item.ExpiresAt,
                    AuthMethod = item.AuthMethod ?? "social",
                    Region = "us-east-1" // 默认区域
                };

                // 创建账户名称
                var accountName = !string.IsNullOrWhiteSpace(request.AccountNamePrefix)
                    ? $"{request.AccountNamePrefix}_{item.Email ?? item.Id ?? Guid.NewGuid().ToString()[..8]}"
                    : item.Email ?? item.Id ?? "Kiro";

                // 创建账户
                var account = new AIAccount
                {
                    Provider = AIProviders.Kiro,
                    ApiKey = string.Empty,
                    BaseUrl = string.Empty,
                    CreatedAt = DateTime.Now,
                    Email = string.IsNullOrWhiteSpace(item.Email) ? null : item.Email.Trim(),
                    Name = accountName,
                    IsEnabled = true
                };

                account.SetKiroOAuth(credentials);

                await dbContext.AIAccounts.AddAsync(account);
                await dbContext.SaveChangesAsync();

                result.SuccessCount++;
                result.SuccessItems.Add(new ImportSuccessItem
                {
                    OriginalId = item.Id,
                    AccountId = account.Id,
                    Email = account.Email,
                    AccountName = account.Name
                });

                // 添加到已存在集合，避免重复
                if (!string.IsNullOrWhiteSpace(account.Email))
                {
                    existingEmails.Add(account.Email.ToLowerInvariant());
                }
            }
            catch (Exception ex)
            {
                result.FailCount++;
                result.FailItems.Add(new ImportFailItem
                {
                    OriginalId = item.Id,
                    Email = item.Email,
                    ErrorMessage = ex.Message
                });
            }
        }

        // 清除缓存
        if (result.SuccessCount > 0)
        {
            quotaCacheService.ClearAccountsCache();
        }

        return result;
    }

    /// <summary>
    /// Refresh Kiro access token and persist updated credentials.
    /// </summary>
    public async Task RefreshKiroOAuthTokenAsync(AIAccount account)
    {
        var current = account.GetKiroOauth();
        if (current == null)
        {
            throw new InvalidOperationException("No Kiro credentials available");
        }

        if (string.IsNullOrWhiteSpace(current.RefreshToken))
        {
            throw new InvalidOperationException("No Kiro refresh token available");
        }

        var region = string.IsNullOrWhiteSpace(current.Region) ? "us-east-1" : current.Region.Trim();
        var authMethod = string.IsNullOrWhiteSpace(current.AuthMethod) ? "social" : current.AuthMethod.Trim();

        var refreshUrl = $"https://prod.{region}.auth.desktop.kiro.dev/refreshToken";
        var refreshIdcUrl = $"https://oidc.{region}.amazonaws.com/token";

        using var httpClient = new HttpClient { Timeout = TimeSpan.FromSeconds(30) };

        object requestBody = new Dictionary<string, object?>
        {
            ["refreshToken"] = current.RefreshToken
        };

        if (!string.Equals(authMethod, "social", StringComparison.OrdinalIgnoreCase))
        {
            requestBody = new Dictionary<string, object?>
            {
                ["clientId"] = current.ClientId,
                ["clientSecret"] = current.ClientSecret,
                ["grantType"] = "refresh_token",
                ["refreshToken"] = current.RefreshToken
            };
        }

        var refreshEndpoint = string.Equals(authMethod, "social", StringComparison.OrdinalIgnoreCase)
            ? refreshUrl
            : refreshIdcUrl;

        using var request = new HttpRequestMessage(HttpMethod.Post, refreshEndpoint);
        var payload = JsonSerializer.Serialize(requestBody);
        request.Content = new StringContent(payload, Encoding.UTF8, "application/json");

        if (!string.IsNullOrWhiteSpace(current.AccessToken))
        {
            request.Headers.TryAddWithoutValidation("Authorization", $"Bearer {current.AccessToken}");
        }

        request.Headers.TryAddWithoutValidation("User-Agent", $"OneAI/{KiroVersion}");
        request.Headers.TryAddWithoutValidation("amz-sdk-invocation-id", Guid.NewGuid().ToString());
        request.Headers.TryAddWithoutValidation("Origin", "https://app.kiro.dev");
        request.Headers.TryAddWithoutValidation("Referer", "https://app.kiro.dev/");

        var response = await httpClient.SendAsync(request);

        if (!response.IsSuccessStatusCode)
        {
            var error = await response.Content.ReadAsStringAsync();
            throw new Exception($"Kiro token refresh failed: HTTP {(int)response.StatusCode} - {error}");
        }

        var content = await response.Content.ReadAsStringAsync();
        using var doc = JsonDocument.Parse(content);
        var root = doc.RootElement;

        if (!root.TryGetProperty("accessToken", out var accessTokenElement))
        {
            throw new Exception("Kiro token refresh failed: missing accessToken");
        }

        current.AccessToken = accessTokenElement.GetString();
        if (root.TryGetProperty("refreshToken", out var refreshTokenElement))
        {
            current.RefreshToken = refreshTokenElement.GetString();
        }

        if (root.TryGetProperty("profileArn", out var profileArnElement))
        {
            current.ProfileArn = profileArnElement.GetString();
        }

        if (root.TryGetProperty("expiresIn", out var expiresInElement)
            && expiresInElement.ValueKind == JsonValueKind.Number)
        {
            var expiresInSeconds = expiresInElement.GetInt32();
            current.ExpiresAt = DateTime.UtcNow.AddSeconds(expiresInSeconds).ToString("O");
        }

        account.SetKiroOAuth(current);

        appDbContext.AIAccounts.Update(account);
        await appDbContext.SaveChangesAsync();

        quotaCacheService.ClearAccountsCache();
    }

    private static KiroOAuthCredentialsDto ParseCredentials(string raw)
    {
        var trimmed = raw.Trim();
        if (TryParseCredentials(trimmed, out var parsed))
        {
            return parsed;
        }

        try
        {
            var decoded = Encoding.UTF8.GetString(Convert.FromBase64String(trimmed));
            if (TryParseCredentials(decoded, out parsed))
            {
                return parsed;
            }
        }
        catch
        {
            // ignore base64 decode errors
        }

        throw new ArgumentException("Invalid Kiro credentials format");
    }

    private static bool TryParseCredentials(string json, out KiroOAuthCredentialsDto credentials)
    {
        credentials = null!;
        try
        {
            var parsed = JsonSerializer.Deserialize<KiroOAuthCredentialsDto>(json, new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true
            });

            if (parsed == null)
            {
                return false;
            }

            credentials = parsed;
            return true;
        }
        catch
        {
            return false;
        }
    }
}
