using System.Net.Http.Headers;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using OneAI.Data;
using OneAI.Entities;

namespace OneAI.Services;

/// <summary>
/// 账户配额缓存服务
/// 负责管理账户配额信息的内存缓存，从响应头提取配额数据并提供查询接口
/// </summary>
public class AccountQuotaCacheService(
    IMemoryCache cache,
    ILogger<AccountQuotaCacheService> logger)
{
    private const string CACHE_KEY_PREFIX = "AccountQuota_";
    private const string ACCOUNTS_LIST_CACHE_KEY = "AccountsList_All";

    /// <summary>
    /// 从HTTP响应头提取配额信息
    /// </summary>
    /// <param name="accountId">账户ID</param>
    /// <param name="headers">HTTP响应头</param>
    /// <returns>提取的配额信息，如果提取失败则返回null</returns>
    public static AccountQuotaInfo? ExtractFromHeaders(int accountId, HttpResponseHeaders headers)
    {
        try
        {
            var info = new AccountQuotaInfo
            {
                AccountId = accountId,
                LastUpdatedAt = DateTime.UtcNow
            };

            // 提取计划类型
            if (TryGetHeaderValue(headers, "x-codex-plan-type", out var planType))
            {
                info.PlanType = planType;
            }

            // 提取主窗口配额信息
            if (TryGetHeaderValue(headers, "x-codex-primary-used-percent", out var primaryUsed))
            {
                info.PrimaryUsedPercent = int.Parse(primaryUsed);
            }

            if (TryGetHeaderValue(headers, "x-codex-primary-window-minutes", out var primaryWindow))
            {
                info.PrimaryWindowMinutes = int.Parse(primaryWindow);
            }

            if (TryGetHeaderValue(headers, "x-codex-primary-reset-at", out var primaryReset))
            {
                info.PrimaryResetAt = long.Parse(primaryReset);
            }

            if (TryGetHeaderValue(headers, "x-codex-primary-reset-after-seconds", out var primaryResetAfter))
            {
                info.PrimaryResetAfterSeconds = int.Parse(primaryResetAfter);
            }

            // 提取次级窗口配额信息
            if (TryGetHeaderValue(headers, "x-codex-secondary-used-percent", out var secondaryUsed))
            {
                info.SecondaryUsedPercent = int.Parse(secondaryUsed);
            }

            if (TryGetHeaderValue(headers, "x-codex-secondary-window-minutes", out var secondaryWindow))
            {
                info.SecondaryWindowMinutes = int.Parse(secondaryWindow);
            }

            if (TryGetHeaderValue(headers, "x-codex-secondary-reset-at", out var secondaryReset))
            {
                info.SecondaryResetAt = long.Parse(secondaryReset);
            }

            if (TryGetHeaderValue(headers, "x-codex-secondary-reset-after-seconds", out var secondaryResetAfter))
            {
                info.SecondaryResetAfterSeconds = int.Parse(secondaryResetAfter);
            }

            // 提取超限信息
            if (TryGetHeaderValue(headers, "x-codex-primary-over-secondary-limit-percent", out var overLimit))
            {
                info.PrimaryOverSecondaryLimitPercent = int.Parse(overLimit);
            }

            // 提取信用额度信息
            if (TryGetHeaderValue(headers, "x-codex-credits-has-credits", out var hasCredits))
            {
                info.HasCredits = bool.Parse(hasCredits);
            }

            if (TryGetHeaderValue(headers, "x-codex-credits-balance", out var creditsBalance))
            {
                info.CreditsBalance = creditsBalance;
            }

            if (TryGetHeaderValue(headers, "x-codex-credits-unlimited", out var creditsUnlimited))
            {
                info.CreditsUnlimited = bool.Parse(creditsUnlimited);
            }

            return info;
        }
        catch (Exception ex)
        {
            // 提取失败时返回null，调用方应处理这种情况
            return null;
        }
    }

    /// <summary>
    /// 尝试从响应头中获取指定键的值
    /// </summary>
    private static bool TryGetHeaderValue(HttpResponseHeaders headers, string key, out string value)
    {
        if (headers.TryGetValues(key, out var values))
        {
            value = values.FirstOrDefault() ?? "";
            return !string.IsNullOrEmpty(value);
        }

        value = "";
        return false;
    }

    /// <summary>
    /// 从 Anthropic 风格的 HTTP 响应头提取限流信息
    /// </summary>
    /// <param name="accountId">账户ID</param>
    /// <param name="headers">HTTP响应头</param>
    /// <returns>提取的配额信息，如果提取失败则返回null</returns>
    public static AccountQuotaInfo? ExtractFromAnthropicHeaders(int accountId, HttpResponseHeaders headers)
    {
        try
        {
            var info = new AccountQuotaInfo
            {
                AccountId = accountId,
                LastUpdatedAt = DateTime.UtcNow
            };

            bool hasAnyData = false;

            // 提取 Input tokens 限流信息
            if (TryGetHeaderValue(headers, "anthropic-ratelimit-input-tokens-limit", out var inputLimit))
            {
                info.InputTokensLimit = long.Parse(inputLimit);
                hasAnyData = true;
            }

            if (TryGetHeaderValue(headers, "anthropic-ratelimit-input-tokens-remaining", out var inputRemaining))
            {
                info.InputTokensRemaining = long.Parse(inputRemaining);
                hasAnyData = true;
            }

            if (TryGetHeaderValue(headers, "anthropic-ratelimit-input-tokens-reset", out var inputReset))
            {
                info.InputTokensResetAt = DateTime.Parse(inputReset).ToUniversalTime();
                hasAnyData = true;
            }

            // 提取 Output tokens 限流信息
            if (TryGetHeaderValue(headers, "anthropic-ratelimit-output-tokens-limit", out var outputLimit))
            {
                info.OutputTokensLimit = long.Parse(outputLimit);
                hasAnyData = true;
            }

            if (TryGetHeaderValue(headers, "anthropic-ratelimit-output-tokens-remaining", out var outputRemaining))
            {
                info.OutputTokensRemaining = long.Parse(outputRemaining);
                hasAnyData = true;
            }

            if (TryGetHeaderValue(headers, "anthropic-ratelimit-output-tokens-reset", out var outputReset))
            {
                info.OutputTokensResetAt = DateTime.Parse(outputReset).ToUniversalTime();
                hasAnyData = true;
            }

            // 提取 Total tokens 限流信息
            if (TryGetHeaderValue(headers, "anthropic-ratelimit-tokens-limit", out var tokensLimit))
            {
                info.TokensLimit = long.Parse(tokensLimit);
                hasAnyData = true;
            }

            if (TryGetHeaderValue(headers, "anthropic-ratelimit-tokens-remaining", out var tokensRemaining))
            {
                info.TokensRemaining = long.Parse(tokensRemaining);
                hasAnyData = true;
            }

            if (TryGetHeaderValue(headers, "anthropic-ratelimit-tokens-reset", out var tokensReset))
            {
                info.TokensResetAt = DateTime.Parse(tokensReset).ToUniversalTime();
                hasAnyData = true;
            }

            // 如果没有提取到任何数据，返回null
            return hasAnyData ? info : null;
        }
        catch (Exception ex)
        {
            // 提取失败时返回null，调用方应处理这种情况
            return null;
        }
    }

    /// <summary>
    /// 更新账户配额缓存
    /// </summary>
    /// <param name="quotaInfo">配额信息</param>
    public void UpdateQuota(AccountQuotaInfo quotaInfo)
    {
        var cacheKey = $"{CACHE_KEY_PREFIX}{quotaInfo.AccountId}";

        // 配额信息永久保存在内存中，不设置过期时间
        // 配额是否有效通过 AccountQuotaInfo.IsExpired() 方法判断（基于配额重置时间）
        var options = new MemoryCacheEntryOptions()
            .SetPriority(CacheItemPriority.High); // 高优先级，避免内存压力时被清除

        cache.Set(cacheKey, quotaInfo, options);

        logger.LogInformation(
            "更新账户 {AccountId} 配额缓存: {Status}",
            quotaInfo.AccountId,
            quotaInfo.GetStatusDescription());
    }

    /// <summary>
    /// 获取账户配额信息
    /// </summary>
    /// <param name="accountId">账户ID</param>
    /// <returns>配额信息，如果不存在则返回null</returns>
    public AccountQuotaInfo? GetQuota(int accountId)
    {
        var cacheKey = $"{CACHE_KEY_PREFIX}{accountId}";
        return cache.Get<AccountQuotaInfo>(cacheKey);
    }

    /// <summary>
    /// 标记账户为配额耗尽状态
    /// 用于处理429 Too Many Requests响应
    /// </summary>
    /// <param name="accountId">账户ID</param>
    /// <param name="resetAfterSeconds">配额重置剩余秒数</param>
    public void MarkAsExhausted(int accountId, int resetAfterSeconds)
    {
        var quotaInfo = GetQuota(accountId);

        if (quotaInfo == null)
        {
            // 如果没有现有配额信息，创建一个新的
            quotaInfo = new AccountQuotaInfo
            {
                AccountId = accountId,
                PrimaryUsedPercent = 100,
                SecondaryUsedPercent = 100,
                PrimaryResetAfterSeconds = resetAfterSeconds,
                LastUpdatedAt = DateTime.UtcNow
            };
        }
        else
        {
            // 更新现有配额信息
            quotaInfo.PrimaryUsedPercent = 100;
            quotaInfo.PrimaryResetAfterSeconds = resetAfterSeconds;
            quotaInfo.LastUpdatedAt = DateTime.UtcNow;
        }

        UpdateQuota(quotaInfo);

        logger.LogWarning(
            "账户 {AccountId} 配额已耗尽，将在 {Seconds} 秒后重置",
            accountId,
            resetAfterSeconds);
    }

    /// <summary>
    /// 获取多个账户的配额信息（批量查询）
    /// 用于智能账户分配时的批量查询
    /// </summary>
    /// <param name="accountIds">账户ID列表</param>
    /// <returns>账户ID到配额信息的映射（仅包含有效且未过期的配额信息）</returns>
    public Dictionary<int, AccountQuotaInfo> GetAllQuotas(List<int> accountIds)
    {
        var result = new Dictionary<int, AccountQuotaInfo>();

        foreach (var accountId in accountIds)
        {
            var quota = GetQuota(accountId);

            // 只返回存在且未过期的配额信息
            if (quota != null && !quota.IsExpired())
            {
                result[accountId] = quota;
            }
        }

        return result;
    }

    /// <summary>
    /// 清除指定账户的配额缓存
    /// </summary>
    /// <param name="accountId">账户ID</param>
    public void ClearQuota(int accountId)
    {
        var cacheKey = $"{CACHE_KEY_PREFIX}{accountId}";
        cache.Remove(cacheKey);

        logger.LogInformation("清除账户 {AccountId} 的配额缓存", accountId);
    }

    /// <summary>
    /// 清除所有账户的配额缓存
    /// </summary>
    public void ClearAllQuotas()
    {
        // 注意：IMemoryCache 不提供清除所有缓存的方法
        // 这里只是记录日志，实际的缓存会根据过期时间自动清除
        logger.LogInformation("请求清除所有账户配额缓存（将在过期时间后自动清除）");
    }

    /// <summary>
    /// 获取配额缓存统计信息（用于监控和调试）
    /// </summary>
    /// <param name="accountIds">要统计的账户ID列表</param>
    /// <returns>配额统计摘要</returns>
    public string GetQuotaStatistics(List<int> accountIds)
    {
        var quotas = GetAllQuotas(accountIds);

        if (!quotas.Any())
        {
            return "无可用的配额缓存信息";
        }

        var exhaustedCount = quotas.Values.Count(q => q.IsQuotaExhausted());
        var healthyCount = quotas.Values.Count(q => !q.IsQuotaExhausted());
        var avgHealthScore = quotas.Values.Average(q => q.GetHealthScore());

        return $"总账户: {quotas.Count}, 健康: {healthyCount}, 耗尽: {exhaustedCount}, 平均健康度: {avgHealthScore:F1}";
    }

    #region 账户列表缓存

    /// <summary>
    /// 设置账户列表缓存
    /// </summary>
    /// <param name="accounts">账户列表</param>
    public void SetAccountsCache(List<Entities.AIAccount> accounts)
    {
        var options = new MemoryCacheEntryOptions()
            .SetAbsoluteExpiration(TimeSpan.FromMinutes(30)) // 缓存30分钟
            .SetPriority(CacheItemPriority.High);

        cache.Set(ACCOUNTS_LIST_CACHE_KEY, accounts, options);

        logger.LogDebug("账户列表已缓存，共 {Count} 个账户", accounts.Count);
    }

    /// <summary>
    /// 获取账户列表缓存
    /// </summary>
    /// <returns>缓存的账户列表，如果不存在则返回null</returns>
    public List<Entities.AIAccount>? GetAccountsCache()
    {
        return cache.Get<List<Entities.AIAccount>>(ACCOUNTS_LIST_CACHE_KEY);
    }

    /// <summary>
    /// 清空账户列表缓存（在新增、删除、修改账户时调用）
    /// </summary>
    public void ClearAccountsCache()
    {
        cache.Remove(ACCOUNTS_LIST_CACHE_KEY);
        logger.LogInformation("账户列表缓存已清空");
    }

    #endregion

    #region 会话粘性缓存

    private const string CONVERSATION_ACCOUNT_PREFIX = "ConversationAccount_";

    /// <summary>
    /// 设置会话与账户的映射关系
    /// </summary>
    /// <param name="conversationId">会话ID</param>
    /// <param name="accountId">账户ID</param>
    public void SetConversationAccount(string conversationId, int accountId)
    {
        var cacheKey = $"{CONVERSATION_ACCOUNT_PREFIX}{conversationId}";

        // 缓存60分钟，会话通常不会持续太久
        var options = new MemoryCacheEntryOptions()
            .SetAbsoluteExpiration(TimeSpan.FromMinutes(60))
            .SetPriority(CacheItemPriority.Normal);

        cache.Set(cacheKey, accountId, options);

        logger.LogDebug("会话 {ConversationId} 已映射到账户 {AccountId}", conversationId, accountId);
    }

    /// <summary>
    /// 获取会话上次使用的账户ID
    /// </summary>
    /// <param name="conversationId">会话ID</param>
    /// <returns>账户ID，如果不存在则返回null</returns>
    public int? GetConversationAccount(string conversationId)
    {
        var cacheKey = $"{CONVERSATION_ACCOUNT_PREFIX}{conversationId}";
        return cache.Get<int?>(cacheKey);
    }

    /// <summary>
    /// 清除会话与账户的映射关系
    /// </summary>
    /// <param name="conversationId">会话ID</param>
    public void ClearConversationAccount(string conversationId)
    {
        var cacheKey = $"{CONVERSATION_ACCOUNT_PREFIX}{conversationId}";
        cache.Remove(cacheKey);

        logger.LogDebug("已清除会话 {ConversationId} 的账户映射", conversationId);
    }

    #endregion
}
