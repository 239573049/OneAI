using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using OneAI.Constants;
using OneAI.Data;
using OneAI.Entities;
using OneAI.Extensions;
using OneAI.Models;
using OneAI.Services.AI;
using OneAI.Services.ClaudeCodeOAuth;
using OneAI.Services.FactoryOAuth;
using OneAI.Services.OpenAIOAuth;

namespace OneAI.Services;

public class AIAccountService
{
    private readonly AppDbContext _appDbContext;
    private readonly AccountQuotaCacheService _quotaCache;
    private readonly OpenAIOAuthService _openAiOAuthService;
    private readonly ClaudeCodeOAuthService _claudeCodeOAuthService;
    private readonly FactoryOAuthService _factoryOAuthService;
    private readonly KiroService _kiroService;
    private readonly ILogger<AIAccountService> _logger;

    public AIAccountService(
        AppDbContext appDbContext,
        AccountQuotaCacheService quotaCache,
        OpenAIOAuthService openAiOAuthService,
        ClaudeCodeOAuthService claudeCodeOAuthService,
        FactoryOAuthService factoryOAuthService,
        KiroService kiroService,
        ILogger<AIAccountService> logger)
    {
        _appDbContext = appDbContext;
        _quotaCache = quotaCache;
        _openAiOAuthService = openAiOAuthService;
        _claudeCodeOAuthService = claudeCodeOAuthService;
        _factoryOAuthService = factoryOAuthService;
        _kiroService = kiroService;
        _logger = logger;
    }

    /// <summary>
    /// 智能获取最优 AI 账户
    /// 根据配额使用情况、账户健康度等因素智能选择最佳账户
    /// </summary>
    /// <param name="model">请求的模型名称</param>
    /// <returns>最优账户，如果没有可用账户则返回null</returns>
    public async Task<AIAccount?> GetAIAccount(string model, string provider)
    {
        // 1. 尝试从缓存获取账户列表，如果缓存不存在则从数据库查询
        var allAccounts = _quotaCache.GetAccountsCache();
        if (allAccounts == null)
        {
            _logger.LogDebug("账户列表缓存未命中，从数据库加载");
            allAccounts = await _appDbContext.AIAccounts.ToListAsync();
            _quotaCache.SetAccountsCache(allAccounts);
        }
        else
        {
            _logger.LogDebug("账户列表缓存命中，共 {Count} 个账户", allAccounts.Count);
        }

        // 2. 筛选可用账户（已启用 + 不在使用中 + 未限流或限流已过期）
        var availableAccounts = allAccounts
            .Where(x => x.IsEnabled &&
                        !AIProviderAsyncLocal.AIProviderIds.Contains(x.Id) &&
                        x.Provider == provider
                        &&
                        x.IsAvailable())
            .ToList();

        if (!availableAccounts.Any())
        {
            _logger.LogWarning("没有找到可用的 AI 账户 (模型: {Model})", model);
            return null;
        }

        // 3. 获取所有账户的配额信息
        var accountIds = availableAccounts.Select(a => a.Id).ToList();
        var quotaInfos = _quotaCache.GetAllQuotas(accountIds);

        _logger.LogDebug(
            "正在为模型 {Model} 选择账户，可用账户数: {Count}, 配额信息: {QuotaStats}",
            model,
            availableAccounts.Count,
            _quotaCache.GetQuotaStatistics(accountIds));

        // 4. 根据配额信息进行智能排序
        var rankedAccounts = availableAccounts
            .Select(account => new
            {
                Account = account,
                QuotaInfo = quotaInfos.GetValueOrDefault(account.Id),
                // 计算综合评分
                Score = CalculateAccountScore(account, quotaInfos.GetValueOrDefault(account.Id))
            })
            .OrderByDescending(x => x.Score) // 分数高的优先
            .ThenBy(x => x.Account.UsageCount) // 使用次数少的优先
            .ThenByDescending(x => x.Account.LastUsedAt ?? DateTime.MinValue) // 最近使用的最后考虑（避免单一账户过载）
            .ToList();

        // 5. 过滤掉配额耗尽的账户
        var bestAccountData = rankedAccounts
            .FirstOrDefault(x => x.QuotaInfo == null || !x.QuotaInfo.IsQuotaExhausted());

        if (bestAccountData == null)
        {
            _logger.LogWarning("所有账户配额已耗尽 (模型: {Model})", model);
            return null;
        }

        var bestAccountId = bestAccountData.Account.Id;

        // 6. 使用原子更新来更新使用统计（避免并发问题）
        var now = DateTime.UtcNow;
        await _appDbContext.AIAccounts
            .Where(x => x.Id == bestAccountId)
            .ExecuteUpdateAsync(setters => setters
                .SetProperty(a => a.LastUsedAt, now)
                .SetProperty(a => a.UsageCount, a => a.UsageCount + 1));

        _logger.LogInformation(
            "为模型 {Model} 选中账户 {AccountId} (名称: {Name}), 评分: {Score}, 配额状态: {Status}",
            model,
            bestAccountId,
            bestAccountData.Account.Name ?? "未命名",
            bestAccountData.Score,
            bestAccountData.QuotaInfo?.GetStatusDescription() ?? "无配额信息");

        // 返回选中的账户（需要重新查询以获取最新的统计信息）
        return bestAccountData.Account;
    }

    /// <summary>
    /// 智能获取指定提供商的最优账户
    /// 用于 Gemini API 等特定提供商的请求
    /// </summary>
    /// <param name="provider">AI 提供商（如 AIProviders.Gemini）</param>
    /// <returns>最优账户，如果没有可用账户则返回null</returns>
    public async Task<AIAccount?> GetAIAccountByProvider(params string[] provider)
    {
        // 1. 尝试从缓存获取账户列表，如果缓存不存在则从数据库查询
        var allAccounts = _quotaCache.GetAccountsCache();
        if (allAccounts == null)
        {
            _logger.LogDebug("账户列表缓存未命中，从数据库加载");
            allAccounts = await _appDbContext.AIAccounts.ToListAsync();
            _quotaCache.SetAccountsCache(allAccounts);
        }
        else
        {
            _logger.LogDebug("账户列表缓存命中，共 {Count} 个账户", allAccounts.Count);
        }

        // 2. 筛选指定提供商的可用账户（已启用 + 提供商匹配 + 不在使用中 + 未限流或限流已过期）
        var availableAccounts = allAccounts
            .Where(x => x.IsEnabled &&
                        provider.Contains(x.Provider) &&
                        !AIProviderAsyncLocal.AIProviderIds.Contains(x.Id) &&
                        x.IsAvailable())
            .ToList();

        if (!availableAccounts.Any())
        {
            _logger.LogWarning("没有找到可用的 {Provider} 账户", provider);
            return null;
        }

        // 3. 获取所有账户的配额信息
        var accountIds = availableAccounts.Select(a => a.Id).ToList();
        var quotaInfos = _quotaCache.GetAllQuotas(accountIds);

        _logger.LogDebug(
            "正在为提供商 {Provider} 选择账户，可用账户数: {Count}, 配额信息: {QuotaStats}",
            provider,
            availableAccounts.Count,
            _quotaCache.GetQuotaStatistics(accountIds));

        // 4. 根据配额信息进行智能排序
        var rankedAccounts = availableAccounts
            .Select(account => new
            {
                Account = account,
                QuotaInfo = quotaInfos.GetValueOrDefault(account.Id),
                // 计算综合评分
                Score = CalculateAccountScore(account, quotaInfos.GetValueOrDefault(account.Id))
            })
            .OrderByDescending(x => x.Score) // 分数高的优先
            .ThenBy(x => x.Account.UsageCount) // 使用次数少的优先
            .ThenByDescending(x => x.Account.LastUsedAt ?? DateTime.MinValue) // 最近使用的最后考虑（避免单一账户过载）
            .ToList();

        // 5. 过滤掉配额耗尽的账户
        var bestAccountData = rankedAccounts
            .FirstOrDefault(x => x.QuotaInfo == null || !x.QuotaInfo.IsQuotaExhausted());

        if (bestAccountData == null)
        {
            _logger.LogWarning("所有 {Provider} 账户配额已耗尽", provider);
            return null;
        }

        var bestAccountId = bestAccountData.Account.Id;

        // 6. 使用原子更新来更新使用统计（避免并发问题）
        var now = DateTime.UtcNow;
        await _appDbContext.AIAccounts
            .Where(x => x.Id == bestAccountId)
            .ExecuteUpdateAsync(setters => setters
                .SetProperty(a => a.LastUsedAt, now)
                .SetProperty(a => a.UsageCount, a => a.UsageCount + 1));

        _logger.LogInformation(
            "为提供商 {Provider} 选中账户 {AccountId} (名称: {Name}), 评分: {Score}, 配额状态: {Status}",
            provider,
            bestAccountId,
            bestAccountData.Account.Name ?? "未命名",
            bestAccountData.Score,
            bestAccountData.QuotaInfo?.GetStatusDescription() ?? "无配额信息");

        // 返回选中的账户
        return bestAccountData.Account;
    }

