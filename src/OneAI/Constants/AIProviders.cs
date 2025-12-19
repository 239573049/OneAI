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
    /// Google PaLM (PaLM 2 等)
    /// </summary>
    public const string PaLM = "PaLM";

    /// <summary>
    /// Cohere
    /// </summary>
    public const string Cohere = "Cohere";

    /// <summary>
    /// Hugging Face
    /// </summary>
    public const string HuggingFace = "HuggingFace";

    /// <summary>
    /// Microsoft Azure OpenAI
    /// </summary>
    public const string AzureOpenAI = "AzureOpenAI";

    /// <summary>
    /// Amazon Bedrock
    /// </summary>
    public const string Bedrock = "Bedrock";

    /// <summary>
    /// Alibaba Cloud 通义千问
    /// </summary>
    public const string Qwen = "Qwen";

    /// <summary>
    /// Baidu 文心一言
    /// </summary>
    public const string Ernie = "Ernie";

    /// <summary>
    /// 讯飞星火
    /// </summary>
    public const string Spark = "Spark";

    /// <summary>
    /// 智谱 ChatGLM
    /// </summary>
    public const string ChatGLM = "ChatGLM";

    /// <summary>
    /// 月之暗面 Moonshot (Kimi)
    /// </summary>
    public const string Moonshot = "Moonshot";

    /// <summary>
    /// MiniMax
    /// </summary>
    public const string MiniMax = "MiniMax";

    /// <summary>
    /// 自定义提供商
    /// </summary>
    public const string Custom = "Custom";

    /// <summary>
    /// 获取所有支持的提供商列表
    /// </summary>
    public static readonly string[] All =
    [
        OpenAI,
        Claude,
        Gemini,
        PaLM,
        Cohere,
        HuggingFace,
        AzureOpenAI,
        Bedrock,
        Qwen,
        Ernie,
        Spark,
        ChatGLM,
        Moonshot,
        MiniMax,
        Custom
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
            PaLM => "Google PaLM",
            Cohere => "Cohere",
            HuggingFace => "Hugging Face",
            AzureOpenAI => "Azure OpenAI",
            Bedrock => "Amazon Bedrock",
            Qwen => "阿里云通义千问",
            Ernie => "百度文心一言",
            Spark => "讯飞星火",
            ChatGLM => "智谱 ChatGLM",
            Moonshot => "月之暗面 (Kimi)",
            MiniMax => "MiniMax",
            Custom => "自定义提供商",
            _ => provider
        };
    }
}
