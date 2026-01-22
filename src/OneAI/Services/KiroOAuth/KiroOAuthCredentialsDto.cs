using System.Text.Json.Serialization;

namespace OneAI.Services.KiroOAuth;

/// <summary>
/// Kiro OAuth/Reverse credentials
/// </summary>
public class KiroOAuthCredentialsDto
{
    [JsonPropertyName("accessToken")]
    public string? AccessToken { get; set; }

    [JsonPropertyName("refreshToken")]
    public string? RefreshToken { get; set; }

    [JsonPropertyName("clientId")]
    public string? ClientId { get; set; }

    [JsonPropertyName("clientSecret")]
    public string? ClientSecret { get; set; }

    [JsonPropertyName("authMethod")]
    public string? AuthMethod { get; set; }

    [JsonPropertyName("expiresAt")]
    public string? ExpiresAt { get; set; }

    [JsonPropertyName("profileArn")]
    public string? ProfileArn { get; set; }

    [JsonPropertyName("region")]
    public string? Region { get; set; }

    [JsonPropertyName("uuid")]
    public string? Uuid { get; set; }

    [JsonPropertyName("machineId")]
    public string? MachineId { get; set; }

    [JsonPropertyName("email")]
    public string? Email { get; set; }
}
