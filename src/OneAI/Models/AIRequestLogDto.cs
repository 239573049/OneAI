namespace OneAI.Models;

/// <summary>
/// AI请求日志查询请求
/// </summary>
public class AIRequestLogQueryRequest
{
    /// <summary>
    /// 账户ID过滤（可选）
    /// </summary>
    public int? AccountId { get; set; }

    /// <summary>
    /// 开始时间（默认7天前）
    /// </summary>
    public DateTime? StartTime { get; set; }

    /// <summary>
    /// 结束时间（默认现在）
    /// </summary>
    public DateTime? EndTime { get; set; }

    /// <summary>
    /// 模型过滤（可选）
    /// </summary>
    public string? Model { get; set; }

    /// <summary>
    /// 是否成功过滤（可选）
    /// </summary>
    public bool? IsSuccess { get; set; }

    /// <summary>
    /// 页码（从1开始）
    /// </summary>
    public int PageNumber { get; set; } = 1;

    /// <summary>
    /// 每页数量
    /// </summary>
    public int PageSize { get; set; } = 50;
}

/// <summary>
/// AI请求日志响应
/// </summary>
public class AIRequestLogDto
{
    public long Id { get; set; }
    public string RequestId { get; set; } = string.Empty;
    public string? ConversationId { get; set; }
    public string? SessionId { get; set; }

    // 账户信息
    public int? AccountId { get; set; }
    public string? AccountName { get; set; }
    public string? AccountEmail { get; set; }
    public string? Provider { get; set; }

    // 请求信息
    public string Model { get; set; } = string.Empty;
    public bool IsStreaming { get; set; }
    // 响应信息
    public int? StatusCode { get; set; }
    public bool IsSuccess { get; set; }
    public string? ErrorMessage { get; set; }
    public int RetryCount { get; set; }
    public int TotalAttempts { get; set; }

    // Token使用
    public int? PromptTokens { get; set; }
    public int? CompletionTokens { get; set; }
    public int? TotalTokens { get; set; }

    // 性能指标
    public DateTime RequestStartTime { get; set; }
    public DateTime? RequestEndTime { get; set; }
    public long? DurationMs { get; set; }
    public long? TimeToFirstByteMs { get; set; }

    // 配额和限流
    public bool IsRateLimited { get; set; }
    public int? RateLimitResetSeconds { get; set; }
    public bool SessionStickinessUsed { get; set; }

    // 其他元数据
    public string? ClientIp { get; set; }
    public DateTime CreatedAt { get; set; }
}

/// <summary>
/// 分页响应
/// </summary>
public class PagedResponse<T>
{
    public List<T> Items { get; set; } = new();
    public int TotalCount { get; set; }
    public int PageNumber { get; set; }
    public int PageSize { get; set; }
    public int TotalPages => (int)Math.Ceiling(TotalCount / (double)PageSize);
    public bool HasPreviousPage => PageNumber > 1;
    public bool HasNextPage => PageNumber < TotalPages;
}