    /// <summary>
    /// 尝试获取指定ID的账户（用于会话粘性）
    /// 检查账户是否可用，如果可用则返回，否则返回null
    /// </summary>
    /// <param name="accountId">账户ID</param>
    /// <returns>可用的账户，如果不可用则返回null</returns>
    public async Task<AIAccount?> TryGetAccountById(int accountId)
    {
        // 1. 尝试从缓存获取账户列表
        var allAccounts = _quotaCache.GetAccountsCache();
        if (allAccounts == null)
        {
            _logger.LogDebug("账户列表缓存未命中，从数据库加载");
            allAccounts = await _appDbContext.AIAccounts.ToListAsync();
            _quotaCache.SetAccountsCache(allAccounts);
        }

        // 2. 查找指定ID的账户
        var account = allAccounts.FirstOrDefault(x => x.Id == accountId);
        if (account == null)
        {
            _logger.LogDebug("账户 {AccountId} 不存在", accountId);
            return null;
        }

        // 3. 检查账户是否可用
        var isAvailable = account.IsEnabled &&
                          !AIProviderAsyncLocal.AIProviderIds.Contains(account.Id) &&
                          account.IsAvailable();

        if (!isAvailable)
        {
            _logger.LogDebug(
                "账户 {AccountId} 不可用 (启用: {IsEnabled}, 使用中: {InUse}, 可用: {IsAvailable})",
                accountId,
                account.IsEnabled,
                AIProviderAsyncLocal.AIProviderIds.Contains(account.Id),
                account.IsAvailable());
            return null;
        }

        // 4. 检查配额是否耗尽
        var quotaInfo = _quotaCache.GetQuota(accountId);
        if (quotaInfo != null && !quotaInfo.IsExpired() && quotaInfo.IsQuotaExhausted())
        {
            _logger.LogDebug("账户 {AccountId} 配额已耗尽", accountId);
            return null;
        }

        // 5. 账户可用，更新使用统计
        var now = DateTime.UtcNow;
        await _appDbContext.AIAccounts
            .Where(x => x.Id == accountId)
            .ExecuteUpdateAsync(setters => setters
                .SetProperty(a => a.LastUsedAt, now)
                .SetProperty(a => a.UsageCount, a => a.UsageCount + 1));

        _logger.LogInformation(
            "会话粘性：成功获取账户 {AccountId} (名称: {Name})",
            accountId,
            account.Name ?? "未命名");

        return account;
    }

    /// <summary>
    /// 计算账户综合评分
    /// </summary>
    /// <param name="account">账户实体</param>
    /// <param name="quotaInfo">配额信息（可能为null）</param>
    /// <returns>0-100的综合评分</returns>
    private int CalculateAccountScore(AIAccount account, AccountQuotaInfo? quotaInfo)
    {
        var score = 0;

        // 如果有配额信息，使用健康度分数（权重80%）
        if (quotaInfo != null && !quotaInfo.IsExpired())
        {
            var healthScore = quotaInfo.GetHealthScore();
            score = (int)(healthScore * 0.8);

            // 如果配额耗尽，直接返回0分
            if (quotaInfo.IsQuotaExhausted())
            {
                return 0;
            }
        }
        else
        {
            // 没有配额信息时，给予中等分数（权重80%）
            // 这意味着未使用过的账户会获得中等优先级
            score = 40; // 50 * 0.8
        }

        // 考虑使用次数（使用次数越少，分数越高，权重10%）
        var usageScore = Math.Max(0, 100 - account.UsageCount / 10);
        score += (int)(usageScore * 0.1);

        // 考虑最后使用时间（越久未使用，分数越高，权重10%）
        if (account.LastUsedAt.HasValue)
        {
            var minutesSinceLastUse = (DateTime.UtcNow - account.LastUsedAt.Value).TotalMinutes;
            // 最多给予10分（100分钟以上未使用）
            var timeScore = Math.Min(100, (int)minutesSinceLastUse);
            score += (int)(timeScore * 0.1);
        }
        else
        {
            // 从未使用过的账户额外加分
            score += 10;
        }

        return Math.Max(0, Math.Min(100, score));
    }

    /// <summary>
    /// 标记账户为限流状态（使用原子更新）
    /// </summary>
    /// <param name="accountId">账户ID</param>
    /// <param name="resetAfterSeconds">配额重置剩余秒数</param>
    public async Task MarkAccountAsRateLimited(int accountId, int resetAfterSeconds)
    {
        var resetTime = DateTime.UtcNow.AddSeconds(resetAfterSeconds);
        var updateTime = DateTime.UtcNow;

        var affectedRows = await _appDbContext.AIAccounts
            .Where(x => x.Id == accountId)
            .ExecuteUpdateAsync(setters => setters
                .SetProperty(a => a.IsRateLimited, true)
                .SetProperty(a => a.RateLimitResetTime, resetTime)
                .SetProperty(a => a.UpdatedAt, updateTime));

        if (affectedRows == 0)
        {
            _logger.LogWarning("尝试标记不存在的账户 {AccountId} 为限流状态", accountId);
            return;
        }

        // 清除账户列表缓存（因为限流状态发生了变化）
        _quotaCache.ClearAccountsCache();

        _logger.LogWarning(
            "账户 {AccountId} 已标记为限流状态，将在 {ResetTime} 重置",
            accountId,
            resetTime);
    }

    /// <summary>
    /// 清理已过期的限流状态（使用原子更新）
    /// </summary>
    /// <param name="accountId">账户ID</param>
    public async Task ClearExpiredRateLimit(int accountId)
    {
        var now = DateTime.UtcNow;

        var affectedRows = await _appDbContext.AIAccounts
            .Where(x => x.Id == accountId
                        && x.IsRateLimited
                        && x.RateLimitResetTime.HasValue
                        && x.RateLimitResetTime.Value <= now)
            .ExecuteUpdateAsync(setters => setters
                .SetProperty(a => a.IsRateLimited, false)
                .SetProperty(a => a.RateLimitResetTime, (DateTime?)null)
                .SetProperty(a => a.UpdatedAt, now));

        if (affectedRows == 0)
        {
            return;
        }

        // 清除账户列表缓存（因为限流状态发生了变化）
        _quotaCache.ClearAccountsCache();

        _logger.LogInformation("账户 {AccountId} 限流已过期，已自动清除限流状态", accountId);
    }

    /// <summary>
    /// 禁用账户（用于401等认证失败的情况，使用原子更新）
    /// </summary>
    /// <param name="accountId">账户ID</param>
    public async Task DisableAccount(int accountId)
    {
        var updateTime = DateTime.UtcNow;

        var affectedRows = await _appDbContext.AIAccounts
            .Where(x => x.Id == accountId)
            .ExecuteUpdateAsync(setters => setters
                .SetProperty(a => a.IsEnabled, false)
                .SetProperty(a => a.UpdatedAt, updateTime));

        if (affectedRows == 0)
        {
            _logger.LogWarning("尝试禁用不存在的账户 {AccountId}", accountId);
            return;
        }

        // 清除账户列表缓存（因为账户状态发生了变化）
        _quotaCache.ClearAccountsCache();

        _logger.LogWarning("账户 {AccountId} 已被禁用", accountId);
    }

