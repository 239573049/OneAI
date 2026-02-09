namespace OneAI.Services.AI.Kiro;

public
    sealed class KiroImageSource(string? mediaType, string? data)
{
    public string? MediaType { get; } = mediaType;
    public string? Data { get; } = data;
}