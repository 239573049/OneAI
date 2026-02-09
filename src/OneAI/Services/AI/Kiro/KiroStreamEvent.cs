namespace OneAI.Services.AI.Kiro;


public sealed class KiroStreamEvent(KiroStreamEventType type)
{
    public KiroStreamEventType Type { get; } = type;
    public string? Content { get; init; }
    public KiroToolUseEvent? ToolUse { get; init; }
    public string? ToolInput { get; init; }
    public bool? ToolStop { get; init; }
    public KiroUsageInfo? UsageInfo { get; init; }
    public double? ContextUsagePercentage { get; init; }
}