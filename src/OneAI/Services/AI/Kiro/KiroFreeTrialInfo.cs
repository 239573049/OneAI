using System.Text.Json.Serialization;

namespace OneAI.Services.AI;

/// <summary>
/// Kiro free trial info
/// </summary>
public class KiroFreeTrialInfo
{
    [JsonPropertyName("freeTrialStatus")] public string? FreeTrialStatus { get; set; }

    [JsonPropertyName("currentUsage")] public double CurrentUsage { get; set; }

    [JsonPropertyName("currentUsageWithPrecision")]
    public double CurrentUsageWithPrecision { get; set; }

    [JsonPropertyName("usageLimit")] public double UsageLimit { get; set; }

    [JsonPropertyName("usageLimitWithPrecision")]
    public double UsageLimitWithPrecision { get; set; }

    [JsonPropertyName("freeTrialExpiry")] public double FreeTrialExpiry { get; set; }
}