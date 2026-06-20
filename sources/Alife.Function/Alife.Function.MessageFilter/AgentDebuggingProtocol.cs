using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace Alife.Function.MessageFilter;

public sealed record AgentDebugIssuePacket(
    string IssueType,
    string Surface,
    string Goal,
    IReadOnlyList<string> Constraints,
    IReadOnlyList<string> KnownEvidence,
    IReadOnlyList<string> CandidateSubsystems,
    string NextStep);

public sealed record AgentLocationCheck(string Location, string Finding);

public sealed record AgentLocationCandidate(string Location, string Confidence, string Reason);

public sealed record AgentLocationFailureReport(
    string KnownSymptom,
    IReadOnlyList<AgentLocationCheck> Checked,
    IReadOnlyList<AgentLocationCandidate> Candidates,
    IReadOnlyList<string> MissingEvidence,
    string NextLowTokenStep);

public sealed record AgentPersonaContract(
    string PersonaId,
    string OwnerAddress,
    string Audience,
    string Mode,
    string Tone,
    string EngineeringStyle,
    int MaxStyleTokens)
{
    public static AgentPersonaContract XiayuOwnerEngineering { get; } = new(
        "xiayu",
        "术术",
        "owner",
        "engineering",
        "concise-warm",
        "evidence-first, no guessing, verify before claims",
        30);
}

public static class AgentDebuggingProtocol
{
    static readonly string[] DefaultConstraints =
    [
        "token-efficient",
        "persona-aware",
        "evidence-first"
    ];

    public static AgentDebugIssuePacket CreateIssuePacket(string ownerMessage)
    {
        string message = ownerMessage?.Trim() ?? "";
        string issueType = ClassifyIssueType(message);
        string surface = ClassifySurface(message, issueType);
        return new AgentDebugIssuePacket(
            issueType,
            surface,
            BuildGoal(issueType, surface),
            DefaultConstraints,
            ExtractKnownEvidence(message),
            BuildCandidateSubsystems(message, issueType),
            "check debug map and CodeGraph before reading broad files");
    }

    public static string FormatLocationFailureReport(AgentLocationFailureReport report)
    {
        ArgumentNullException.ThrowIfNull(report);

        StringBuilder builder = new();
        builder.AppendLine("Location status: not unique yet.");
        builder.AppendLine("Known symptom:");
        builder.AppendLine($"- {report.KnownSymptom}");
        builder.AppendLine("Checked:");
        AppendItems(builder, report.Checked, item => $"{item.Location}: {item.Finding}");
        builder.AppendLine("Likely candidates:");
        AppendItems(builder, report.Candidates, item => $"{item.Location}: {item.Confidence} confidence; {item.Reason}");
        builder.AppendLine("Missing evidence:");
        AppendItems(builder, report.MissingEvidence, item => item);
        builder.AppendLine("Next low-token step:");
        builder.Append($"- {report.NextLowTokenStep}");
        return builder.ToString();
    }

    public static string FormatHypothesis(
        AgentPersonaContract persona,
        string candidate,
        string evidence,
        string nextStep)
    {
        ArgumentNullException.ThrowIfNull(persona);
        return $"{persona.OwnerAddress}，当前高概率出口是 {candidate}。证据：{evidence}。下一步：{nextStep}。";
    }

    static string ClassifyIssueType(string message)
    {
        if (ContainsAny(message, "[QQ", "qchat-", "qzone-", "route=", "no-reply", "StopAfterTaskFeedback"))
            return "qq-visible-output-leak";
        if (ContainsAny(message, "token", "上下文", "读太多", "消耗"))
            return "token-overuse-during-debugging";
        if (ContainsAny(message, "人设", "语气", "角色"))
            return "persona-fact-boundary-mix";
        if (ContainsAny(message, "位置", "找不到", "不知道哪个文件"))
            return "unknown-code-location";

        return "unknown-code-location";
    }

    static string ClassifySurface(string message, string issueType)
    {
        if (message.Contains("QQ", StringComparison.OrdinalIgnoreCase) ||
            issueType == "qq-visible-output-leak")
        {
            return "qq";
        }

        return "engineering";
    }

    static string BuildGoal(string issueType, string surface)
    {
        return issueType switch
        {
            "qq-visible-output-leak" => $"{surface} visible text must not contain internal labels",
            "token-overuse-during-debugging" => "agent debugging should locate issues with minimal context",
            "persona-fact-boundary-mix" => "persona expression must not alter engineering facts",
            _ => "locate the code path before editing production code"
        };
    }

    static IReadOnlyList<string> ExtractKnownEvidence(string message)
    {
        if (string.IsNullOrWhiteSpace(message))
            return [];

        string compact = message.Length <= 140 ? message : message[..140];
        return [compact.Trim()];
    }

    static IReadOnlyList<string> BuildCandidateSubsystems(string message, string issueType)
    {
        List<string> candidates = [];
        if (message.Contains("QZone", StringComparison.OrdinalIgnoreCase) ||
            message.Contains("Zone", StringComparison.OrdinalIgnoreCase) ||
            issueType == "qq-visible-output-leak")
        {
            candidates.Add("qzone");
        }

        if (message.Contains("QQ", StringComparison.OrdinalIgnoreCase) ||
            issueType == "qq-visible-output-leak")
        {
            candidates.Add("qchat");
        }

        if (candidates.Count == 0)
            candidates.Add("message-filter");

        return candidates.Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
    }

    static bool ContainsAny(string value, params string[] needles)
    {
        return needles.Any(needle => value.Contains(needle, StringComparison.OrdinalIgnoreCase));
    }

    static void AppendItems<T>(StringBuilder builder, IReadOnlyList<T> items, Func<T, string> format)
    {
        if (items.Count == 0)
        {
            builder.AppendLine("- none");
            return;
        }

        foreach (T item in items)
            builder.AppendLine($"- {format(item)}");
    }
}