    /// <summary>
    /// 获取所有 AI 账户列表
    /// </summary>
    public async Task<List<AIAccountDto>> GetAllAccountsAsync()
    {
        var accounts = await _appDbContext.AIAccounts
            .Select(a => new AIAccountDto
            {
                Id = a.Id,
                Provider = a.Provider,
                Name = a.Name,
                Email = a.Email,
                BaseUrl = a.BaseUrl,
                IsEnabled = a.IsEnabled,
                IsRateLimited = a.IsRateLimited,
                RateLimitResetTime = a.RateLimitResetTime,
                CreatedAt = a.CreatedAt,
                UpdatedAt = a.UpdatedAt,
                LastUsedAt = a.LastUsedAt,
                UsageCount = a.UsageCount
            })
            .ToListAsync();

        return accounts;
    }

    /// <summary>
    /// 获取账户的配额状态（从缓存获取）
    /// </summary>
    /// <param name="accountId">账户ID</param>
    /// <returns>配额状态，如果缓存不存在则返回无数据的状态</returns>
    public AccountQuotaStatusDto GetAccountQuotaStatus(int accountId)
    {
        var quotaInfo = _quotaCache.GetQuota(accountId);

        if (quotaInfo == null || quotaInfo.IsExpired())
        {
            return new AccountQuotaStatusDto
            {
                AccountId = accountId,
                HasCacheData = false
            };
        }

        var dto = new AccountQuotaStatusDto
        {
            AccountId = accountId,
            HasCacheData = true,
            HealthScore = quotaInfo.GetHealthScore(),
            PrimaryUsedPercent = quotaInfo.PrimaryUsedPercent,
            SecondaryUsedPercent = quotaInfo.SecondaryUsedPercent,
            PrimaryResetAfterSeconds = quotaInfo.PrimaryResetAfterSeconds,
            SecondaryResetAfterSeconds = quotaInfo.SecondaryResetAfterSeconds,
            StatusDescription = quotaInfo.GetStatusDescription(),
            LastUpdatedAt = quotaInfo.LastUpdatedAt,
            // Anthropic 风格的限流信息
            TokensLimit = quotaInfo.TokensLimit,
            TokensRemaining = quotaInfo.TokensRemaining,
            InputTokensLimit = quotaInfo.InputTokensLimit,
            InputTokensRemaining = quotaInfo.InputTokensRemaining,
            OutputTokensLimit = quotaInfo.OutputTokensLimit,
            OutputTokensRemaining = quotaInfo.OutputTokensRemaining,
            // Anthropic Unified 限流信息
            AnthropicUnifiedStatus = quotaInfo.AnthropicUnifiedStatus,
            AnthropicUnifiedFiveHourStatus = quotaInfo.AnthropicUnifiedFiveHourStatus,
            AnthropicUnifiedFiveHourUtilization = quotaInfo.AnthropicUnifiedFiveHourUtilization,
            AnthropicUnifiedSevenDayStatus = quotaInfo.AnthropicUnifiedSevenDayStatus,
            AnthropicUnifiedSevenDayUtilization = quotaInfo.AnthropicUnifiedSevenDayUtilization,
            AnthropicUnifiedRepresentativeClaim = quotaInfo.AnthropicUnifiedRepresentativeClaim,
            AnthropicUnifiedFallbackPercentage = quotaInfo.AnthropicUnifiedFallbackPercentage,
            AnthropicUnifiedResetAt = quotaInfo.AnthropicUnifiedResetAt,
            AnthropicUnifiedOverageDisabledReason = quotaInfo.AnthropicUnifiedOverageDisabledReason,
            AnthropicUnifiedOverageStatus = quotaInfo.AnthropicUnifiedOverageStatus
        };

        // 计算 token 使用百分比
        if (quotaInfo.TokensLimit.HasValue && quotaInfo.TokensLimit.Value > 0)
        {
            dto.TokensUsedPercent = 100 - (int)((quotaInfo.TokensRemaining ?? 0) * 100.0 / quotaInfo.TokensLimit.Value);
        }

        return dto;
    }

    /// <summary>
    /// 批量获取账户的配额状态
    /// </summary>
    /// <param name="accountIds">账户ID列表</param>
    /// <returns>账户配额状态字典</returns>
    public Dictionary<int, AccountQuotaStatusDto> GetAccountQuotaStatuses(List<int> accountIds)
    {
        var result = new Dictionary<int, AccountQuotaStatusDto>();

        foreach (var accountId in accountIds)
        {
            result[accountId] = GetAccountQuotaStatus(accountId);
        }

        return result;
    }

    /// <summary>
    /// 删除 AI 账户
    /// </summary>
    /// <param name="id">账户 ID</param>
    /// <returns>是否删除成功</returns>
    public async Task<bool> DeleteAccountAsync(int id)
    {
        var account = await _appDbContext.AIAccounts.FindAsync(id);
        if (account == null)
        {
            return false;
        }

        // 清除配额缓存
        _quotaCache.ClearQuota(id);

        // 清除账户列表缓存（因为列表发生了变化）
        _quotaCache.ClearAccountsCache();

        _appDbContext.AIAccounts.Remove(account);
        await _appDbContext.SaveChangesAsync();

        _logger.LogInformation("账户 {AccountId} 已删除", id);

        return true;
    }

    /// <summary>
    /// 切换 AI 账户的启用/禁用状态
    /// </summary>
    /// <param name="id">账户 ID</param>
    /// <returns>更新后的账户信息，如果账户不存在则返回 null</returns>
    public async Task<AIAccountDto?> ToggleAccountStatusAsync(int id)
    {
        var account = await _appDbContext.AIAccounts.FindAsync(id);
        if (account == null)
        {
            return null;
        }

        account.IsEnabled = !account.IsEnabled;
        account.UpdatedAt = DateTime.UtcNow;

        // FindAsync 返回的实体已被跟踪，直接调用 SaveChangesAsync 即可
        await _appDbContext.SaveChangesAsync();

        // 清除账户列表缓存（因为账户状态发生了变化）
        _quotaCache.ClearAccountsCache();

        _logger.LogInformation(
            "账户 {AccountId} 状态已切换为 {Status}",
            id,
            account.IsEnabled ? "启用" : "禁用");

        return new AIAccountDto
        {
            Id = account.Id,
            Provider = account.Provider,
            Name = account.Name,
            Email = account.Email,
            BaseUrl = account.BaseUrl,
            IsEnabled = account.IsEnabled,
            IsRateLimited = account.IsRateLimited,
            RateLimitResetTime = account.RateLimitResetTime,
            CreatedAt = account.CreatedAt,
            UpdatedAt = account.UpdatedAt,
            LastUsedAt = account.LastUsedAt,
            UsageCount = account.UsageCount
        };
    }

