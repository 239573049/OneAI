namespace OneAI.Services.AI.Kiro;


/// <summary>
/// Token使用详情
/// </summary>
public sealed class KiroTokenUsage
{
    public int InputTokens { get; set; }
    public int OutputTokens { get; set; }
    public int CacheCreationInputTokens { get; set; }
    public int CacheReadInputTokens { get; set; }
    public double TotalCost { get; set; }
}