using SharpToken;

namespace OneAI.Extensions;

public static class TokenExtensions
{
    private static readonly GptEncoding Encoding = GptEncoding.GetEncoding("o200k_base");
    
    public static int GetTokens(this string? str)
    {
        return string.IsNullOrEmpty(str) ? 0 : Encoding.CountTokens(str);
    }
}