namespace Alife.Framework;

public enum EmbodiedCapabilityKind
{
    Body,
    Sense,
    Expression,
    Memory,
    Communication,
    Tool,
    Environment
}

public interface IEmbodiedCapability
{
    string Name { get; }
    EmbodiedCapabilityKind Kind { get; }
    string SelfDescription { get; }
    string? GetCurrentState();
}
