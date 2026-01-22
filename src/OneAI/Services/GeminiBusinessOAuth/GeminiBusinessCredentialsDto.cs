using System.Text.Json.Serialization;

namespace OneAI.Services.GeminiBusinessOAuth;

/// <summary>
/// Gemini Business credentials (business.gemini.google reverse)
/// </summary>
public class GeminiBusinessCredentialsDto
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
    public bool Disabled { get; set; }
}