    /// <summary>
    /// 刷新 OpenAI 账户的配额状态
    /// </summary>
    /// <param name="id">账户 ID</param>
    /// <returns>账户配额状态，如果账户不存在或刷新失败则返回 null</returns>
    public async Task<AccountQuotaStatusDto?> RefreshOpenAIQuotaStatusAsync(int id)
    {
        var account = await _appDbContext.AIAccounts.FindAsync(id);
        if (account == null)
        {
            _logger.LogWarning("尝试刷新不存在的账户 {AccountId} 的配额状态", id);
            return null;
        }

        if (account.Provider != "OpenAI")
        {
            _logger.LogWarning("账户 {AccountId} 不是 OpenAI 账户，无法刷新 OpenAI 配额", id);
            return null;
        }

        try
        {
            var oauth = account.GetOpenAiOauth();
            if (oauth == null)
            {
                _logger.LogWarning("账户 {AccountId} 没有 OpenAI OAuth 凭证，无法刷新 OpenAI 配额", account.Id);
                return null;
            }

            if (string.IsNullOrWhiteSpace(oauth.AccessToken))
            {
                _logger.LogWarning("账户 {AccountId} 的 OpenAI AccessToken 为空，无法刷新 OpenAI 配额", account.Id);
                return null;
            }

            var nowUnixSeconds = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
            var isTokenExpired = oauth.ExpiresAt > 0 && oauth.ExpiresAt <= nowUnixSeconds;
            if (isTokenExpired)
            {
                _logger.LogInformation("账户 {AccountId} OpenAI AccessToken 已过期，尝试刷新后再刷新配额", account.Id);
                await _openAiOAuthService.RefreshOpenAiOAuthTokenAsync(account);

                oauth = account.GetOpenAiOauth();
                if (oauth == null || string.IsNullOrWhiteSpace(oauth.AccessToken))
                {
                    _logger.LogWarning("账户 {AccountId} OpenAI Token 刷新后仍无效，无法刷新 OpenAI 配额", account.Id);
                    return null;
                }
            }

            var accessToken = oauth.AccessToken;
            var address = string.IsNullOrEmpty(account.BaseUrl)
                ? "https://chatgpt.com/backend-api/codex"
                : account.BaseUrl;

            // 构造一个简单的请求来获取配额信息
            var headers = new Dictionary<string, string>
            {
                { "Authorization", "Bearer " + accessToken },
                { "User-Agent", "codex_cli_rs/0.76.0 (Windows 10.0.26200; x86_64) vscode/1.105.1" },
                { "openai-beta", "responses=experimental" },
                { "originator", "codex_cli_rs" }
            };

            using var httpClient = new HttpClient();
            httpClient.DefaultRequestHeaders.Clear();

            // 发送一个简单的 HEAD 请求来获取配额头信息
            var response = await httpClient.HttpRequestRaw($"{address.TrimEnd('/')}/responses", new
            {
                model = "gpt-5.2",
                instructions = AIPrompt.CodeXPrompt,
                store = false,
                stream = true,
                input = new object[]
                {
                    new
                    {
                        role = "user",
                        type = "message",
                        content = new[]
                        {
                            new
                            {
                                type = "input_text",
                                text = "Say 1"
                            }
                        }
                    }
                }
            }, headers);

            if (!response.IsSuccessStatusCode && response.StatusCode != HttpStatusCode.TooManyRequests)
            {
                throw new Exception(await response.Content.ReadAsStringAsync());
            }

            // 提取配额信息
            var quotaInfo = AccountQuotaCacheService.ExtractFromHeaders(account.Id, response.Headers);
            if (quotaInfo != null)
            {
                _quotaCache.UpdateQuota(quotaInfo);

                _logger.LogInformation(
                    "成功刷新账户 {AccountId} 的 OpenAI 配额状态",
                    account.Id);

                return new AccountQuotaStatusDto
                {
                    AccountId = account.Id,
                    HasCacheData = true,
                    HealthScore = quotaInfo.GetHealthScore(),
                    PrimaryUsedPercent = quotaInfo.PrimaryUsedPercent,
                    SecondaryUsedPercent = quotaInfo.SecondaryUsedPercent,
                    PrimaryResetAfterSeconds = quotaInfo.PrimaryResetAfterSeconds,
                    SecondaryResetAfterSeconds = quotaInfo.SecondaryResetAfterSeconds,
                    StatusDescription = quotaInfo.GetStatusDescription(),
                    LastUpdatedAt = quotaInfo.LastUpdatedAt
                };
            }

            _logger.LogWarning("无法从响应头提取账户 {AccountId} 的配额信息", account.Id);
            return new AccountQuotaStatusDto
            {
                AccountId = account.Id,
                HasCacheData = false
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "刷新账户 {AccountId} 的 OpenAI 配额状态失败", id);
            return null;
        }
    }

