using System;
using System.Collections.Generic;
using System.Linq;

namespace Alife.Function.FunctionCaller;

public sealed class ToolCapabilityRouter
{
    const string RouteId = "tool-capability-router-v0";
    const string OrdinaryChatIntent = "ordinary_chat";
    const string OrdinaryChatReason = "ordinary_chat";
    const string ToolNotAllowedReason = "tool_not_allowed_in_current_route";
    const string RouteStateNotTrustedReason = "route_state_not_trusted";
    const string SurfaceNotAllowedReason = "surface_not_allowed";
    const string PreconditionNotMetReason = "precondition_not_met";

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

    static readonly IReadOnlyList<string> StartAnalysisTools =
    [
        "dataagent_query",
        "dataagent_analysis_start"
    ];

    static readonly IReadOnlyList<string> ActiveAnalysisTools =
    [
        "dataagent_query",
        "dataagent_analysis_continue",
        "dataagent_analysis_summarize",
        "dataagent_analysis_end"
    ];

    readonly IReadOnlyList<ToolCapabilityManifest> manifests;

    public ToolCapabilityRouter(IReadOnlyList<ToolCapabilityManifest> manifests)
    {
        if (manifests is null)
        {
            throw new ArgumentNullException(nameof(manifests));
        }

        ToolCapabilityManifest[] manifestCopy = manifests.ToArray();
        if (manifestCopy.Any(manifest => manifest is null))
        {
            throw new ArgumentException("Tool capability manifests cannot contain null entries.", nameof(manifests));
        }

        this.manifests = manifestCopy.Length == 0
            ? Array.Empty<ToolCapabilityManifest>()
            : Array.AsReadOnly(manifestCopy);
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
        bool isDataAgentAnalysis = LooksLikeDataAgentAnalysis(normalizedUtterance);

        if (state.IsTrustedRuntime == false)
        {
            return BuildDecision(
                isDataAgentAnalysis ? ToolCapabilityDomain.DataAgent : ToolCapabilityDomain.Chat,
                isDataAgentAnalysis ? GetDataAgentIntent(state) : OrdinaryChatIntent,
                Array.Empty<string>(),
                state,
                RouteStateNotTrustedReason,
                routeDenyReason: RouteStateNotTrustedReason);
        }

        if (isDataAgentAnalysis == false)
        {
            return BuildDecision(
                ToolCapabilityDomain.Chat,
                OrdinaryChatIntent,
                Array.Empty<string>(),
                state,
                OrdinaryChatReason);
        }

        if (IsOwnerPrivateSurfaceAllowed(state) == false)
        {
            return BuildDecision(
                ToolCapabilityDomain.DataAgent,
                GetDataAgentIntent(state),
                Array.Empty<string>(),
                state,
                SurfaceNotAllowedReason,
                routeDenyReason: SurfaceNotAllowedReason);
        }

        if (state.HasActiveDataAgentSession)
        {
            return BuildDecision(
                ToolCapabilityDomain.DataAgent,
                "analysis_continue",
                ActiveAnalysisTools,
                state,
                "explicit_dataagent_analysis_continue");
        }

        return BuildDecision(
            ToolCapabilityDomain.DataAgent,
            "analysis_start",
            StartAnalysisTools,
            state,
            "explicit_dataagent_analysis_start");
    }

    ToolRouteDecision BuildDecision(
        ToolCapabilityDomain domain,
        string intent,
        IReadOnlyList<string> allowedToolNames,
        ToolRouteState state,
        string reason,
        string? routeDenyReason = null)
    {
        HashSet<string> allowedNameSet = new(
            allowedToolNames.Where(name => string.IsNullOrWhiteSpace(name) == false),
            StringComparer.OrdinalIgnoreCase);

        List<string> allowedTools = [];
        List<ToolRouteDeniedTool> deniedTools = [];

        foreach (ToolCapabilityManifest manifest in manifests)
        {
            if (routeDenyReason is not null)
            {
                deniedTools.Add(new ToolRouteDeniedTool(manifest.Name, routeDenyReason));
                continue;
            }

            if (allowedNameSet.Contains(manifest.Name) == false)
            {
                deniedTools.Add(new ToolRouteDeniedTool(manifest.Name, ToolNotAllowedReason));
                continue;
            }

            string? manifestDenyReason = GetManifestDenyReason(manifest, state);
            if (manifestDenyReason is not null)
            {
                deniedTools.Add(new ToolRouteDeniedTool(manifest.Name, manifestDenyReason));
                continue;
            }

            allowedTools.Add(manifest.Name);
        }

        return new ToolRouteDecision(
            RouteId,
            domain,
            intent,
            allowedTools,
            deniedTools,
            state,
            reason);
    }

    static string? GetManifestDenyReason(ToolCapabilityManifest manifest, ToolRouteState state)
    {
        if (RequiresTrustedRuntime(manifest) && state.IsTrustedRuntime == false)
        {
            return RouteStateNotTrustedReason;
        }

        if (manifest.Surfaces.Contains(ToolCapabilitySurface.OwnerPrivate)
            && IsOwnerPrivateSurfaceAllowed(state) == false)
        {
            return SurfaceNotAllowedReason;
        }

        if (manifest.Preconditions.Contains(ToolCapabilityPrecondition.OwnerIdentity)
            && state.IsOwner == false)
        {
            return SurfaceNotAllowedReason;
        }

        if (manifest.Preconditions.Contains(ToolCapabilityPrecondition.PrivateChat)
            && state.IsPrivateChat == false)
        {
            return SurfaceNotAllowedReason;
        }

        if (manifest.Preconditions.Contains(ToolCapabilityPrecondition.ActiveDataAgentAnalysisSession)
            && state.HasActiveDataAgentSession == false)
        {
            return PreconditionNotMetReason;
        }

        return null;
    }

    static bool RequiresTrustedRuntime(ToolCapabilityManifest manifest)
    {
        return manifest.Surfaces.Contains(ToolCapabilitySurface.TrustedRuntime)
            || manifest.Preconditions.Contains(ToolCapabilityPrecondition.TrustedRuntime);
    }

    static bool IsOwnerPrivateSurfaceAllowed(ToolRouteState state)
    {
        return state.IsOwner && state.IsPrivateChat;
    }

    static string GetDataAgentIntent(ToolRouteState state)
    {
        return state.HasActiveDataAgentSession
            ? "analysis_continue"
            : "analysis_start";
    }

    static bool LooksLikeDataAgentAnalysis(string utterance)
    {
        if (string.IsNullOrWhiteSpace(utterance))
        {
            return false;
        }

        return ContainsOrdinalIgnoreCase(utterance, "dataagent")
            || ContainsOrdinalIgnoreCase(utterance, "data agent")
            || LooksLikeProjectGapAnalysis(utterance);
    }

    static bool LooksLikeProjectGapAnalysis(string utterance)
    {
        return utterance.Contains("分析", StringComparison.Ordinal)
            && (ContainsOrdinalIgnoreCase(utterance, "v2")
                || ContainsOrdinalIgnoreCase(utterance, "v1.5")
                || utterance.Contains("我们离", StringComparison.Ordinal)
                || utterance.Contains("还差什么", StringComparison.Ordinal));
    }

    static bool ContainsOrdinalIgnoreCase(string value, string candidate)
    {
        return value.Contains(candidate, StringComparison.OrdinalIgnoreCase);
    }
}
