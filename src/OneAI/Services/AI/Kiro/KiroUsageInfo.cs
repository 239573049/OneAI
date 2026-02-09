namespace OneAI.Services.AI.Kiro;


/// <summary>
/// Kiro usage info from stream event
/// </summary>
public sealed class KiroUsageInfo
{
    public string? Unit { get; init; }
    public double Usage { get; init; }
}