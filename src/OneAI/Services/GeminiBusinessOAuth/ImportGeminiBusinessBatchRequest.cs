using System.Text.Json.Serialization;

namespace OneAI.Services.GeminiBusinessOAuth;

/// <summary>
/// Gemini Business batch import single account item
/// Compatible with gemini-business2api accounts.json items.
/// </summary>
public class GeminiBusinessBatchAccountItem
{
    [JsonPropertyName("secure_c_ses")]
    public string? SecureCSes { get; set; }

    [JsonPropertyName("host_c_oses")]
    public string? HostCOses { get; set; }

    [JsonPropertyName("csesidx")]
    public string? Csesidx { get; set; }

    [JsonPropertyName("config_id")]
    public string? ConfigId { get; set; }

    [JsonPropertyName("expires_at")]
    public string? ExpiresAt { get; set; }

    [JsonPropertyName("disabled")]
    public bool? Disabled { get; set; }

    [JsonPropertyName("email")]
    public string? Email { get; set; }

    [JsonPropertyName("id")]
    public string? Id { get; set; }
}

/// <summary>
/// Gemini Business batch import request
/// </summary>
public class ImportGeminiBusinessBatchRequest
{
    /// <summary>
    /// Accounts to import
    /// </summary>
    public List<GeminiBusinessBatchAccountItem> Accounts { get; set; } = new();

    /// <summary>
    /// Skip existing accounts (by csesidx when available, otherwise by email)
    /// </summary>
    public bool SkipExisting { get; set; } = true;

    /// <summary>
    /// Optional account name prefix
    /// </summary>
    public string? AccountNamePrefix { get; set; }
}

/// <summary>
/// Batch import result
/// </summary>
public class ImportGeminiBusinessBatchResult
{
    public int SuccessCount { get; set; }

    public int FailCount { get; set; }

    public int SkippedCount { get; set; }

    public List<ImportSuccessItem> SuccessItems { get; set; } = new();

    public List<ImportFailItem> FailItems { get; set; } = new();
}

/// <summary>
/// Batch import success item
/// </summary>
public class ImportSuccessItem
{
    public string? OriginalId { get; set; }

    public int AccountId { get; set; }

    public string? Email { get; set; }

    public string? AccountName { get; set; }
}

/// <summary>
/// Batch import fail item
/// </summary>
public class ImportFailItem
{
    public string? OriginalId { get; set; }

    public string? Email { get; set; }

    public string ErrorMessage { get; set; } = string.Empty;
}

