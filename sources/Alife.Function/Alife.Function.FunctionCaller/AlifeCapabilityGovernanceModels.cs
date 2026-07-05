namespace Alife.Function.FunctionCaller;

public enum AlifeCapabilityGovernanceRole
{
    InteractionSurface,
    AgentWorkflowCandidate,
    DeterministicService,
    ContextProvider,
    PerceptionAdapter,
    PresentationAdapter,
    ExternalBridge
}

public enum AlifeCapabilityOrchestrationKind
{
    None,
    NativeWorkflow,
    FutureLangGraphCandidate
}

public enum AlifeCapabilityRiskBoundary
{
    None,
    OwnerGate,
    ApprovalRequired,
    DeterministicSafetyGate
}

public sealed record AlifeCapabilityGovernanceDescriptor(
    string Owner,
    ToolCapabilityDomain? Domain,
    AlifeCapabilityGovernanceRole Role,
    AlifeCapabilityOrchestrationKind OrchestrationKind,
    AlifeCapabilityRiskBoundary RiskBoundary,
    string Summary);
