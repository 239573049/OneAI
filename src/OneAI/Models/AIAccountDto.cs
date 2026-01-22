namespace OneAI.Models;

/// <summary>
/// AI 账户数据传输对象
/// </summary>
public class AIAccountDto
{
    /// <summary>
    /// 账户 ID
    /// </summary>
    public int Id { get; set; }

    /// <summary>
    /// AI 提供商名称
    /// </summary>
    public required string Provider { get; set; }

    /// <summary>
    /// 账户名称/备注
    /// </summary>
    public string? Name { get; set; }

    /// <summary>
    /// 邮箱
    /// </summary>
    public string? Email { get; set; }

    /// <summary>
    /// 基础 URL
    /// </summary>
    public string? BaseUrl { get; set; }

    /// <summary>
    /// 是否启用
    /// </summary>
    public bool IsEnabled { get; set; }

    /// <summary>
    /// 是否被限流
    /// </summary>
    public bool IsRateLimited { get; set; }

    /// <summary>
    /// 限流解除时间
    /// </summary>
    public DateTime? RateLimitResetTime { get; set; }

    /// <summary>
    /// 创建时间
    /// </summary>
    public DateTime CreatedAt { get; set; }

    /// <summary>
    /// 更新时间
    /// </summary>
    public DateTime? UpdatedAt { get; set; }

    /// <summary>
    /// 最后使用时间
    /// </summary>
    public DateTime? LastUsedAt { get; set; }

    /// <summary>
    /// 使用次数统计
    /// </summary>
    public int UsageCount { get; set; }

    /// <summary>
    /// 累计请求Token数量
    /// </summary>
    public long PromptTokens { get; set; }

    /// <summary>
    /// 累计完成Token数量
    /// </summary>
    public long CompletionTokens { get; set; }

    /// <summary>
    /// 累计缓存Token数量
    /// </summary>
    public long CacheTokens { get; set; }

    /// <summary>
    /// 累计创建缓存Token数量
    /// </summary>
    public long CreateCacheTokens { get; set; }

    /// <summary>
    /// Gemini 项目 ID（仅当提供商为 Gemini 时）
    /// </summary>
    public string? ProjectId { get; set; }
}

/// <summary>
/// 批量操作结果
/// </summary>
public class BatchOperationResult
{
    /// <summary>
    /// 成功数量
    /// </summary>
    public int SuccessCount { get; set; }

    /// <summary>
    /// 失败数量
    /// </summary>
    public int FailedCount { get; set; }

    /// <summary>
    /// 总数量
    /// </summary>
    public int TotalCount { get; set; }

    /// <summary>
    /// 失败的账户ID列表
    /// </summary>
    public List<int> FailedIds { get; set; } = new();
}
