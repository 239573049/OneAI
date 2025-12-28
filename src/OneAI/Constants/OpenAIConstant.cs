namespace OneAI.Constants;


/// <summary>
///     OpenAI常量
/// </summary>
public static class OpenAIConstant
{
    /// <summary>
    ///     字符串utf-8编码
    /// </summary>
    /// <returns></returns>
    public const string Done = "[DONE]";

    /// <summary>
    ///     Data: 协议头
    /// </summary>
    public const string Data = "data:";
    
    public const string EventPrefix = "event: ";

    /// <summary>
    ///     think: 协议头
    /// </summary>
    public const string ThinkStart = "<think>";

    /// <summary>
    ///     think: 协议尾
    /// </summary>
    public const string ThinkEnd = "</think>";

    /// <summary>
    /// :
    /// </summary>
    public const string Colon = ":";
    
    public static readonly byte[] DataPrefix = "data: "u8.ToArray();

    public  static readonly byte[] NewLine = "\n"u8.ToArray();

    public  static readonly byte[] DoubleNewLine = "\n\n"u8.ToArray();
}