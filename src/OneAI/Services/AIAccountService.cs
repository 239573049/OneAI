using Microsoft.EntityFrameworkCore;
using OneAI.Constants;
using OneAI.Data;
using OneAI.Entities;
using OneAI.Models;
using OneAI.Services.AI;

namespace OneAI.Services;

public class AIAccountService
{
    private readonly AppDbContext _appDbContext;
    private readonly AccountQuotaCacheService _quotaCache;
    private readonly ILogger<AIAccountService> _logger;

    public AIAccountService(
        AppDbContext appDbContext,
        AccountQuotaCacheService quotaCache,
        ILogger<AIAccountService> logger)
    {
        _appDbContext = appDbContext;
        _quotaCache = quotaCache;
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
    public async Task<AIAccount?> GetAIAccountByProvider(string provider)
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
                        x.Provider == provider &&
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

        return new AccountQuotaStatusDto
        {
            AccountId = accountId,
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
            var accessToken = account.GetOpenAiOauth()!.AccessToken;
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
            foreach (var header in headers)
            {
                httpClient.DefaultRequestHeaders.TryAddWithoutValidation(header.Key, header.Value);
            }

            // 发送一个简单的 HEAD 请求来获取配额头信息
            var response = await httpClient.GetAsync($"{address.TrimEnd('/')}/responses");

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
            var apiUrl = $"{Services.GeminiOAuth.GeminiAntigravityOAuthConfig.AntigravityApiUrl}/v1internal:fetchAvailableModels";

            using var httpClient = new HttpClient();
            httpClient.DefaultRequestHeaders.Clear();
            httpClient.DefaultRequestHeaders.Add("User-Agent", Services.GeminiOAuth.GeminiAntigravityOAuthConfig.UserAgent);
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
            if (!root.TryGetProperty("models", out var modelsElement) || modelsElement.ValueKind != System.Text.Json.JsonValueKind.Object)
            {
                _logger.LogWarning("Antigravity API 响应中没有 models 字段");
                return new AccountQuotaStatusDto
                {
                    AccountId = account.Id,
                    HasCacheData = false
                };
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
                    HasCacheData = false
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
                LastUpdatedAt = DateTime.UtcNow
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "刷新账户 {AccountId} 的 Antigravity 配额状态失败", id);
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

        var apiUrl = $"{Services.GeminiOAuth.GeminiAntigravityOAuthConfig.AntigravityApiUrl}/v1internal:fetchAvailableModels";

        try
        {
            using var httpClient = new HttpClient();
            httpClient.DefaultRequestHeaders.Clear();
            httpClient.DefaultRequestHeaders.Add("User-Agent", Services.GeminiOAuth.GeminiAntigravityOAuthConfig.UserAgent);
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

            if (!root.TryGetProperty("models", out var modelsElement) || modelsElement.ValueKind != System.Text.Json.JsonValueKind.Object)
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
}
