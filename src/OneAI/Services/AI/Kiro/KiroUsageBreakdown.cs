using System.Text.Json.Serialization;

namespace OneAI.Services.AI;

/// <summary>
/// Kiro usage breakdown item
/// </summary>
public class KiroUsageBreakdown
{
    [JsonPropertyName("displayName")] public string? DisplayName { get; set; }

    [JsonPropertyName("displayNamePlural")]
    public string? DisplayNamePlural { get; set; }

    [JsonPropertyName("currentUsage")] public double CurrentUsage { get; set; }

    [JsonPropertyName("currentUsageWithPrecision")]
    public double CurrentUsageWithPrecision { get; set; }

    [JsonPropertyName("usageLimit")] public double UsageLimit { get; set; }

    [JsonPropertyName("usageLimitWithPrecision")]
    public double UsageLimitWithPrecision { get; set; }

    [JsonPropertyName("nextDateReset")] public double NextDateReset { get; set; }

    [JsonPropertyName("freeTrialInfo")] public KiroFreeTrialInfo? FreeTrialInfo { get; set; }
}