using OneAI.Models;

namespace OneAI.Services.OpenAIOAuth;

public class OAuthSessionData
{
    public string CodeVerifier { get; set; }

    public string State { get; set; }

    public string CodeChallenge { get; set; }

    public ProxyConfig? Proxy { get; set; }

    public DateTime CreatedAt { get; set; }

    public DateTime ExpiresAt { get; set; }

    // Device Authorization Flow (用于 Factory OAuth 等设备码流程)
    public string? DeviceCode { get; set; }

    public string? DeviceUserCode { get; set; }

    public string? DeviceVerificationUri { get; set; }

    public string? DeviceVerificationUriComplete { get; set; }

    public int? DeviceIntervalSeconds { get; set; }

    public int? DeviceExpiresInSeconds { get; set; }
}
