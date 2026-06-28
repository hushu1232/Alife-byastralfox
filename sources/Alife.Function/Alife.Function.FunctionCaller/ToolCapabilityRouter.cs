using System;
using System.Collections.Generic;
using System.Linq;

namespace Alife.Function.FunctionCaller;

public sealed class ToolCapabilityRouter
{
    static readonly IReadOnlyList<ToolCapabilityPrecondition> TrustedOnlyPreconditions =
    [
        ToolCapabilityPrecondition.TrustedRuntime
    ];

    static readonly IReadOnlyList<ToolCapabilityPrecondition> ActiveAnalysisPreconditions =
    [
        ToolCapabilityPrecondition.ActiveDataAgentAnalysisSession,
        ToolCapabilityPrecondition.TrustedRuntime
    ];

    static readonly IReadOnlyList<ToolCapabilitySurface> DataAgentSurfaces =
    [
        ToolCapabilitySurface.OwnerPrivate,
        ToolCapabilitySurface.TrustedRuntime
    ];

    readonly IReadOnlyList<ToolCapabilityManifest> manifests;

    public ToolCapabilityRouter(IReadOnlyList<ToolCapabilityManifest> manifests)
    {
        this.manifests = manifests is null || manifests.Count == 0
            ? Array.Empty<ToolCapabilityManifest>()
            : Array.AsReadOnly(manifests.ToArray());
    }

    public static ToolCapabilityRouter CreateDefault()
    {
        return new ToolCapabilityRouter(
        [
            new(
                "dataagent_query",
                ToolCapabilityDomain.DataAgent,
                "query",
                ToolCapabilityRisk.Low,
                TrustedOnlyPreconditions,
                DataAgentSurfaces,
                ToolStateEffect.ReadsData),
            new(
                "dataagent_analysis_start",
                ToolCapabilityDomain.DataAgent,
                "analysis_start",
                ToolCapabilityRisk.Low,
                TrustedOnlyPreconditions,
                DataAgentSurfaces,
                ToolStateEffect.AppendsAnalysisTurn),
            new(
                "dataagent_analysis_continue",
                ToolCapabilityDomain.DataAgent,
                "analysis_continue",
                ToolCapabilityRisk.Low,
                ActiveAnalysisPreconditions,
                DataAgentSurfaces,
                ToolStateEffect.AppendsAnalysisTurn),
            new(
                "dataagent_analysis_summarize",
                ToolCapabilityDomain.DataAgent,
                "analysis_summarize",
                ToolCapabilityRisk.Low,
                ActiveAnalysisPreconditions,
                DataAgentSurfaces,
                ToolStateEffect.SummarizesAnalysis),
            new(
                "dataagent_analysis_end",
                ToolCapabilityDomain.DataAgent,
                "analysis_end",
                ToolCapabilityRisk.Low,
                ActiveAnalysisPreconditions,
                DataAgentSurfaces,
                ToolStateEffect.EndsAnalysis)
        ]);
    }

    public ToolRouteDecision Route(string utterance, ToolRouteState state)
    {
        state ??= ToolRouteState.Empty;
        string normalizedUtterance = utterance?.Trim() ?? string.Empty;

        if (LooksLikeDataAgentAnalysis(normalizedUtterance) == false)
        {
            return BuildDecision(
                ToolCapabilityDomain.Chat,
                "ordinary_chat",
                Array.Empty<string>(),
                state,
                "ordinary_chat");
        }

        if (state.IsTrustedRuntime == false)
        {
            return BuildDecision(
                ToolCapabilityDomain.DataAgent,
                "dataagent_analysis",
                Array.Empty<string>(),
                state,
                "route_state_not_trusted",
                denyReason: "route_state_not_trusted");
        }

        if (state.HasActiveDataAgentSession)
        {
            return BuildDecision(
                ToolCapabilityDomain.DataAgent,
                "analysis_continue",
                [
                    "dataagent_query",
                    "dataagent_analysis_continue",
                    "dataagent_analysis_summarize",
                    "dataagent_analysis_end"
                ],
                state,
                "explicit_dataagent_analysis_continue");
        }

        return BuildDecision(
            ToolCapabilityDomain.DataAgent,
            "analysis_start",
            [
                "dataagent_query",
                "dataagent_analysis_start"
            ],
            state,
            "explicit_dataagent_analysis_start");
    }

    ToolRouteDecision BuildDecision(
        ToolCapabilityDomain domain,
        string intent,
        IReadOnlyList<string> allowedToolNames,
        ToolRouteState state,
        string reason,
        string denyReason = "tool_not_allowed_in_current_route")
    {
        HashSet<string> allowedNameSet = new(
            allowedToolNames.Where(name => string.IsNullOrWhiteSpace(name) == false),
            StringComparer.OrdinalIgnoreCase);

        string[] allowedTools = manifests
            .Where(manifest => allowedNameSet.Contains(manifest.Name))
            .Select(manifest => manifest.Name)
            .ToArray();

        ToolRouteDeniedTool[] deniedTools = manifests
            .Where(manifest => allowedNameSet.Contains(manifest.Name) == false)
            .Select(manifest => new ToolRouteDeniedTool(manifest.Name, denyReason))
            .ToArray();

        return new ToolRouteDecision(
            "tool-capability-router-v0",
            domain,
            intent,
            allowedTools,
            deniedTools,
            state,
            reason);
    }

    static bool LooksLikeDataAgentAnalysis(string utterance)
    {
        if (string.IsNullOrWhiteSpace(utterance))
        {
            return false;
        }

        return ContainsOrdinalIgnoreCase(utterance, "dataagent")
            || ContainsOrdinalIgnoreCase(utterance, "data agent")
            || ContainsOrdinalIgnoreCase(utterance, "analysis")
            || ContainsOrdinalIgnoreCase(utterance, "analyze")
            || ContainsOrdinalIgnoreCase(utterance, "analyse")
            || utterance.Contains("分析", StringComparison.Ordinal);
    }

    static bool ContainsOrdinalIgnoreCase(string value, string candidate)
    {
        return value.Contains(candidate, StringComparison.OrdinalIgnoreCase);
    }
}
