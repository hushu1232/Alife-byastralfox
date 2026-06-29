using System.Collections.Generic;

namespace Alife.Function.FunctionCaller;

public enum ToolCapabilityDomain
{
    Chat,
    DataAgent,
    Browser,
    Vision,
    Tts,
    Memory,
    Desktop,
    QChat,
    Developer,
    Mcp
}

public enum ToolCapabilityRisk
{
    Low,
    Medium,
    High
}

public enum ToolCapabilityPrecondition
{
    None,
    ActiveDataAgentAnalysisSession,
    EndedDataAgentAnalysisSession,
    OwnerIdentity,
    PrivateChat,
    TrustedRuntime,
    LiveRuntimeReady
}

public enum ToolCapabilitySurface
{
    OwnerPrivate,
    OwnerGroup,
    PublicGroup,
    TrustedRuntime,
    LocalHarness
}

public enum ToolStateEffect
{
    None,
    ReadsData,
    AppendsAnalysisTurn,
    SummarizesAnalysis,
    EndsAnalysis,
    SendsExternalMessage,
    ControlsDesktop
}

public sealed record ToolCapabilityManifest(
    string Name,
    ToolCapabilityDomain Domain,
    string Intent,
    ToolCapabilityRisk Risk,
    IReadOnlyList<ToolCapabilityPrecondition> Preconditions,
    IReadOnlyList<ToolCapabilitySurface> Surfaces,
    ToolStateEffect StateEffect)
{
    public ToolCapabilityManifest(
        string name,
        ToolCapabilityDomain domain,
        string intent)
        : this(
            name,
            domain,
            intent,
            ToolCapabilityRisk.Low,
            [ToolCapabilityPrecondition.None],
            [ToolCapabilitySurface.TrustedRuntime],
            ToolStateEffect.None)
    {
    }
}
