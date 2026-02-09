namespace OneAI.Services.AI.Kiro;

public sealed record KiroToolUseEvent(string Name, string ToolUseId, string? Input, bool Stop);