    /// <summary>
    /// 刷新 Gemini Antigravity 账户的配额状态
    /// </summary>
    /// <param name="id">账户 ID</param>
    /// <returns>账户配额状态，如果账户不存在或刷新失败则返回 null</returns>
    public async Task<AccountQuotaStatusDto?> RefreshAntigravityQuotaStatusAsync(int id)
    {
        var account = await _appDbContext.AIAccounts.FindAsync(id);
        if (account == null)
        {
            _logger.LogWarning("尝试刷新不存在的账户 {AccountId} 的配额状态", id);
            return null;
        }

        if (account.Provider != AIProviders.GeminiAntigravity)
        {
            _logger.LogWarning("账户 {AccountId} 不是 Gemini Antigravity 账户，无法刷新配额", id);
            return null;
        }

        try
        {
            var geminiOauth = account.GetGeminiOauth();
            if (geminiOauth == null)
            {
                _logger.LogWarning("账户 {AccountId} 没有 Gemini OAuth 凭证", id);
                return null;
            }

            var accessToken = geminiOauth.Token;
            if (string.IsNullOrEmpty(accessToken))
            {
                _logger.LogWarning("账户 {AccountId} 的 Access Token 为空", id);
                return null;
            }

            // 调用 Antigravity API 获取配额信息
            var apiUrl =
                $"{Services.GeminiOAuth.GeminiAntigravityOAuthConfig.AntigravityApiUrl}/v1internal:fetchAvailableModels";

            using var httpClient = new HttpClient();
            httpClient.DefaultRequestHeaders.Clear();
            httpClient.DefaultRequestHeaders.Add("User-Agent",
                Services.GeminiOAuth.GeminiAntigravityOAuthConfig.UserAgent);
            httpClient.DefaultRequestHeaders.Add("Authorization", $"Bearer {accessToken}");
            httpClient.Timeout = TimeSpan.FromSeconds(30);

            var requestContent = new StringContent("{}", System.Text.Encoding.UTF8, "application/json");
            var response = await httpClient.PostAsync(apiUrl, requestContent);

            if (!response.IsSuccessStatusCode)
            {
                var errorBody = await response.Content.ReadAsStringAsync();
                _logger.LogError("Antigravity API 返回错误 {StatusCode}: {Error}",
                    (int)response.StatusCode, errorBody);
                return null;
            }

            var responseBody = await response.Content.ReadAsStringAsync();
            using var jsonDoc = System.Text.Json.JsonDocument.Parse(responseBody);
            var root = jsonDoc.RootElement;

            // 解析 models 字段获取配额信息
            if (!root.TryGetProperty("models", out var modelsElement) ||
                modelsElement.ValueKind != System.Text.Json.JsonValueKind.Object)
            {
                _logger.LogWarning("Antigravity API 响应中没有 models 字段");
                return new AccountQuotaStatusDto
                {
                    AccountId = account.Id,
                    HasCacheData = false
                };
            }

            int? TryGetResetAfterSeconds(string? resetTimeRaw)
            {
                if (string.IsNullOrWhiteSpace(resetTimeRaw))
                {
                    return null;
                }

                if (DateTimeOffset.TryParse(resetTimeRaw, out var resetTime))
                {
                    var delta = resetTime.ToUniversalTime() - DateTimeOffset.UtcNow;
                    return Math.Max(0, (int)delta.TotalSeconds);
                }

                try
                {
                    var resetTimeFallback = DateTime.Parse(resetTimeRaw.Replace("Z", "+00:00"));
                    var delta = resetTimeFallback.ToUniversalTime() - DateTime.UtcNow;
                    return Math.Max(0, (int)delta.TotalSeconds);
                }
                catch
                {
                    return null;
                }
            }

            // Antigravity 按模型提供 quotaInfo，这里返回所有模型的配额信息（用于前端展示）
            var antigravityModelQuotas = new List<AntigravityModelQuotaDto>();
            foreach (var modelProperty in modelsElement.EnumerateObject())
            {
                var quotaDto = new AntigravityModelQuotaDto
                {
                    Model = modelProperty.Name
                };

                if (modelProperty.Value.TryGetProperty("quotaInfo", out var quotaInfo)
                    && quotaInfo.ValueKind == System.Text.Json.JsonValueKind.Object)
                {
                    quotaDto.HasQuotaInfo = true;

                    if (quotaInfo.TryGetProperty("remainingFraction", out var remaining)
                        && remaining.ValueKind == System.Text.Json.JsonValueKind.Number)
                    {
                        var remainingFractionValue = remaining.GetDouble();
                        remainingFractionValue = Math.Max(0, Math.Min(1, remainingFractionValue));

                        quotaDto.RemainingFraction = remainingFractionValue;
                        quotaDto.RemainingPercent = (int)Math.Round(remainingFractionValue * 100);
                        quotaDto.UsedPercent = (int)Math.Round((1 - remainingFractionValue) * 100);
                    }

                    if (quotaInfo.TryGetProperty("resetTime", out var resetTime)
                        && resetTime.ValueKind == System.Text.Json.JsonValueKind.String)
                    {
                        quotaDto.ResetTime = resetTime.GetString();
                        quotaDto.ResetAfterSeconds = TryGetResetAfterSeconds(quotaDto.ResetTime);
                    }
                }

                antigravityModelQuotas.Add(quotaDto);
            }

            // 查找第一个包含 quotaInfo 的模型（优先选择 gemini 开头的模型）
            string? selectedModel = null;
            double remainingFraction = 0;
            string? resetTimeRaw = null;

            // 第一遍：优先查找 gemini / claude 开头的模型
            var preferredPrefixes = new[] { "gemini", "claude" };
            foreach (var prefix in preferredPrefixes)
            {
                foreach (var modelProperty in modelsElement.EnumerateObject())
                {
                    if (modelProperty.Name.StartsWith(prefix, StringComparison.OrdinalIgnoreCase) &&
                        modelProperty.Value.TryGetProperty("quotaInfo", out var quotaInfo))
                    {
                        selectedModel = modelProperty.Name;
                        if (quotaInfo.TryGetProperty("remainingFraction", out var remaining))
                        {
                            remainingFraction = remaining.GetDouble();
                        }

                        if (quotaInfo.TryGetProperty("resetTime", out var resetTime))
                        {
                            resetTimeRaw = resetTime.GetString();
                        }

                        break;
                    }
                }

                if (selectedModel != null)
                {
                    break;
                }
            }

            // 第二遍：如果没找到 gemini/claude 模型，使用第一个有 quotaInfo 的模型
            if (selectedModel == null)
            {
                foreach (var modelProperty in modelsElement.EnumerateObject())
                {
                    if (modelProperty.Value.TryGetProperty("quotaInfo", out var quotaInfo))
                    {
                        selectedModel = modelProperty.Name;
                        if (quotaInfo.TryGetProperty("remainingFraction", out var remaining))
                        {
                            remainingFraction = remaining.GetDouble();
                        }

                        if (quotaInfo.TryGetProperty("resetTime", out var resetTime))
                        {
                            resetTimeRaw = resetTime.GetString();
                        }

                        break;
                    }
                }
            }

            if (selectedModel == null)
            {
                _logger.LogWarning("Antigravity API 响应中没有找到包含配额信息的模型");
                return new AccountQuotaStatusDto
                {
                    AccountId = account.Id,
                    HasCacheData = false,
                    AntigravityModelQuotas = antigravityModelQuotas
                };
            }

            // 计算配额百分比
            remainingFraction = Math.Max(0, Math.Min(1, remainingFraction));
            var remainingPercent = (int)Math.Round(remainingFraction * 100);
            var usedPercent = (int)Math.Round((1 - remainingFraction) * 100);
            var healthScore = Math.Max(0, Math.Min(100, 100 - usedPercent));

            // 计算重置时间（秒）
            int? resetAfterSeconds = null;
            if (!string.IsNullOrEmpty(resetTimeRaw))
            {
                try
                {
                    var resetTime = DateTime.Parse(resetTimeRaw.Replace("Z", "+00:00"));
                    var delta = resetTime.ToUniversalTime() - DateTime.UtcNow;
                    resetAfterSeconds = Math.Max(0, (int)delta.TotalSeconds);
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "解析重置时间失败: {ResetTime}", resetTimeRaw);
                }
            }

            // 构建状态描述
            var statusDescription = $"{selectedModel} 剩余 {remainingPercent}%";
            if (resetAfterSeconds.HasValue && resetAfterSeconds > 0)
            {
                var hours = resetAfterSeconds.Value / 3600;
                var minutes = (resetAfterSeconds.Value % 3600) / 60;
                if (hours > 0)
                {
                    statusDescription += $" ({hours}小时{minutes}分钟后重置)";
                }
                else
                {
                    statusDescription += $" ({minutes}分钟后重置)";
                }
            }

            _logger.LogInformation(
                "成功刷新账户 {AccountId} 的 Antigravity 配额状态: {Status}",
                account.Id, statusDescription);

            // 注意：Antigravity 的配额系统与 OpenAI 不同
            // 这里我们将 remaining 映射到 Primary，并设置合理的重置时间
            return new AccountQuotaStatusDto
            {
                AccountId = account.Id,
                HasCacheData = true,
                HealthScore = healthScore,
                PrimaryUsedPercent = usedPercent,
                SecondaryUsedPercent = usedPercent,
                PrimaryResetAfterSeconds = resetAfterSeconds ?? 0,
                SecondaryResetAfterSeconds = resetAfterSeconds ?? 0,
                StatusDescription = statusDescription,
                LastUpdatedAt = DateTime.UtcNow,
                AntigravityModelQuotas = antigravityModelQuotas
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "刷新账户 {AccountId} 的 Antigravity 配额状态失败", id);
            return null;
        }
    }

    /// <summary>
    /// 刷新 Claude 账户的配额状态（通过请求上游并解析响应头）
    /// </summary>
    public async Task<AccountQuotaStatusDto?> RefreshClaudeQuotaStatusAsync(int id)
    {
        var account = await _appDbContext.AIAccounts.FindAsync(id);
        if (account == null)
        {
            _logger.LogWarning("尝试刷新不存在的账户 {AccountId} 的配额状态", id);
            return null;
        }

        if (account.Provider != AIProviders.Claude)
        {
            _logger.LogWarning("账户 {AccountId} 不是 Claude 账户，无法刷新配额", id);
            return null;
        }

        try
        {
            var hasExplicitApiKey = !string.IsNullOrWhiteSpace(account.ApiKey);
            var upstreamUrl = BuildClaudeMessagesUrl(account.BaseUrl);
            var upstreamUri = new Uri(upstreamUrl);
            var isAnthropicBase =
                string.Equals(upstreamUri.Scheme, "https", StringComparison.OrdinalIgnoreCase)
                && string.Equals(upstreamUri.Host, "api.anthropic.com", StringComparison.OrdinalIgnoreCase);

            var claudeOauth = account.GetClaudeOauth();
            if (!hasExplicitApiKey)
            {
                if (claudeOauth == null || string.IsNullOrWhiteSpace(claudeOauth.AccessToken))
                {
                    _logger.LogWarning("账户 {AccountId} 没有有效的 Claude Oauth 凭证，无法刷新配额", id);
                    return null;
                }

                if (IsClaudeTokenExpired(claudeOauth))
                {
                    _logger.LogInformation("账户 {AccountId} Claude Token 已过期，尝试刷新后再刷新配额", id);
                    await _claudeCodeOAuthService.RefreshClaudeOAuthTokenAsync(account);

                    // RefreshClaudeOAuthTokenAsync 会更新数据库，重新读取以拿到最新 token
                    account = await _appDbContext.AIAccounts.FindAsync(id) ?? account;
                    claudeOauth = account.GetClaudeOauth();
                    if (claudeOauth == null || string.IsNullOrWhiteSpace(claudeOauth.AccessToken))
                    {
                        _logger.LogWarning("账户 {AccountId} Claude Token 刷新后仍无效，无法刷新配额", id);
                        return null;
                    }
                }
            }

            var authToken = hasExplicitApiKey
                ? account.ApiKey!.Trim()
                : claudeOauth!.AccessToken;

            var useApiKeyHeader = isAnthropicBase && hasExplicitApiKey;

            using var httpClient = new HttpClient
            {
                Timeout = TimeSpan.FromSeconds(30)
            };

            using var request = new HttpRequestMessage(HttpMethod.Post, upstreamUrl);

            request.Headers.TryAddWithoutValidation("User-Agent", "claude-cli/2.0.5 (external, cli)");
            request.Headers.TryAddWithoutValidation("Accept", "application/json");
            request.Headers.TryAddWithoutValidation("Accept-Encoding", "gzip, deflate, br, zstd");
            request.Headers.TryAddWithoutValidation("Accept-Language", "*");
            request.Headers.TryAddWithoutValidation("anthropic-dangerous-direct-browser-access", "true");
            request.Headers.TryAddWithoutValidation("anthropic-version", "2023-06-01");

            request.Headers.TryAddWithoutValidation(
                "anthropic-beta",
                useApiKeyHeader
                    ? "claude-code-20250219,interleaved-thinking-2025-05-14,fine-grained-tool-streaming-2025-05-14"
                    : "oauth-2025-04-20,claude-code-20250219,interleaved-thinking-2025-05-14,fine-grained-tool-streaming-2025-05-14");

            // Follow upstream conventions:
            // - Official Anthropic API uses x-api-key
            // - Reverse/proxy channels typically accept Authorization: Bearer
            if (useApiKeyHeader)
            {
                request.Headers.TryAddWithoutValidation("x-api-key", authToken);
            }
            else
            {
                request.Headers.TryAddWithoutValidation("Authorization", "Bearer " + authToken);
            }

            request.Content = JsonContent.Create(new
            {
                model = "claude-haiku-4-5-20251001",
                max_tokens = 1,
                messages = new[]
                {
                    new
                    {
                        role = "user",
                        content = "ping"
                    }
                }
            });

            using var response = await httpClient.SendAsync(request);

            if (response.StatusCode is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden)
            {
                _logger.LogWarning("Claude 配额刷新鉴权失败（HTTP {StatusCode}），将禁用账户 {AccountId}",
                    (int)response.StatusCode,
                    id);
                await DisableAccount(id);
                return null;
            }

            if (!response.IsSuccessStatusCode && response.StatusCode != HttpStatusCode.TooManyRequests)
            {
                var errorBody = await response.Content.ReadAsStringAsync();
                _logger.LogError("Claude 配额刷新失败（HTTP {StatusCode}）: {Error}",
                    (int)response.StatusCode,
                    errorBody);
                return null;
            }

            var quotaInfo = AccountQuotaCacheService.ExtractFromAnthropicHeaders(account.Id, response.Headers);
            if (quotaInfo != null)
            {
                _quotaCache.UpdateQuota(quotaInfo);
            }

            return GetAccountQuotaStatus(account.Id);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "刷新账户 {AccountId} 的 Claude 配额状态失败", id);
            return null;
        }
    }

    /// <summary>
    /// 刷新 Factory 账户的配额状态（通过请求上游并解析响应头）
    /// </summary>
    public async Task<AccountQuotaStatusDto?> RefreshFactoryQuotaStatusAsync(int id)
    {
        var account = await _appDbContext.AIAccounts.FindAsync(id);
        if (account == null)
        {
            _logger.LogWarning("尝试刷新不存在的账户 {AccountId} 的配额状态", id);
            return null;
        }

        if (account.Provider != AIProviders.Factory)
        {
            _logger.LogWarning("账户 {AccountId} 不是 Factory 账户，无法刷新配额", id);
            return null;
        }

        try
        {
            var factoryOauth = account.GetFactoryOauth();
            if (factoryOauth == null || string.IsNullOrWhiteSpace(factoryOauth.AccessToken))
            {
                _logger.LogWarning("账户 {AccountId} 没有有效的 Factory Oauth 凭证，无法刷新配额", id);
                return null;
            }

            if (IsOAuthTokenExpired(factoryOauth.ExpiresAt) && !string.IsNullOrWhiteSpace(factoryOauth.RefreshToken))
            {
                _logger.LogInformation("账户 {AccountId} Factory Token 已过期，尝试刷新后再刷新配额", id);

                var refreshed = await _factoryOAuthService.RefreshTokenAsync(
                    factoryOauth.RefreshToken,
                    factoryOauth.OrganizationId);

                account.SetFactoryOAuth(refreshed);
                _appDbContext.AIAccounts.Update(account);
                await _appDbContext.SaveChangesAsync();

                _quotaCache.ClearAccountsCache();

                factoryOauth = refreshed;
            }

            using var httpClient = new HttpClient
            {
                Timeout = TimeSpan.FromSeconds(30)
            };

            using var request =
                new HttpRequestMessage(HttpMethod.Post, "https://app.factory.ai/api/llm/a/v1/messages");

            request.Headers.TryAddWithoutValidation("Authorization", "Bearer " + factoryOauth.AccessToken);
            request.Headers.TryAddWithoutValidation("User-Agent", "factory-cli/0.41.0");
            request.Headers.TryAddWithoutValidation("Accept", "text/event-stream");
            request.Headers.TryAddWithoutValidation("Accept-Encoding", "gzip, deflate, br");
            request.Headers.TryAddWithoutValidation("Accept-Language", "*");
            request.Headers.TryAddWithoutValidation("x-factory-client", "cli");
            request.Headers.TryAddWithoutValidation("x-session-id", Guid.NewGuid().ToString());
            request.Headers.TryAddWithoutValidation("x-assistant-message-id", Guid.NewGuid().ToString());
            request.Headers.TryAddWithoutValidation("x-stainless-arch", "x64");
            request.Headers.TryAddWithoutValidation("x-stainless-helper-method", "stream");
            request.Headers.TryAddWithoutValidation("x-stainless-lang", "js");
            request.Headers.TryAddWithoutValidation("x-stainless-os", "Windows");
            request.Headers.TryAddWithoutValidation("x-stainless-package-version", "0.70.0");
            request.Headers.TryAddWithoutValidation("x-stainless-retry-count", "0");
            request.Headers.TryAddWithoutValidation("x-stainless-runtime", "node");
            request.Headers.TryAddWithoutValidation("x-stainless-runtime-version", "v24.3.0");
            request.Headers.TryAddWithoutValidation("anthropic-dangerous-direct-browser-access", "true");
            request.Headers.TryAddWithoutValidation(
                "anthropic-beta",
                "oauth-2025-04-20,claude-code-20250219,interleaved-thinking-2025-05-14,fine-grained-tool-streaming-2025-05-14");

            request.Headers.Referrer = new Uri("https://app.factory.ai/");

            request.Content = JsonContent.Create(new
            {
                model = "claude-haiku-4-5-20251001",
                max_tokens = 1,
                stream = true,
                system = new object[]
                {
                    new
                    {
                        type = "text",
                        text = "You are Droid, an AI software engineering agent built by Factory."
                    },
                    new
                    {
                        type = "text",
                        text =
                            "You work within an interactive cli tool and you are focused on helping users with any software engineering tasks.nGuidelines:n- Use tools when necessary.n- Don't stop until all user tasks are completed.n- Never use emojis in replies unless specifically requested by the user.n- Only add absolutely necessary comments to the code you generate.n- Your replies should be concise and you should preserve users tokens.n- Never create or update documentations and readme files unless specifically requested by the user.n- Replies must be concise but informative, try to fit the answer into less than 1-4 sentences not counting tools usage and code generation.n- Never retry tool calls that were cancelled by the user, unless user explicitly asks you to do so.n- Use FetchUrl to fetch Factory docs (https://docs.factory.ai/factory-docs-map.md) when:n  - Asks questions in the second person (eg. \"are you able...\", \"can you do...\")n  - User asks about Droid capabilities or featuresn  - User needs help with Droid commands, configuration, or settingsn  - User asks about skills, MCP, hooks, custom droids, BYOK, or other Factory specific featuresnFocus on the task at hand, don't try to jump to related but not requested tasks.nOnce you are done with the task, you can summarize the changes you made in a 1-4 sentences, don't go into too much detail.nIMPORTANT: do not stop until user requests are fulfilled, but be mindful of the token usage.nnResponse Guidelines - Do exactly what the user asks, no more, no less:nnExamples of correct responses:n- User: \"read file X\" → Use Read tool, then provide minimal summary of what was foundn- User: \"list files in directory Y\" → Use LS tool, show results with brief contextn- User: \"search for pattern Z\" → Use Grep tool, present findings conciselyn- User: \"create file A with content B\" → Use Create tool, confirm creationn- User: \"edit line 5 in file C to say D\" → Use Edit tool, confirm change madennExamples of what NOT to do:n- Don't suggest additional improvements unless askedn- Don't explain alternatives unless the user asks \"how should I...\"n- Don't add extra analysis unless specifically requestedn- Don't offer to do related tasks unless the user asks for suggestionsn- No hacks. No unreasonable shortcuts.n- Do not give up if you encounter unexpected problems. Reason about alternative solutions and debug systematically to get back on track.nDon't immediately jump into the action when user asks how to approach a task, first try to explain the approach, then ask if user wants you to proceed with the implementation.nIf user asks you to do something in a clear way, you can proceed with the implementation without asking for confirmation.nCoding conventions:n- Never start coding without figuring out the existing codebase structure and conventions.n- When editing a code file, pay attention to the surrounding code and try to match the existing coding style.n- Follow approaches and use already used libraries and patterns. Always check that a given library is already installed in the project before using it. Even most popular libraries can be missing in the project.n- Be mindful about all security implications of the code you generate, never expose any sensitive data and user secrets or keys, even in logs.n- Before ANY git commit or push operation:n    - Run 'git diff --cached' to review ALL changes being committedn    - Run 'git status' to confirm all files being includedn    - Examine the diff for secrets, credentials, API keys, or sensitive data (especially in config files, logs, environment files, and build outputs) n    - if detected, STOP and warn the usernTesting and verification:nBefore completing the task, always verify that the code you generated works as expected. Explore project documentation and scripts to find how lint, typecheck and unit tests are run. Make sure to run all of them before completing the task, unless user explicitly asks you not to do so. Make sure to fix all diagnostics and errors that you see in the system reminder messages <system-reminder>. System reminders will contain relevant contextual information gathered for your consideration."
                    }
                },
                messages = new[]
                {
                    new
                    {
                        role = "user",
                        content = new object[]
                        {
                            new
                            {
                                type = "text",
                                text = "ping"
                            }
                        }
                    }
                }
            });

            using var response = await httpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead);

            if (response.StatusCode is HttpStatusCode.Unauthorized)
            {
                var errorBody = await response.Content.ReadAsStringAsync();
                _logger.LogWarning("Factory 配额刷新鉴权失败（HTTP 401）：{Error}，将禁用账户 {AccountId}",
                    errorBody,
                    id);
                await DisableAccount(id);
                return null;
            }

            if (response.StatusCode is HttpStatusCode.Forbidden)
            {
                var errorBody = await response.Content.ReadAsStringAsync();
                _logger.LogWarning("Factory 配额刷新被拒绝（HTTP 403）：{Error}，账户 {AccountId}",
                    errorBody,
                    id);
                return null;
            }

            if (!response.IsSuccessStatusCode && response.StatusCode != HttpStatusCode.TooManyRequests)
            {
                var errorBody = await response.Content.ReadAsStringAsync();
                _logger.LogError("Factory 配额刷新失败（HTTP {StatusCode}）: {Error}",
                    (int)response.StatusCode,
                    errorBody);
                return null;
            }

            var quotaInfo = AccountQuotaCacheService.ExtractFromAnthropicHeaders(account.Id, response.Headers);
            if (quotaInfo != null)
            {
                _quotaCache.UpdateQuota(quotaInfo);
            }

            return GetAccountQuotaStatus(account.Id);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "刷新账户 {AccountId} 的 Factory 配额状态失败", id);
            return null;
        }
    }

    /// <summary>
    /// 刷新 Kiro 账户的配额状态
    /// </summary>
    /// <param name="id">账户 ID</param>
    /// <returns>账户配额状态，如果账户不存在或刷新失败则返回 null</returns>
    public async Task<AccountQuotaStatusDto?> RefreshKiroQuotaStatusAsync(int id)
    {
        var account = await _appDbContext.AIAccounts.FindAsync(id);
        if (account == null)
        {
            _logger.LogWarning("尝试刷新不存在的账户 {AccountId} 的配额状态", id);
            return null;
        }

        if (account.Provider != AIProviders.Kiro)
        {
            _logger.LogWarning("账户 {AccountId} 不是 Kiro 账户，无法刷新配额", id);
            return null;
        }

        try
        {
            var usageLimits = await _kiroService.GetUsageLimitsAsync(account);
            if (usageLimits == null || usageLimits.UsageBreakdownList == null)
            {
                _logger.LogWarning("无法获取账户 {AccountId} 的 Kiro 使用限制", id);
                return new AccountQuotaStatusDto
                {
                    AccountId = account.Id,
                    HasCacheData = false
                };
            }

            // 查找 Credits 使用情况（通常用于显示配额）
            var creditsItem = usageLimits.UsageBreakdownList
                .FirstOrDefault(x =>
                    string.Equals(x.DisplayName, "Credits", StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(x.DisplayName, "Credit", StringComparison.OrdinalIgnoreCase));

            if (creditsItem == null)
            {
                // 如果没有 Credits，使用第一个项目
                creditsItem = usageLimits.UsageBreakdownList.FirstOrDefault();
            }

            if (creditsItem == null)
            {
                _logger.LogWarning("账户 {AccountId} 的 Kiro 使用限制为空", id);
                return new AccountQuotaStatusDto
                {
                    AccountId = account.Id,
                    HasCacheData = false
                };
            }

            // 计算使用百分比（使用带精度的字段）
            var usedPercent = 0;
            var remainingPercent = 100;
            var healthScore = 100;

            // 优先使用带精度的字段，如果不可用则使用普通字段
            var currentUsage = creditsItem.CurrentUsageWithPrecision > 0 ? creditsItem.CurrentUsageWithPrecision : creditsItem.CurrentUsage;
            var usageLimit = creditsItem.UsageLimitWithPrecision > 0 ? creditsItem.UsageLimitWithPrecision : creditsItem.UsageLimit;

            if (usageLimit > 0)
            {
                usedPercent = (int)Math.Round((currentUsage / usageLimit) * 100);
                remainingPercent = 100 - usedPercent;
                healthScore = Math.Max(0, 100 - usedPercent);
            }

            // 计算重置时间（NextDateReset 是 Unix 时间戳，单位为秒，格式为科学计数法）
            int? resetAfterSeconds = null;
            if (creditsItem.NextDateReset > 0)
            {
                try
                {
                    // Kiro API 返回的是秒级 Unix 时间戳（科学计数法格式，如 1.769904E9 = 1769904000）
                    var resetTimeSeconds = (long)creditsItem.NextDateReset;
                    var resetTime = DateTimeOffset.FromUnixTimeSeconds(resetTimeSeconds);
                    var delta = resetTime - DateTimeOffset.UtcNow;
                    resetAfterSeconds = Math.Max(0, (int)delta.TotalSeconds);
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "解析 Kiro 重置时间失败: {ResetTime}", creditsItem.NextDateReset);
                }
            }

            // 构建状态描述
            var statusDescription = $"{creditsItem.DisplayName ?? "Credits"} 使用: {usedPercent}%";
            if (resetAfterSeconds.HasValue && resetAfterSeconds > 0)
            {
                var hours = resetAfterSeconds.Value / 3600;
                var minutes = (resetAfterSeconds.Value % 3600) / 60;
                if (hours > 0)
                {
                    statusDescription += $" ({hours}小时{minutes}分钟后重置)";
                }
                else if (minutes > 0)
                {
                    statusDescription += $" ({minutes}分钟后重置)";
                }
            }

            _logger.LogInformation(
                "成功刷新账户 {AccountId} 的 Kiro 配额状态: {Status}",
                account.Id, statusDescription);

            // 构建 Kiro 使用明细列表
            var kiroUsageBreakdownList = usageLimits.UsageBreakdownList?.Select(item =>
            {
                var itemUsedPercent = 0;
                var itemRemaining = 0.0;
                var itemRemainingPercent = 100;
                int? itemResetAfterSeconds = null;

                // 使用带精度的字段进行计算
                var itemCurrentUsage = item.CurrentUsageWithPrecision > 0 ? item.CurrentUsageWithPrecision : item.CurrentUsage;
                var itemUsageLimit = item.UsageLimitWithPrecision > 0 ? item.UsageLimitWithPrecision : item.UsageLimit;

                if (itemUsageLimit > 0)
                {
                    itemUsedPercent = (int)Math.Round((itemCurrentUsage / itemUsageLimit) * 100);
                    itemRemaining = itemUsageLimit - itemCurrentUsage;
                    itemRemainingPercent = 100 - itemUsedPercent;
                }

                if (item.NextDateReset > 0)
                {
                    try
                    {
                        // Kiro API 返回的是秒级 Unix 时间戳（科学计数法格式）
                        var resetTimeSeconds = (long)item.NextDateReset;
                        var resetTime = DateTimeOffset.FromUnixTimeSeconds(resetTimeSeconds);
                        var delta = resetTime - DateTimeOffset.UtcNow;
                        itemResetAfterSeconds = Math.Max(0, (int)delta.TotalSeconds);
                    }
                    catch
                    {
                        // Ignore parse errors
                    }
                }

                // 处理免费试用信息
                KiroFreeTrialInfoDto? freeTrialInfoDto = null;
                if (item.FreeTrialInfo != null)
                {
                    var ftCurrentUsage = item.FreeTrialInfo.CurrentUsageWithPrecision > 0
                        ? item.FreeTrialInfo.CurrentUsageWithPrecision
                        : item.FreeTrialInfo.CurrentUsage;
                    var ftUsageLimit = item.FreeTrialInfo.UsageLimitWithPrecision > 0
                        ? item.FreeTrialInfo.UsageLimitWithPrecision
                        : item.FreeTrialInfo.UsageLimit;

                    var ftUsedPercent = 0;
                    var ftRemaining = 0.0;
                    var ftRemainingPercent = 100;
                    int? ftExpiryAfterSeconds = null;

                    if (ftUsageLimit > 0)
                    {
                        ftUsedPercent = (int)Math.Round((ftCurrentUsage / ftUsageLimit) * 100);
                        ftRemaining = ftUsageLimit - ftCurrentUsage;
                        ftRemainingPercent = 100 - ftUsedPercent;
                    }

                    if (item.FreeTrialInfo.FreeTrialExpiry > 0)
                    {
                        try
                        {
                            // freeTrialExpiry 是毫秒级 Unix 时间戳
                            var expiryTimeMs = (long)item.FreeTrialInfo.FreeTrialExpiry;
                            var expiryTime = DateTimeOffset.FromUnixTimeMilliseconds(expiryTimeMs);
                            var delta = expiryTime - DateTimeOffset.UtcNow;
                            ftExpiryAfterSeconds = Math.Max(0, (int)delta.TotalSeconds);
                        }
                        catch
                        {
                            // Ignore parse errors
                        }
                    }

                    freeTrialInfoDto = new KiroFreeTrialInfoDto
                    {
                        FreeTrialStatus = item.FreeTrialInfo.FreeTrialStatus,
                        CurrentUsage = ftCurrentUsage,
                        UsageLimit = ftUsageLimit,
                        UsedPercent = ftUsedPercent,
                        Remaining = ftRemaining,
                        RemainingPercent = ftRemainingPercent,
                        FreeTrialExpiry = (long)(item.FreeTrialInfo.FreeTrialExpiry / 1000), // Convert to seconds
                        ExpiryAfterSeconds = ftExpiryAfterSeconds
                    };
                }

                return new KiroUsageBreakdownDto
                {
                    DisplayName = item.DisplayName,
                    CurrentUsage = itemCurrentUsage,
                    UsageLimit = itemUsageLimit,
                    NextDateReset = (long)item.NextDateReset,
                    UsedPercent = itemUsedPercent,
                    Remaining = itemRemaining,
                    RemainingPercent = itemRemainingPercent,
                    ResetAfterSeconds = itemResetAfterSeconds,
                    FreeTrialInfo = freeTrialInfoDto
                };
            }).ToList();

            // 提取第一个免费试用信息作为单独展示（如果存在）
            KiroFreeTrialInfoDto? kiroFreeTrialInfo = null;
            if (kiroUsageBreakdownList != null)
            {
                var firstWithTrial = kiroUsageBreakdownList.FirstOrDefault(item => item.FreeTrialInfo != null);
                if (firstWithTrial?.FreeTrialInfo != null)
                {
                    kiroFreeTrialInfo = firstWithTrial.FreeTrialInfo;
                }
            }

            return new AccountQuotaStatusDto
            {
                AccountId = account.Id,
                HasCacheData = true,
                HealthScore = healthScore,
                PrimaryUsedPercent = usedPercent,
                SecondaryUsedPercent = usedPercent,
                PrimaryResetAfterSeconds = resetAfterSeconds ?? 0,
                SecondaryResetAfterSeconds = resetAfterSeconds ?? 0,
                StatusDescription = statusDescription,
                LastUpdatedAt = DateTime.UtcNow,
                KiroUsageBreakdownList = kiroUsageBreakdownList,
                KiroFreeTrialInfo = kiroFreeTrialInfo
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "刷新账户 {AccountId} 的 Kiro 配额状态失败", id);
            return null;
        }
    }

    /// <summary>
    /// 获取 Gemini Antigravity 可用模型列表
    /// </summary>
    /// <param name="id">账户 ID</param>
    /// <returns>模型名称列表，失败返回 null</returns>
    public async Task<List<string>?> GetAntigravityAvailableModelsAsync(int id)
    {
        var account = await _appDbContext.AIAccounts.FindAsync(id);
        if (account == null)
        {
            _logger.LogWarning("尝试获取不存在账户 {AccountId} 的 Antigravity 模型列表", id);
            return null;
        }

        if (account.Provider != AIProviders.GeminiAntigravity)
        {
            _logger.LogWarning("账户 {AccountId} 不是 Gemini Antigravity 账户，无法获取模型列表", id);
            return null;
        }

        var geminiOauth = account.GetGeminiOauth();
        if (geminiOauth == null)
        {
            _logger.LogWarning("账户 {AccountId} 没有 Gemini OAuth 凭证", id);
            return null;
        }

        var accessToken = geminiOauth.Token;
        if (string.IsNullOrEmpty(accessToken))
        {
            _logger.LogWarning("账户 {AccountId} 的 Access Token 为空", id);
            return null;
        }

        var apiUrl =
            $"{Services.GeminiOAuth.GeminiAntigravityOAuthConfig.AntigravityApiUrl}/v1internal:fetchAvailableModels";

        try
        {
            using var httpClient = new HttpClient();
            httpClient.DefaultRequestHeaders.Clear();
            httpClient.DefaultRequestHeaders.Add("User-Agent",
                Services.GeminiOAuth.GeminiAntigravityOAuthConfig.UserAgent);
            httpClient.DefaultRequestHeaders.Add("Authorization", $"Bearer {accessToken}");
            httpClient.Timeout = TimeSpan.FromSeconds(30);

            using var requestContent = new StringContent("{}", System.Text.Encoding.UTF8, "application/json");
            var response = await httpClient.PostAsync(apiUrl, requestContent);

            if (!response.IsSuccessStatusCode)
            {
                var errorBody = await response.Content.ReadAsStringAsync();
                _logger.LogError("Antigravity API 返回错误 {StatusCode}: {Error}",
                    (int)response.StatusCode, errorBody);
                return null;
            }

            var responseBody = await response.Content.ReadAsStringAsync();
            using var jsonDoc = System.Text.Json.JsonDocument.Parse(responseBody);
            var root = jsonDoc.RootElement;

            if (!root.TryGetProperty("models", out var modelsElement) ||
                modelsElement.ValueKind != System.Text.Json.JsonValueKind.Object)
            {
                _logger.LogWarning("Antigravity API 响应中没有 models 字段");
                return new List<string>();
            }

            var models = new List<string>();
            foreach (var modelProperty in modelsElement.EnumerateObject())
            {
                models.Add(modelProperty.Name);
            }

            return models;
        }
        catch (System.Net.Http.HttpRequestException ex)
        {
            _logger.LogError(ex, "调用 Antigravity API 获取模型列表时发生 HTTP 错误，账户 {AccountId}", id);
            return null;
        }
        catch (System.Threading.Tasks.TaskCanceledException ex)
        {
            _logger.LogError(ex, "调用 Antigravity API 获取模型列表超时或被取消，账户 {AccountId}", id);
            return null;
        }
        catch (System.Text.Json.JsonException ex)
        {
            _logger.LogError(ex, "解析 Antigravity API 响应失败，账户 {AccountId}", id);
            return null;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "获取 Antigravity 模型列表时发生未知错误，账户 {AccountId}", id);
            return null;
        }
    }

    private static bool IsOAuthTokenExpired(long expiresAtUnixSeconds)
    {
        if (expiresAtUnixSeconds <= 0)
        {
            return false;
        }

        // 提前一点刷新，避免临界点卡住
        const int expirySkewSeconds = 60;
        var nowUnixSeconds = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        return expiresAtUnixSeconds <= nowUnixSeconds + expirySkewSeconds;
    }

    private static bool IsClaudeTokenExpired(ClaudeAiOAuth claudeOauth)
    {
        return IsOAuthTokenExpired(claudeOauth.ExpiresAt);
    }

    private static string BuildClaudeMessagesUrl(string? baseUrl)
    {
        if (string.IsNullOrWhiteSpace(baseUrl))
        {
            return "https://api.anthropic.com/v1/messages?beta=true";
        }

        var trimmed = baseUrl.Trim();
        if (trimmed.EndsWith("/v1/messages", StringComparison.OrdinalIgnoreCase))
        {
            return trimmed + "?beta=true";
        }

        trimmed = trimmed.TrimEnd('/');

        if (trimmed.EndsWith("/v1", StringComparison.OrdinalIgnoreCase))
        {
            return trimmed + "/messages?beta=true";
        }

        return trimmed + "/v1/messages?beta=true";
    }
}