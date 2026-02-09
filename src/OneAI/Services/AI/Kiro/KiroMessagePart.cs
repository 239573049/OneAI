using System.Text.Json.Nodes;

namespace OneAI.Services.AI.Kiro;

public sealed class KiroMessagePart
{
    public KiroMessagePart()
    {
    }

    public KiroMessagePart(string type)
    {
        Type = type;
    }

    public string Type { get; }
    public string? Text { get; init; }
    public string? ToolUseId { get; init; }
    public string? Name { get; init; }
    public JsonNode? Input { get; init; }
    public string? Content { get; init; }
    public KiroImageSource? Source { get; init; }

    public KiroCachePoint? CachePoint { get; set; }

    public KiroMessagePart Clone()
    {
        return new KiroMessagePart(Type)
        {
            Text = Text,
            ToolUseId = ToolUseId,
            Name = Name,
            Input = Input?.DeepClone(),
            Content = Content,
            Source = Source,
            CachePoint = CachePoint == null ? null : new KiroCachePoint { Type = CachePoint.Type }
        };
    }
}