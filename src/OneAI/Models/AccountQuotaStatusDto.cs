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
