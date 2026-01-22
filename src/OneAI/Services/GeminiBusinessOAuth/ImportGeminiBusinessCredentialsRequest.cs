namespace OneAI.Services.GeminiBusinessOAuth;

/// <summary>
/// Import Gemini Business credentials request
/// </summary>
public record ImportGeminiBusinessCredentialsRequest(
    string Credentials,
    string? AccountName = null,
    string? Email = null
);

