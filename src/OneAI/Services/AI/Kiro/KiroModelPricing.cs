namespace OneAI.Services.AI.Kiro;

/// <summary>
/// 模型价格配置记录
/// </summary>
/// <param name="InputPrice">输入价格 $/MTok</param>
/// <param name="OutputPrice">输出价格 $/MTok</param>
/// <param name="CacheCreatePrice">创建缓存价格 $/MTok</param>
/// <param name="CacheReadPrice">读取缓存价格 $/MTok</param>
/// <param name="MaxContextTokens">最大上下文token数</param>
public sealed record KiroModelPricing(
    double InputPrice,
    double OutputPrice,
    double CacheCreatePrice,
    double CacheReadPrice,
    int MaxContextTokens);