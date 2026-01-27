namespace OneAI.Models;

/// <summary>
/// 每小时总体统计DTO
/// </summary>
public class HourlySummaryDto
{
    /// <summary>
    /// 统计小时起始时间（UTC）
    /// </summary>
    public DateTime HourStartTime { get; set; }

    /// <summary>
    /// 总请求数
    /// </summary>
    public int TotalRequests { get; set; }

    /// <summary>
    /// 成功请求数
    /// </summary>
    public int SuccessRequests { get; set; }

    /// <summary>
    /// 失败请求数
    /// </summary>
    public int FailedRequests { get; set; }

    /// <summary>
    /// 成功率（0-1）
    /// </summary>
    public double SuccessRate { get; set; }

    /// <summary>
    /// 总Token消耗
    /// </summary>
    public long TotalTokens { get; set; }

    /// <summary>
    /// 平均响应时间（毫秒）
    /// </summary>
    public double AvgDurationMs { get; set; }

    /// <summary>
    /// P95响应时间（毫秒）
    /// </summary>
    public long? P95DurationMs { get; set; }
}

/// <summary>
/// 按模型分组的小时统计DTO
/// </summary>
public class HourlyByModelDto
{
    /// <summary>
    /// 统计小时起始时间（UTC）
    /// </summary>
    public DateTime HourStartTime { get; set; }

    /// <summary>
    /// 模型名称
    /// </summary>
    public string Model { get; set; } = string.Empty;

    /// <summary>
    /// AI提供商
    /// </summary>
    public string? Provider { get; set; }

    /// <summary>
    /// 总请求数
    /// </summary>
    public int TotalRequests { get; set; }

    /// <summary>
    /// 成功请求数
    /// </summary>
    public int SuccessRequests { get; set; }

    /// <summary>
    /// 成功率（0-1）
    /// </summary>
    public double SuccessRate { get; set; }

    /// <summary>
    /// 输入Token消耗（Prompt Tokens）
    /// </summary>
    public long PromptTokens { get; set; }

    /// <summary>
    /// 输出Token消耗（Completion Tokens）
    /// </summary>
    public long CompletionTokens { get; set; }

    /// <summary>
    /// 总Token消耗
    /// </summary>
    public long TotalTokens { get; set; }

    /// <summary>
    /// 平均响应时间（毫秒）
    /// </summary>
    public double AvgDurationMs { get; set; }
}

/// <summary>
/// 按账户分组的小时统计DTO
/// </summary>
public class HourlyByAccountDto
{
    /// <summary>
    /// 统计小时起始时间（UTC）
    /// </summary>
    public DateTime HourStartTime { get; set; }

    /// <summary>
    /// 账户ID
    /// </summary>
    public int AccountId { get; set; }

    /// <summary>
    /// 账户名称
    /// </summary>
    public string? AccountName { get; set; }

    /// <summary>
    /// AI提供商
    /// </summary>
    public string? Provider { get; set; }

    /// <summary>
    /// 总请求数
    /// </summary>
    public int TotalRequests { get; set; }

    /// <summary>
    /// 成功请求数
    /// </summary>
    public int SuccessRequests { get; set; }

    /// <summary>
    /// 成功率（0-1）
    /// </summary>
    public double SuccessRate { get; set; }

    /// <summary>
    /// 总Token消耗
    /// </summary>
    public long TotalTokens { get; set; }

    /// <summary>
    /// 限流请求数
    /// </summary>
    public int RateLimitedRequests { get; set; }
}
