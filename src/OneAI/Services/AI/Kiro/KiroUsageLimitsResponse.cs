using System.Text.Json.Serialization;

namespace OneAI.Services.AI;

/// <summary>
/// Kiro usage limits response DTO
/// </summary>
public class KiroUsageLimitsResponse
{
    [JsonPropertyName("usageBreakdownList")]
    public List<KiroUsageBreakdown>? UsageBreakdownList { get; set; }
}