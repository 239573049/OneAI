namespace OneAI.Services.KiroOAuth;

/// <summary>
/// Import Kiro credentials request
/// </summary>
public record ImportKiroCredentialsRequest(
    string Credentials,
    string? AccountName = null,
    string? Email = null
);
