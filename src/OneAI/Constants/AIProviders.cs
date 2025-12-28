namespace OneAI.Constants;

/// <summary>
/// AI 提供商常量
/// </summary>
public static class AIProviders
{
    /// <summary>
    /// OpenAI (ChatGPT, GPT-4, GPT-3.5 等)
    /// </summary>
    public const string OpenAI = "OpenAI";

    /// <summary>
    /// Anthropic Claude (Claude 3 Opus, Sonnet, Haiku 等)
    /// </summary>
    public const string Claude = "Claude";

    /// <summary>
    /// Google Gemini (Gemini Pro, Ultra 等)
    /// </summary>
    public const string Gemini = "Gemini";

    /// <summary>
    /// Google Gemini Antigravity（Google Code Assist / Antigravity API）
    /// </summary>
    public const string GeminiAntigravity = "Gemini-Antigravity";

    /// <summary>
    /// 获取所有支持的提供商列表
    /// </summary>
    public static readonly string[] All =
    [
        OpenAI,
        Claude,
        Gemini,
        GeminiAntigravity
    ];

    /// <summary>
    /// 判断是否为有效的提供商
    /// </summary>
    public static bool IsValid(string provider)
    {
        return All.Contains(provider);
    }

    /// <summary>
    /// 获取提供商的显示名称
    /// </summary>
    public static string GetDisplayName(string provider)
    {
        return provider switch
        {
            OpenAI => "OpenAI (ChatGPT)",
            Claude => "Anthropic Claude",
            Gemini => "Google Gemini",
            GeminiAntigravity => "Google Gemini (Antigravity)",
            _ => provider
        };
    }
}
