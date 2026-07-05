using System;
using System.Collections.Generic;
using System.Linq;

namespace Alife.Function.FunctionCaller;

public static class AlifeCapabilityGovernanceCatalog
{
    private static readonly IReadOnlyList<AlifeCapabilityGovernanceDescriptor> DefaultCatalog =
    [
        new(
            "QChat",
            ToolCapabilityDomain.QChat,
            AlifeCapabilityGovernanceRole.InteractionSurface,
            AlifeCapabilityOrchestrationKind.None,
            AlifeCapabilityRiskBoundary.OwnerGate,
            "Owner-facing chat interaction surface; delegates capability execution rather than owning workflow orchestration."),
        new(
            "FunctionCaller",
            ToolCapabilityDomain.Chat,
            AlifeCapabilityGovernanceRole.DeterministicService,
            AlifeCapabilityOrchestrationKind.None,
            AlifeCapabilityRiskBoundary.DeterministicSafetyGate,
            "Deterministic function-call routing, tool manifest classification, and capability boundary enforcement."),
        new(
            "DataAgent",
            ToolCapabilityDomain.DataAgent,
            AlifeCapabilityGovernanceRole.AgentWorkflowCandidate,
            AlifeCapabilityOrchestrationKind.NativeWorkflow,
            AlifeCapabilityRiskBoundary.DeterministicSafetyGate,
            "Native QueryPlan workflow candidate with deterministic SQL safety checks retained as the authority boundary."),
        new(
            "Browser",
            ToolCapabilityDomain.Browser,
            AlifeCapabilityGovernanceRole.ExternalBridge,
            AlifeCapabilityOrchestrationKind.FutureLangGraphCandidate,
            AlifeCapabilityRiskBoundary.OwnerGate,
            "Browser bridge for controlled web interaction behind owner-scoped routing gates."),
        new(
            "DesktopControl",
            ToolCapabilityDomain.Desktop,
            AlifeCapabilityGovernanceRole.ExternalBridge,
            AlifeCapabilityOrchestrationKind.FutureLangGraphCandidate,
            AlifeCapabilityRiskBoundary.ApprovalRequired,
            "Desktop automation boundary for local machine actions that require explicit approval."),
        new(
            "Memory",
            ToolCapabilityDomain.Memory,
            AlifeCapabilityGovernanceRole.ContextProvider,
            AlifeCapabilityOrchestrationKind.None,
            AlifeCapabilityRiskBoundary.OwnerGate,
            "Long-lived owner context provider, not a free-form workflow agent."),
        new(
            "Vision",
            ToolCapabilityDomain.Vision,
            AlifeCapabilityGovernanceRole.PerceptionAdapter,
            AlifeCapabilityOrchestrationKind.None,
            AlifeCapabilityRiskBoundary.OwnerGate,
            "Visual perception adapter that supplies observed context without owning orchestration."),
        new(
            "Speech",
            ToolCapabilityDomain.Tts,
            AlifeCapabilityGovernanceRole.PresentationAdapter,
            AlifeCapabilityOrchestrationKind.None,
            AlifeCapabilityRiskBoundary.OwnerGate,
            "Speech presentation adapter for voice output and related owner-gated surfaces."),
        new(
            "Auditory",
            null,
            AlifeCapabilityGovernanceRole.PerceptionAdapter,
            AlifeCapabilityOrchestrationKind.None,
            AlifeCapabilityRiskBoundary.OwnerGate,
            "Auditory perception adapter that contributes signal context without workflow authority."),
        new(
            "DeskPet",
            null,
            AlifeCapabilityGovernanceRole.PresentationAdapter,
            AlifeCapabilityOrchestrationKind.None,
            AlifeCapabilityRiskBoundary.OwnerGate,
            "Desktop companion presentation surface, not an autonomous workflow owner."),
        new(
            "Emotion",
            null,
            AlifeCapabilityGovernanceRole.ContextProvider,
            AlifeCapabilityOrchestrationKind.None,
            AlifeCapabilityRiskBoundary.OwnerGate,
            "Affective state context provider for interaction tone and presentation decisions."),
        new(
            "VirtualWorld",
            null,
            AlifeCapabilityGovernanceRole.PresentationAdapter,
            AlifeCapabilityOrchestrationKind.None,
            AlifeCapabilityRiskBoundary.OwnerGate,
            "Virtual-world presentation adapter with no independent workflow authority."),
        new(
            "Mcp",
            ToolCapabilityDomain.Mcp,
            AlifeCapabilityGovernanceRole.ExternalBridge,
            AlifeCapabilityOrchestrationKind.FutureLangGraphCandidate,
            AlifeCapabilityRiskBoundary.OwnerGate,
            "MCP external bridge constrained by owner-gated tool routing."),
        new(
            "Python",
            null,
            AlifeCapabilityGovernanceRole.ExternalBridge,
            AlifeCapabilityOrchestrationKind.FutureLangGraphCandidate,
            AlifeCapabilityRiskBoundary.ApprovalRequired,
            "Python execution bridge for developer-style automation that requires explicit approval."),
        new(
            "Developer",
            ToolCapabilityDomain.Developer,
            AlifeCapabilityGovernanceRole.ExternalBridge,
            AlifeCapabilityOrchestrationKind.FutureLangGraphCandidate,
            AlifeCapabilityRiskBoundary.ApprovalRequired,
            "Developer tooling bridge for code and environment actions that require explicit approval.")
    ];

    public static IReadOnlyList<AlifeCapabilityGovernanceDescriptor> CreateDefault()
    {
        return Array.AsReadOnly(DefaultCatalog.ToArray());
    }

    public static AlifeCapabilityGovernanceDescriptor? FindByOwner(string owner)
    {
        if (string.IsNullOrWhiteSpace(owner))
        {
            return null;
        }

        return DefaultCatalog.FirstOrDefault(
            item => string.Equals(item.Owner, owner, StringComparison.OrdinalIgnoreCase));
    }
}
