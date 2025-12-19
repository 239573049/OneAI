namespace OneAI.Entities;

/// <summary>
/// AI 账户实体
/// </summary>
public class AIAccount
{
    /// <summary>
    /// 账户 ID
    /// </summary>
    public int Id { get; set; }

    /// <summary>
    /// AI 提供商名称（如 OpenAI, Claude, Gemini 等）
    /// </summary>
    public required string Provider { get; set; }

    /// <summary>
    /// API Key（用于 API Key 认证方式）
    /// </summary>
    public string? ApiKey { get; set; }

    /// <summary>
    /// 账户名称/备注
    /// </summary>
    public string? Name { get; set; }

    /// <summary>
    /// 基础 URL（可选，用于自定义 API 端点）
    /// </summary>
    public string? BaseUrl { get; set; }

    /// <summary>
    /// 是否启用
    /// </summary>
    public bool IsEnabled { get; set; } = true;

    /// <summary>
    /// 是否被限流
    /// </summary>
    public bool IsRateLimited { get; set; } = false;

    /// <summary>
    /// 限流解除时间（当 IsRateLimited 为 true 时有效）
    /// </summary>
    public DateTime? RateLimitResetTime { get; set; }

    /// <summary>
    /// OAuth2 令牌数据（JSON 格式，包含访问令牌、刷新令牌等信息，由后台解析）
    /// </summary>
    public string? OAuthToken { get; set; }

    /// <summary>
    /// 创建时间
    /// </summary>
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

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
    public int UsageCount { get; set; } = 0;

    /// <summary>
    /// 判断是否使用 OAuth 认证方式
    /// </summary>
    public bool IsUsingOAuth()
    {
        return !string.IsNullOrEmpty(OAuthToken);
    }

    /// <summary>
    /// 判断限流是否已解除
    /// </summary>
    public bool IsRateLimitExpired()
    {
        return IsRateLimited && RateLimitResetTime.HasValue && RateLimitResetTime.Value <= DateTime.UtcNow;
    }

    /// <summary>
    /// 判断账户是否可用（启用 + 未被限流）
    /// </summary>
    public bool IsAvailable()
    {
        if (!IsEnabled)
            return false;

        // 如果正在限流且未到解除时间，不可用
        if (IsRateLimited && RateLimitResetTime.HasValue && RateLimitResetTime.Value > DateTime.UtcNow)
            return false;

        return true;
    }
}
