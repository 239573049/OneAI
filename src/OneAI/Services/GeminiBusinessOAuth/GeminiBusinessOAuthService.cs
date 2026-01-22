using System.Text;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using OneAI.Constants;
using OneAI.Data;
using OneAI.Entities;

namespace OneAI.Services.GeminiBusinessOAuth;

/// <summary>
/// Gemini Business credentials import service
/// </summary>
public class GeminiBusinessOAuthService(AccountQuotaCacheService quotaCacheService)
{
    /// <summary>
    /// Import Gemini Business credentials and create account.
    /// </summary>
    public async Task<AIAccount> ImportGeminiBusinessCredentialsAsync(
        AppDbContext dbContext,
        ImportGeminiBusinessCredentialsRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.Credentials))
        {
            throw new ArgumentException("Gemini Business credentials are required");
        }

        var credentials = ParseCredentials(request.Credentials);
        if (string.IsNullOrWhiteSpace(credentials.SecureCSes)
            || string.IsNullOrWhiteSpace(credentials.Csesidx)
            || string.IsNullOrWhiteSpace(credentials.ConfigId))
        {
            throw new ArgumentException("Gemini Business credentials missing secure_c_ses/csesidx/config_id");
        }

        var account = new AIAccount
        {
            Provider = AIProviders.GeminiBusiness,
            ApiKey = string.Empty,
            BaseUrl = string.Empty,
            CreatedAt = DateTime.Now,
            Email = string.IsNullOrWhiteSpace(request.Email) ? null : request.Email.Trim(),
            Name = string.IsNullOrWhiteSpace(request.AccountName)
                ? "Gemini Business"
                : request.AccountName.Trim(),
            IsEnabled = !credentials.Disabled
        };

        account.SetGeminiBusinessOAuth(credentials);

        await dbContext.AIAccounts.AddAsync(account);
        await dbContext.SaveChangesAsync();

        quotaCacheService.ClearAccountsCache();

        return account;
    }

    /// <summary>
    /// Import Gemini Business credentials in batch and create accounts.
    /// </summary>
    public async Task<ImportGeminiBusinessBatchResult> ImportGeminiBusinessBatchAsync(
        AppDbContext dbContext,
        ImportGeminiBusinessBatchRequest request)
    {
        var result = new ImportGeminiBusinessBatchResult();
        var existingCsesidx = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var existingEmails = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        if (request.SkipExisting)
        {
            var existingAccounts = await dbContext.AIAccounts
                .Where(a => a.Provider == AIProviders.GeminiBusiness)
                .Select(a => new { a.Email, a.OAuthToken })
                .ToListAsync();

            foreach (var a in existingAccounts)
            {
                if (!string.IsNullOrWhiteSpace(a.Email))
                {
                    existingEmails.Add(a.Email.Trim());
                }

                if (string.IsNullOrWhiteSpace(a.OAuthToken))
                {
                    continue;
                }

                try
                {
                    var dto = JsonSerializer.Deserialize<GeminiBusinessCredentialsDto>(a.OAuthToken,
                        new JsonSerializerOptions { PropertyNameCaseInsensitive = true });

                    if (!string.IsNullOrWhiteSpace(dto?.Csesidx))
                    {
                        existingCsesidx.Add(dto.Csesidx.Trim());
                    }
                }
                catch
                {
                    // ignore malformed stored token
                }
            }
        }

        foreach (var item in request.Accounts)
        {
            try
            {
                var csesidx = item.Csesidx?.Trim();
                var email = item.Email?.Trim();

                if (request.SkipExisting)
                {
                    if (!string.IsNullOrWhiteSpace(csesidx) && existingCsesidx.Contains(csesidx))
                    {
                        result.SkippedCount++;
                        continue;
                    }

                    if (!string.IsNullOrWhiteSpace(email) && existingEmails.Contains(email))
                    {
                        result.SkippedCount++;
                        continue;
                    }
                }

                if (string.IsNullOrWhiteSpace(item.SecureCSes)
                    || string.IsNullOrWhiteSpace(item.Csesidx)
                    || string.IsNullOrWhiteSpace(item.ConfigId))
                {
                    throw new ArgumentException("Missing secure_c_ses/csesidx/config_id");
                }

                var credentials = new GeminiBusinessCredentialsDto
                {
                    SecureCSes = item.SecureCSes.Trim(),
                    HostCOses = string.IsNullOrWhiteSpace(item.HostCOses) ? null : item.HostCOses.Trim(),
                    Csesidx = item.Csesidx.Trim(),
                    ConfigId = item.ConfigId.Trim(),
                    ExpiresAt = string.IsNullOrWhiteSpace(item.ExpiresAt) ? null : item.ExpiresAt.Trim(),
                    Disabled = item.Disabled ?? false
                };

                var nameHint = email ?? item.Id ?? csesidx ?? Guid.NewGuid().ToString("N")[..8];
                var accountName = !string.IsNullOrWhiteSpace(request.AccountNamePrefix)
                    ? $"{request.AccountNamePrefix.Trim()}_{nameHint}"
                    : (email ?? item.Id ?? "Gemini Business");

                var account = new AIAccount
                {
                    Provider = AIProviders.GeminiBusiness,
                    ApiKey = string.Empty,
                    BaseUrl = string.Empty,
                    CreatedAt = DateTime.Now,
                    Email = string.IsNullOrWhiteSpace(email) ? null : email,
                    Name = accountName,
                    IsEnabled = !credentials.Disabled
                };

                account.SetGeminiBusinessOAuth(credentials);

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

                if (!string.IsNullOrWhiteSpace(credentials.Csesidx))
                {
                    existingCsesidx.Add(credentials.Csesidx.Trim());
                }

                if (!string.IsNullOrWhiteSpace(account.Email))
                {
                    existingEmails.Add(account.Email.Trim());
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

        if (result.SuccessCount > 0)
        {
            quotaCacheService.ClearAccountsCache();
        }

        return result;
    }

    private static GeminiBusinessCredentialsDto ParseCredentials(string raw)
    {
        var trimmed = raw.Trim();
        if (TryParseCredentials(trimmed, out var parsed))
        {
            return parsed;
        }

        // base64 (standard)
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

        // base64 (urlsafe)
        try
        {
            var normalized = trimmed.Replace('-', '+').Replace('_', '/');
            normalized = normalized.PadRight(normalized.Length + (4 - normalized.Length % 4) % 4, '=');
            var decoded = Encoding.UTF8.GetString(Convert.FromBase64String(normalized));
            if (TryParseCredentials(decoded, out parsed))
            {
                return parsed;
            }
        }
        catch
        {
            // ignore base64 decode errors
        }

        throw new ArgumentException("Invalid Gemini Business credentials format");
    }

    private static bool TryParseCredentials(string json, out GeminiBusinessCredentialsDto credentials)
    {
        credentials = null!;
        try
        {
            var parsed = JsonSerializer.Deserialize<GeminiBusinessCredentialsDto>(json, new JsonSerializerOptions
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
