namespace OneAI.Services.AI.Kiro;


/// <summary>
/// 缓存查找结果
/// </summary>
public sealed class CacheResult
{
    /// <summary>
    /// 未缓存的输入 token 数
    /// </summary>
    public int UncachedInputTokens { get; set; }

    /// <summary>
    /// 缓存读取的 token 数
    /// </summary>
    public int CacheReadInputTokens { get; set; }

    /// <summary>
    /// 缓存创建的 token 数
    /// </summary>
    public int CacheCreationInputTokens { get; set; }
}
