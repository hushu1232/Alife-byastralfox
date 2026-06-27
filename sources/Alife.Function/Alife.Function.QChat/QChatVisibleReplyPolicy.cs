using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Text.RegularExpressions;

namespace Alife.Function.QChat;

public sealed record QChatVisibleReplyResult(bool ShouldSend, string Text, string Reason);

public sealed class QChatVisibleReplyPolicy
{
    static readonly string[] DefaultNoReplyReactions =
    [
        "\u3002",
        "\u3002\u3002\u3002",
        "\uFF1F",
        "\u7EF7",
        "\u5567"
    ];

    static readonly string PrivateOwnerLabel = "\u79C1\u804A\u4E3B\u4EBA";
    static readonly string GroupReplyLabel = "\u7FA4\u91CC\u56DE\u590D";

    static readonly string[] InternalStateLinePrefixes =
    [
        "\u5FC3\u7406\u72B6\u6001",
        "\u5185\u5FC3\u72EC\u767D",
        "\u72B6\u6001",
        "\u5185\u5FC3",
        "\u5FC3\u60F3",
        "OS",
        "os"
    ];

    static readonly string[] InternalRuntimeMarkers =
    [
        "[QQ",
        "[QChat",
        "[Internal",
        "[XiaYu state",
        "[/XiaYu state]",
        "[qchat persona frame]",
        "[/qchat persona frame]",
        "[qchat image analysis]",
        "[/qchat image analysis]",
        "/qchat",
        "internal_action=",
        "tool_call=",
        "tool_choice=",
        "function_call=",
        "speaker_role=",
        "recommended_stance=",
        "social_intent=",
        "boundary_pressure=",
        "provider=agnes",
        "image_1_status=",
        "image_1_source=",
        "image_1_summary=",
        "image_1_safety=",
        "image_url=",
        "local_image_path=",
        "Authorization:",
        "Bearer ",
        "AgnesVisionApiKey",
        "qchat-",
        "qchat_",
        "qzone-",
        "route=",
        "session=qq:",
        "managed_file_id=",
        "StopAfterTaskFeedback",
        "TaskFeedbackOnly",
        "NoReply",
        "no-reply"
    ];

    readonly IReadOnlyList<string> noReplyReactions;
    int reactionIndex;

    public QChatVisibleReplyPolicy(IReadOnlyList<string>? noReplyReactions = null)
    {
        this.noReplyReactions = noReplyReactions is { Count: > 0 }
            ? noReplyReactions
            : DefaultNoReplyReactions;
    }

    public QChatVisibleReplyResult Normalize(
        string? modelText,
        QChatConversationKind conversationKind,
        bool shouldReply)
    {
        string selected = SelectConversationSection(modelText ?? string.Empty, conversationKind);
        string sanitized = ContainsInternalRuntimeMarker(selected)
            ? string.Empty
            : QChatVisibleTextPolicy.SanitizeVisibleText(selected);

        if (shouldReply == false && conversationKind == QChatConversationKind.Group)
        {
            return string.IsNullOrEmpty(sanitized)
                ? new QChatVisibleReplyResult(true, NextReaction(), "group no-reply reaction")
                : new QChatVisibleReplyResult(true, sanitized, "group no-reply model visible reaction accepted");
        }

        return string.IsNullOrEmpty(sanitized)
            ? new QChatVisibleReplyResult(false, string.Empty, "empty or unsafe visible text")
            : new QChatVisibleReplyResult(true, sanitized, "visible reply accepted");
    }

    string NextReaction()
    {
        int index = Interlocked.Increment(ref reactionIndex) - 1;
        return noReplyReactions[index % noReplyReactions.Count];
    }

    static string SelectConversationSection(string text, QChatConversationKind kind)
    {
        List<SectionHeader> headers = FindSectionHeaders(text);
        bool hasAnyConversationSection = headers.Count > 0;
        string targetLabel = kind == QChatConversationKind.Private
            ? PrivateOwnerLabel
            : GroupReplyLabel;
        SectionHeader? targetHeader = headers.FirstOrDefault(header => header.Label == targetLabel);

        if (targetHeader is null)
            return hasAnyConversationSection ? string.Empty : text;

        int headerIndex = headers.IndexOf(targetHeader);
        int bodyEnd = headerIndex + 1 < headers.Count
            ? headers[headerIndex + 1].BoundaryStart
            : text.Length;

        return text[targetHeader.BodyStart..bodyEnd].Trim();
    }

