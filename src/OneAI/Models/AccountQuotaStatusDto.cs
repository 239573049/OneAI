namespace OneAI.Models;

/// <summary>
/// 账户配额状态 DTO（从缓存获取）
/// </summary>
public class AccountQuotaStatusDto
{
    /// <summary>
    /// 账户 ID
    /// </summary>
    public int AccountId { get; set; }

    /// <summary>
    /// 健康度评分（0-100）
    /// </summary>
    public int? HealthScore { get; set; }

    /// <summary>
    /// 主窗口（5小时）使用百分比
    /// </summary>
    public int? PrimaryUsedPercent { get; set; }

    /// <summary>
    /// 次级窗口（7天）使用百分比
    /// </summary>
    public int? SecondaryUsedPercent { get; set; }

    /// <summary>
    /// 主窗口配额重置剩余秒数
    /// </summary>
    public int? PrimaryResetAfterSeconds { get; set; }

    /// <summary>
    /// 次级窗口配额重置剩余秒数
    /// </summary>
    public int? SecondaryResetAfterSeconds { get; set; }

    /// <summary>
    /// 配额状态描述
    /// </summary>
    public string? StatusDescription { get; set; }

    /// <summary>
    /// 是否有缓存数据
    /// </summary>
    public bool HasCacheData { get; set; }

    /// <summary>
    /// 缓存数据最后更新时间
    /// </summary>
    public DateTime? LastUpdatedAt { get; set; }

    /// <summary>
    /// Token 限制（Anthropic 风格）
    /// </summary>
    public long? TokensLimit { get; set; }

    /// <summary>
    /// Token 剩余量（Anthropic 风格）
    /// </summary>
    public long? TokensRemaining { get; set; }

    /// <summary>
    /// Token 使用百分比（Anthropic 风格）
    /// </summary>
    public int? TokensUsedPercent { get; set; }

    /// <summary>
    /// Input Token 限制
    /// </summary>
    public long? InputTokensLimit { get; set; }

    /// <summary>
    /// Input Token 剩余量
    /// </summary>
    public long? InputTokensRemaining { get; set; }

    /// <summary>
    /// Output Token 限制
    /// </summary>
    public long? OutputTokensLimit { get; set; }

    /// <summary>
    /// Output Token 剩余量
    /// </summary>
    public long? OutputTokensRemaining { get; set; }

    /// <summary>
    /// Anthropic Unified 总状态（allowed / rejected 等）
    /// </summary>
    public string? AnthropicUnifiedStatus { get; set; }

    public string? AnthropicUnifiedFiveHourStatus { get; set; }

    public double? AnthropicUnifiedFiveHourUtilization { get; set; }

    public string? AnthropicUnifiedSevenDayStatus { get; set; }

    public double? AnthropicUnifiedSevenDayUtilization { get; set; }

    public string? AnthropicUnifiedRepresentativeClaim { get; set; }

    public double? AnthropicUnifiedFallbackPercentage { get; set; }

    public long? AnthropicUnifiedResetAt { get; set; }

    public string? AnthropicUnifiedOverageDisabledReason { get; set; }

    public string? AnthropicUnifiedOverageStatus { get; set; }

    /// <summary>
    /// Gemini Antigravity 各模型配额信息（所有模型）
    /// </summary>
    public List<AntigravityModelQuotaDto>? AntigravityModelQuotas { get; set; }

    /// <summary>
    /// Kiro 使用明细列表（包含 Credits 等使用情况）
    /// </summary>
    public List<KiroUsageBreakdownDto>? KiroUsageBreakdownList { get; set; }

    /// <summary>
    /// Kiro 免费试用信息（单独展示）
    /// </summary>
    public KiroFreeTrialInfoDto? KiroFreeTrialInfo { get; set; }
}

/// <summary>
/// Kiro 使用明细 DTO
/// </summary>
public sealed class KiroUsageBreakdownDto
{
    /// <summary>
    /// 显示名称（如 "Credits"）
    /// </summary>
    public string? DisplayName { get; set; }

    /// <summary>
    /// 当前使用量
    /// </summary>
    public double CurrentUsage { get; set; }

    /// <summary>
    /// 使用限制
    /// </summary>
    public double UsageLimit { get; set; }

    /// <summary>
    /// 下次重置时间（Unix 时间戳，秒）
    /// </summary>
    public long NextDateReset { get; set; }

    /// <summary>
    /// 使用百分比（0-100）
    /// </summary>
    public int? UsedPercent { get; set; }

    /// <summary>
    /// 剩余量
    /// </summary>
    public double? Remaining { get; set; }

    /// <summary>
    /// 剩余百分比（0-100）
    /// </summary>
    public int? RemainingPercent { get; set; }

    /// <summary>
    /// 重置剩余秒数
    /// </summary>
    public int? ResetAfterSeconds { get; set; }

    /// <summary>
    /// 免费试用信息
    /// </summary>
    public KiroFreeTrialInfoDto? FreeTrialInfo { get; set; }
}

/// <summary>
/// Kiro 免费试用信息 DTO
/// </summary>
public sealed class KiroFreeTrialInfoDto
{
    /// <summary>
    /// 试用状态（如 "ACTIVE"）
    /// </summary>
    public string? FreeTrialStatus { get; set; }

    /// <summary>
    /// 当前使用量
    /// </summary>
    public double CurrentUsage { get; set; }

    /// <summary>
    /// 使用限制
    /// </summary>
    public double UsageLimit { get; set; }

    /// <summary>
    /// 使用百分比（0-100）
    /// </summary>
    public int? UsedPercent { get; set; }

    /// <summary>
    /// 剩余量
    /// </summary>
    public double? Remaining { get; set; }

    /// <summary>
    /// 剩余百分比（0-100）
    /// </summary>
    public int? RemainingPercent { get; set; }

    /// <summary>
    /// 试用到期时间（Unix 时间戳，秒）
    /// </summary>
    public long FreeTrialExpiry { get; set; }

    /// <summary>
    /// 到期剩余秒数
    /// </summary>
    public int? ExpiryAfterSeconds { get; set; }
}

public sealed class AntigravityModelQuotaDto
{
    public string Model { get; set; } = string.Empty;

    public bool HasQuotaInfo { get; set; }

    public double? RemainingFraction { get; set; }

    public int? RemainingPercent { get; set; }

    public int? UsedPercent { get; set; }

    public string? ResetTime { get; set; }

    public int? ResetAfterSeconds { get; set; }
}
