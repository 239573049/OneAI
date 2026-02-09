namespace OneAI.Services.AI.Kiro;

public  sealed class KiroMessage(string role, List<KiroMessagePart> parts)
{
    public string Role { get; } = role;
    public List<KiroMessagePart> Parts { get; } = parts;

    public KiroMessage Clone()
    {
        return new KiroMessage(Role, Parts.Select(part => part.Clone()).ToList());
    }
}