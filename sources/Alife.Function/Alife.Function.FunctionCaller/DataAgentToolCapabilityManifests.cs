using System;
using System.Collections.Generic;

namespace Alife.Function.FunctionCaller;

public static class DataAgentToolCapabilityManifests
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

    public static ToolCapabilityManifest Query { get; } = new(
        "dataagent_query",
        ToolCapabilityDomain.DataAgent,
        "query",
        ToolCapabilityRisk.Low,
        TrustedOnlyPreconditions,
        DataAgentSurfaces,
        ToolStateEffect.ReadsData);

    public static IReadOnlyList<ToolCapabilityManifest> Analysis { get; } = Array.AsReadOnly<ToolCapabilityManifest>(
    [
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

    public static IReadOnlyList<ToolCapabilityManifest> Create()
    {
        ToolCapabilityManifest[] manifests =
        [
            Query,
            ..Analysis
        ];

        return Array.AsReadOnly(manifests);
    }
}
