using System.Text.Json.Serialization;

namespace OneAI.Services.KiroOAuth;

/// <summary>
/// Kiro批量导入单个账户的数据结构
/// </summary>
public class KiroBatchAccountItem
{
    [JsonPropertyName("accessToken")]
    public string? AccessToken { get; set; }

    [JsonPropertyName("refreshToken")]
    public string? RefreshToken { get; set; }

    [JsonPropertyName("profileArn")]
    public string? ProfileArn { get; set; }

    [JsonPropertyName("expiresAt")]
    public string? ExpiresAt { get; set; }

    [JsonPropertyName("authMethod")]
    public string? AuthMethod { get; set; }

    [JsonPropertyName("provider")]
    public string? Provider { get; set; }

    [JsonPropertyName("email")]
    public string? Email { get; set; }

    [JsonPropertyName("addedAt")]
    public string? AddedAt { get; set; }

    [JsonPropertyName("id")]
    public string? Id { get; set; }

    [JsonPropertyName("usageLimit")]
    public KiroBatchUsageLimit? UsageLimit { get; set; }
}

/// <summary>
/// Kiro批量导入使用限制数据结构
/// </summary>
public class KiroBatchUsageLimit
{
    [JsonPropertyName("limit")]
    public int Limit { get; set; }

    [JsonPropertyName("used")]
    public int Used { get; set; }

    [JsonPropertyName("remaining")]
    public int Remaining { get; set; }
}

/// <summary>
/// Kiro批量导入请求
/// </summary>
public class ImportKiroBatchRequest
{
    /// <summary>
    /// 批量导入的账户列表
    /// </summary>
    public List<KiroBatchAccountItem> Accounts { get; set; } = new();

    /// <summary>
    /// 是否跳过已存在的账户（基于email判断）
    /// </summary>
    public bool SkipExisting { get; set; } = true;

    /// <summary>
    /// 账户名称前缀（可选，用于批量命名）
    /// </summary>
    public string? AccountNamePrefix { get; set; }
}

/// <summary>
/// 批量导入结果
/// </summary>
public class ImportKiroBatchResult
{
    /// <summary>
    /// 成功导入的数量
    /// </summary>
    public int SuccessCount { get; set; }

    /// <summary>
    /// 失败的数量
    /// </summary>
    public int FailCount { get; set; }

    /// <summary>
    /// 跳过的数量（已存在）
    /// </summary>
    public int SkippedCount { get; set; }

    /// <summary>
    /// 成功导入的账户列表
    /// </summary>
    public List<ImportSuccessItem> SuccessItems { get; set; } = new();

    /// <summary>
    /// 失败的错误信息列表
    /// </summary>
    public List<ImportFailItem> FailItems { get; set; } = new();
}

/// <summary>
/// 成功导入项
/// </summary>
public class ImportSuccessItem
{
    /// <summary>
    /// 原始数据中的ID
    /// </summary>
    public string? OriginalId { get; set; }

    /// <summary>
    /// 创建的账户ID
    /// </summary>
    public int AccountId { get; set; }

    /// <summary>
    /// 邮箱
    /// </summary>
    public string? Email { get; set; }

    /// <summary>
    /// 账户名称
    /// </summary>
    public string? AccountName { get; set; }
}

/// <summary>
/// 失败导入项
/// </summary>
public class ImportFailItem
{
    /// <summary>
    /// 原始数据中的ID
    /// </summary>
    public string? OriginalId { get; set; }

    /// <summary>
    /// 邮箱
    /// </summary>
    public string? Email { get; set; }

    /// <summary>
    /// 错误信息
    /// </summary>
    public string ErrorMessage { get; set; } = string.Empty;
}