    static List<SectionHeader> FindSectionHeaders(string text)
    {
        string labels = $"{Regex.Escape(PrivateOwnerLabel)}|{Regex.Escape(GroupReplyLabel)}";
        string pattern = $"(^|\\s)(?<label>{labels})\\s*[:\uFF1A]";
        List<SectionHeader> headers = [];

        foreach (Match match in Regex.Matches(text, pattern, RegexOptions.CultureInvariant))
        {
            Group labelGroup = match.Groups["label"];
            bool beginsModelOutput = string.IsNullOrWhiteSpace(text[..labelGroup.Index]);
            if (headers.Count == 0 && beginsModelOutput == false)
                continue;

            headers.Add(new SectionHeader(
                labelGroup.Value,
                match.Index,
                match.Index + match.Length));
        }

        return headers;
    }

    static string SanitizeVisibleText(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
            return string.Empty;

        if (ContainsInternalRuntimeMarker(text))
            return string.Empty;

        string withoutInternalLines = RemoveInternalStateLines(text);
        string withoutSelfIdentification = RemoveSelfIdentification(withoutInternalLines);
        string normalized = NormalizeWhitespace(withoutSelfIdentification);

        if (string.IsNullOrWhiteSpace(normalized))
            return string.Empty;

        if (QChatVisibleTextPolicy.IsHumanInvisibleStateText(normalized))
            return string.Empty;

        return IsEffectivelyEmpty(normalized) ? string.Empty : normalized;
    }

    static string RemoveInternalStateLines(string text)
    {
        IEnumerable<string> keptLines = text
            .Replace("\r\n", "\n", StringComparison.Ordinal)
            .Replace('\r', '\n')
            .Split('\n')
            .Where(line => IsUnsafeInternalStateLine(line) == false);

        return string.Join('\n', keptLines);
    }

    static bool IsUnsafeInternalStateLine(string line)
    {
        string trimmed = line.Trim();
        if (trimmed.Length == 0)
            return false;

        if (InternalStateLinePrefixes.Any(prefix => HasInternalStateMarkerShape(trimmed, prefix)))
            return true;

        return QChatVisibleTextPolicy.IsHumanInvisibleStateText(trimmed);
    }

    static bool ContainsInternalRuntimeMarker(string text)
    {
        return InternalRuntimeMarkers.Any(marker => text.Contains(marker, StringComparison.OrdinalIgnoreCase));
    }

    static bool HasInternalStateMarkerShape(string text, string prefix)
    {
        if (text.StartsWith(prefix, StringComparison.OrdinalIgnoreCase) == false)
            return false;

        string rest = text[prefix.Length..].TrimStart();
        return rest.StartsWith(":", StringComparison.Ordinal)
               || rest.StartsWith("\uFF1A", StringComparison.Ordinal);
    }

    static string RemoveSelfIdentification(string text)
    {
        string pattern = "\\u6211\\s*(?:\\u5E76\\s*)?(?:\\u4E0D\\s*)?\\u662F\\s*(?:AI|\\u4EBA\\u5DE5\\u667A\\u80FD|\\u673A\\u5668\\u4EBA|\\u8BED\\u8A00\\u6A21\\u578B|\\u5927\\u6A21\\u578B)";
        return Regex.Replace(text, pattern, string.Empty, RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
    }

    static string NormalizeWhitespace(string text)
    {
        IEnumerable<string> lines = text
            .Replace("\r\n", "\n", StringComparison.Ordinal)
            .Replace('\r', '\n')
            .Split('\n')
            .Select(line => line.Trim())
            .Where(line => line.Length > 0);

        return string.Join('\n', lines).Trim();
    }

    static bool IsEffectivelyEmpty(string text)
    {
        return text.Any(char.IsLetterOrDigit) == false;
    }

    sealed record SectionHeader(string Label, int BoundaryStart, int BodyStart);
}
