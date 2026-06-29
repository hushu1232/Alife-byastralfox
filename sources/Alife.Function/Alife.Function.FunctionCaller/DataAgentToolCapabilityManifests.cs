using System;
using System.Collections.Generic;

namespace Alife.Function.FunctionCaller;

public static class DataAgentToolCapabilityManifests
{
    public static IReadOnlyList<ToolCapabilityManifest> Query => ReadOnly(CreateQuery());

    public static IReadOnlyList<ToolCapabilityManifest> Analysis => ReadOnly(
        CreateAnalysisStart(),
        CreateAnalysisContinue(),
        CreateAnalysisSummarize(),
        CreateAnalysisEnd());

    public static IReadOnlyList<ToolCapabilityManifest> Create()
    {
        ToolCapabilityManifest[] manifests =
        [
            ..Query,
            ..Analysis
        ];

        return Array.AsReadOnly(manifests);
    }

    static ToolCapabilityManifest CreateQuery()
    {
        return new(
            "dataagent_query",
            ToolCapabilityDomain.DataAgent,
            "query",
            ToolCapabilityRisk.Low,
            ReadOnly(ToolCapabilityPrecondition.TrustedRuntime),
            DataAgentSurfaces(),
            ToolStateEffect.ReadsData);
    }

    static ToolCapabilityManifest CreateAnalysisStart()
    {
        return new(
            "dataagent_analysis_start",
            ToolCapabilityDomain.DataAgent,
            "analysis_start",
            ToolCapabilityRisk.Low,
            ReadOnly(ToolCapabilityPrecondition.TrustedRuntime),
            DataAgentSurfaces(),
            ToolStateEffect.AppendsAnalysisTurn);
    }

    static ToolCapabilityManifest CreateAnalysisContinue()
    {
        return new(
            "dataagent_analysis_continue",
            ToolCapabilityDomain.DataAgent,
            "analysis_continue",
            ToolCapabilityRisk.Low,
            ActiveAnalysisPreconditions(),
            DataAgentSurfaces(),
            ToolStateEffect.AppendsAnalysisTurn);
    }

    static ToolCapabilityManifest CreateAnalysisSummarize()
    {
        return new(
            "dataagent_analysis_summarize",
            ToolCapabilityDomain.DataAgent,
            "analysis_summarize",
            ToolCapabilityRisk.Low,
            ActiveAnalysisPreconditions(),
            DataAgentSurfaces(),
            ToolStateEffect.SummarizesAnalysis);
    }

    static ToolCapabilityManifest CreateAnalysisEnd()
    {
        return new(
            "dataagent_analysis_end",
            ToolCapabilityDomain.DataAgent,
            "analysis_end",
            ToolCapabilityRisk.Low,
            ActiveAnalysisPreconditions(),
            DataAgentSurfaces(),
            ToolStateEffect.EndsAnalysis);
    }

    static IReadOnlyList<ToolCapabilityPrecondition> ActiveAnalysisPreconditions()
    {
        return ReadOnly(
            ToolCapabilityPrecondition.ActiveDataAgentAnalysisSession,
            ToolCapabilityPrecondition.TrustedRuntime);
    }

    static IReadOnlyList<ToolCapabilitySurface> DataAgentSurfaces()
    {
        return ReadOnly(
            ToolCapabilitySurface.OwnerPrivate,
            ToolCapabilitySurface.TrustedRuntime);
    }

    static IReadOnlyList<T> ReadOnly<T>(params T[] values) => Array.AsReadOnly(values);
}
