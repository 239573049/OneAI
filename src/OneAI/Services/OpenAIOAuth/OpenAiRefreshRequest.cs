using System.Text.Json.Serialization;

namespace OneAI.Services.OpenAIOAuth;


/// <summary>
///     OpenAI刷新令牌请求
/// </summary>
public class OpenAiRefreshRequest
{
    [JsonPropertyName("client_id")]
    public string ClientId { get; set; } = string.Empty;
    
    [JsonPropertyName("grant_type")]
    public string GrantType { get; set; } = "refresh_token";

    [JsonPropertyName("refresh_token")]
    public string RefreshToken { get; set; } = string.Empty;

    [JsonPropertyName("scope")]
    public string Scope { get; set; } = "openid profile email";
}