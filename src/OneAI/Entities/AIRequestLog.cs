namespace OneAI.Entities;

/// <summary>
/// AI模型请求日志 - 记录完整的请求生命周期
/// </summary>
public class AIRequestLog
{
    /// <summary>
    /// 主键ID
    /// </summary>
    public long Id { get; set; }

    /// <summary>
    /// 请求唯一标识（用于追踪）
    /// </summary>
    public string RequestId { get; set; } = string.Empty;

    /// <summary>
    /// 会话ID（conversation_id）
    /// </summary>
    public string? ConversationId { get; set; }

    /// <summary>
    /// 会话标识（session_id）
    /// </summary>
    public string? SessionId { get; set; }

    // ==================== 请求信息 ====================

    /// <summary>
    /// 使用的AI账户ID
    /// </summary>
    public int? AccountId { get; set; }

    /// <summary>
    /// AI提供商（OpenAI, Claude等）
    /// </summary>
    public string? Provider { get; set; }

    /// <summary>
    /// 请求的模型名称
    /// </summary>
    public string Model { get; set; } = string.Empty;

    /// <summary>
    /// 是否为流式传输
    /// </summary>
    public bool IsStreaming { get; set; }

    /// <summary>
    /// HTTP状态码
    /// </summary>
    public int? StatusCode { get; set; }

    /// <summary>
    /// 请求是否成功
    /// </summary>
    public bool IsSuccess { get; set; }

    /// <summary>
    /// 错误信息
    /// </summary>
    public string? ErrorMessage { get; set; }

    /// <summary>
    /// 重试次数
    /// </summary>
    public int RetryCount { get; set; }

    /// <summary>
    /// 使用的总尝试次数
    /// </summary>
    public int TotalAttempts { get; set; }

    /// <summary>
    /// 提示Token数量
    /// </summary>
    public int? PromptTokens { get; set; }

    /// <summary>
    /// 使用换成Token数量
    /// </summary>
    public int? CacheTokens { get; set; }

    /// <summary>
    /// 创建缓存tokens
    /// </summary>
    public int? CreateCacheTokens { get; set; }

    /// <summary>
    /// 完成Token数量
    /// </summary>
    public int? CompletionTokens { get; set; }

    /// <summary>
    /// 总Token数量
    /// </summary>
    public int? TotalTokens { get; set; }

    // ==================== 性能指标 ====================

    /// <summary>
    /// 请求开始时间
    /// </summary>
    public DateTime RequestStartTime { get; set; }

    /// <summary>
    /// 请求结束时间
    /// </summary>
    public DateTime? RequestEndTime { get; set; }

    /// <summary>
    /// 总耗时（毫秒）
    /// </summary>
    public long? DurationMs { get; set; }

    /// <summary>
    /// 首字节时间（毫秒）
    /// </summary>
    public long? TimeToFirstByteMs { get; set; }

    // ==================== 配额和限流信息 ====================

    /// <summary>
    /// 是否触发限流
    /// </summary>
    public bool IsRateLimited { get; set; }

    /// <summary>
    /// 限流重置时间（秒）
    /// </summary>
    public int? RateLimitResetSeconds { get; set; }

    /// <summary>
    /// 配额使用情况（JSON格式）
    /// </summary>
    public string? QuotaInfo { get; set; }

    /// <summary>
    /// 是否会话粘性成功
    /// </summary>
    public bool SessionStickinessUsed { get; set; }

    // ==================== 其他元数据 ====================

    /// <summary>
    /// 客户端IP地址
    /// </summary>
    public string? ClientIp { get; set; }

    /// <summary>
    /// User-Agent
    /// </summary>
    public string? UserAgent { get; set; }

    /// <summary>
    /// 请求来源标识
    /// </summary>
    public string? Originator { get; set; }

    /// <summary>
    /// 记录创建时间
    /// </summary>
    public DateTime CreatedAt { get; set; }

    /// <summary>
    /// 记录更新时间
    /// </summary>
    public DateTime UpdatedAt { get; set; }

    /// <summary>
    /// 扩展信息（JSON格式，用于存储额外的自定义数据）
    /// </summary>
    public string? ExtensionData { get; set; }

    /// <summary>
    /// 关联的AI账户
    /// </summary>
    public AIAccount? Account { get; set; }
}
