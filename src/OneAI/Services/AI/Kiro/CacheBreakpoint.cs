namespace OneAI.Services.AI.Kiro;


/// <summary>
/// 缓存断点信息
/// </summary>
public sealed class CacheBreakpoint
{
    /// <summary>
    /// 断点内容的 SHA256 哈希
    /// </summary>
    public required string Hash { get; init; }

    /// <summary>
    /// 该断点包含的 token 数量
    /// </summary>
    public int TokenCount { get; init; }

    /// <summary>
    /// 创建时间
    /// </summary>
    public DateTime CreatedAt { get; init; } = DateTime.UtcNow;
}
