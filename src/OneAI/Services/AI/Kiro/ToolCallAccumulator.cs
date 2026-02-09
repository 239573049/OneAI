using System.Text;

namespace OneAI.Services.AI.Kiro;

public sealed class ToolCallAccumulator
{
    public ToolCallAccumulator(string toolUseId, string name)
    {
        ToolUseId = string.IsNullOrWhiteSpace(toolUseId) ? Guid.NewGuid().ToString("N") : toolUseId;
        Name = name;
    }

    public string ToolUseId { get; }
    public string Name { get; }
    public StringBuilder Input { get; } = new();

    public KiroToolCall ToToolCall()
    {
        var args = Input.ToString();
        return new KiroToolCall(ToolUseId, Name, string.IsNullOrWhiteSpace(args) ? "{}" : args);
    }
}