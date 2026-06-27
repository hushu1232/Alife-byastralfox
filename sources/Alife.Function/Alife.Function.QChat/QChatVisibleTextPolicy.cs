using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;

namespace Alife.Function.QChat;

internal static class QChatVisibleTextPolicy
{
    static readonly string[] AlwaysHiddenMarkers =
    [
        "\u5FC3\u7406\u72B6\u6001",
        "\u5185\u5FC3\u72EC\u767D",
        "\u5185\u5FC3",
        "\u5FC3\u60F3",
        "OS:",
        "OS\uFF1A",
        "os:",
        "os\uFF1A"
    ];

    static readonly string[] InternalStateLinePrefixes =
    [
        "\u5FC3\u7406\u72B6\u6001",
        "\u5185\u5FC3\u72EC\u767D",
        "\u72B6\u6001",
        "\u5185\u5FC3",
        "\u5FC3\u60F3",
        "\u52A8\u4F5C",
        "\u65C1\u767D",
        "\u7CFB\u7EDF\u5224\u65AD",
        "\u7B56\u7565",
        "OS",
        "os"
    ];

    static readonly string[] StageStateMarkers =
    [
        "\u5B89\u9759",
        "\u5F85\u673A",
        "\u7B49\u5F85",
        "\u89C2\u5BDF",
        "\u65C1\u89C2",
        "\u6C89\u9ED8",
        "\u4E0D\u8BED",
        "\u4E0D\u56DE",
        "\u4E0D\u56DE\u590D",
        "\u4E0D\u56DE\u8986",
        "\u4E0D\u56DE\u5E94",
        "\u4E0D\u56DE\u61C9",
        "\u4E0D\u4F5C",
        "\u4E0D\u505A",
        "\u4E0D\u63D2\u8BDD",
        "\u4E0D\u63D2\u8A71",
        "\u61D2\u5F97\u56DE",
        "\u61D2\u5F97\u7406",
        "\u61D2\u5F97\u7BA1",
        "\u4E0D\u60F3\u7406",
        "\u770B\u7740",
        "\u770B\u8457",
        "\u542C\u7740",
        "\u807D\u8457",
        "\u5F85\u547D",
        "\u5E94\u8BE5\u5B89\u9759",
        "\u4FDD\u6301\u6C89\u9ED8",
        "\u4FDD\u6301\u5B89\u9759"
    ];

    static readonly string[] StageActionMarkers =
    [
        "\u63C9",
        "\u5C3E\u5DF4",
        "\u8033\u6735",
        "\u8DB4",
        "\u8E6D",
        "\u62B1\u4F4F",
        "\u5782\u7738",
        "\u62AC\u5934",
        "\u62AC\u982D",
        "\u4F4E\u5934",
        "\u4F4E\u982D",
        "\u7728\u773C",
        "\u6447\u5934",
        "\u6416\u982D",
        "\u70B9\u5934",
        "\u9EDE\u982D",
        "\u6B6A\u5934",
        "\u6B6A\u982D",
        "\u6505",
        "\u8737",
        "\u8D34\u8FD1",
        "\u8CBC\u8FD1",
        "\u9760\u8FD1",
        "\u7F29\u4E86\u7F29",
        "\u7E2E\u4E86\u7E2E",
        "\u6296\u4E86\u6296",
        "\u53F9\u6C14",
        "\u5606\u6C23",
        "\u51B7\u7B11",
        "\u76B1\u7709"
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
        "internal_action=",
        "tool_call=",
        "tool_choice=",
        "function_call=",
        "speaker_role=",
        "recommended_stance=",
        "social_intent=",
        "boundary_pressure=",
        "owner=true",
        "owner=false",
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
        "qzone-",
        "route=",
        "session=qq:",
        "managed_file_id=",
        "StopAfterTaskFeedback",
        "TaskFeedbackOnly",
        "NoReply",
        "no-reply"
    ];

    static readonly string[] CrossAgentNames =
    [
        "\u771F\u592E",
        "\u54AA\u7EEA",
        "\u96E8\u5BAB\u54AA\u7EEA",
        "\u590F\u7FBD"
    ];

    static readonly (string Start, string End)[] InternalBlockMarkers =
    [
        ("[qchat persona frame]", "[/qchat persona frame]"),
        ("[qchat image analysis]", "[/qchat image analysis]"),
        ("[XiaYu state", "[/XiaYu state]")
    ];

    public static string SanitizeVisibleText(string? message)
    {
        if (string.IsNullOrWhiteSpace(message))
            return string.Empty;

        string withoutBlocks = RemoveInternalBlocks(NormalizeNewlines(message));
        string withoutUnsafeLines = RemoveUnsafeLines(withoutBlocks);
        string withoutStageSegments = RemoveLeadingStageSegments(withoutUnsafeLines);
        string withoutSelfIdentification = RemoveSelfIdentification(withoutStageSegments);
        string normalized = NormalizeWhitespace(withoutSelfIdentification);

        if (string.IsNullOrWhiteSpace(normalized))
            return string.Empty;

        if (IsHumanInvisibleStateText(normalized))
            return string.Empty;

        return IsEffectivelyEmpty(normalized) ? string.Empty : normalized;
    }

    public static bool IsHumanInvisibleStateText(string? message)
    {
        if (string.IsNullOrWhiteSpace(message))
            return false;

        string trimmed = message.Trim();
        if (IsLegalCqCode(trimmed))
            return false;

        string compact = Compact(trimmed);
        if (compact is "\u6C89\u9ED8" or "silent" or "stayquiet" or "noreply")
            return true;

        if (AlwaysHiddenMarkers.Any(marker => compact.Contains(Compact(marker), StringComparison.OrdinalIgnoreCase)))
            return true;

        if (InternalRuntimeMarkers.Any(marker => trimmed.Contains(marker, StringComparison.OrdinalIgnoreCase)))
            return true;

        if (HasInternalStateLineShape(trimmed))
            return true;

        UnwrappedText unwrapped = UnwrapDirective(trimmed);
        if (unwrapped.WasWrapped == false)
            return false;

        string innerCompact = Compact(unwrapped.Text);
        return StageStateMarkers.Any(marker => innerCompact.Contains(Compact(marker), StringComparison.OrdinalIgnoreCase))
               || StageActionMarkers.Any(marker => innerCompact.Contains(Compact(marker), StringComparison.OrdinalIgnoreCase));
    }

    static string RemoveInternalBlocks(string text)
    {
        string current = text;
        foreach ((string start, string end) in InternalBlockMarkers)
        {
            string pattern = $"{Regex.Escape(start)}.*?{Regex.Escape(end)}";
            current = Regex.Replace(
                current,
                pattern,
                string.Empty,
                RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.Singleline);
        }

        return current;
    }

    static string RemoveUnsafeLines(string text)
    {
        IEnumerable<string> keptLines = text
            .Split('\n')
            .Where(line => IsUnsafeInternalStateLine(line) == false);

        return string.Join('\n', keptLines);
    }

    static bool IsUnsafeInternalStateLine(string line)
    {
        string trimmed = line.Trim();
        if (trimmed.Length == 0)
            return false;

        if (IsLegalCqCode(trimmed))
            return false;

        if (InternalRuntimeMarkers.Any(marker => trimmed.Contains(marker, StringComparison.OrdinalIgnoreCase)))
            return true;

        if (IsCrossAgentChatLine(trimmed))
            return true;

        if (HasInternalStateLineShape(trimmed))
            return true;

        return IsHumanInvisibleStateText(trimmed);
    }

    static bool IsCrossAgentChatLine(string text)
    {
        if (text.Contains("<call", StringComparison.OrdinalIgnoreCase))
            return true;

        return CrossAgentNames.Any(name => IsDirectCrossAgentAddress(text, name));
    }

    static bool IsDirectCrossAgentAddress(string text, string name)
    {
        if (text.StartsWith(name, StringComparison.Ordinal) == false)
            return false;

        string rest = text[name.Length..].TrimStart();
        if (rest.Length == 0)
            return false;

        return rest[0] is ',' or '\uFF0C' or ':' or '\uFF1A' or '\u3001' or '.' or '\u3002' or '\u2026' or '!' or '\uFF01' or '?' or '\uFF1F'
               || rest.StartsWith("\u4F60", StringComparison.Ordinal);
    }

    static bool HasInternalStateLineShape(string text)
    {
        return InternalStateLinePrefixes.Any(prefix => HasInternalStateMarkerShape(text, prefix));
    }

    static bool HasInternalStateMarkerShape(string text, string prefix)
    {
        if (text.StartsWith(prefix, StringComparison.OrdinalIgnoreCase) == false)
            return false;

        string rest = text[prefix.Length..].TrimStart();
        return rest.StartsWith(":", StringComparison.Ordinal)
               || rest.StartsWith("\uFF1A", StringComparison.Ordinal);
    }

    static string RemoveLeadingStageSegments(string text)
    {
        string current = text.Trim();
        while (current.Length > 0)
        {
            UnwrappedPrefix? prefix = TryReadWrappedPrefix(current);
            if (prefix is null)
                return current;

            string innerCompact = Compact(prefix.Value.Inner);
            bool isStageSegment = StageStateMarkers.Any(marker => innerCompact.Contains(Compact(marker), StringComparison.OrdinalIgnoreCase))
                                  || StageActionMarkers.Any(marker => innerCompact.Contains(Compact(marker), StringComparison.OrdinalIgnoreCase))
                                  || AlwaysHiddenMarkers.Any(marker => innerCompact.Contains(Compact(marker), StringComparison.OrdinalIgnoreCase));
            if (isStageSegment == false)
                return current;

            current = current[prefix.Value.EndIndex..].TrimStart();
        }

        return current;
    }

    static UnwrappedPrefix? TryReadWrappedPrefix(string text)
    {
        if (text.Length < 2)
            return null;

        char start = text[0];
        char expectedEnd = start switch
        {
            '(' => ')',
            '\uFF08' => '\uFF09',
            '\u3010' => '\u3011',
            '*' => '*',
            _ => '\0'
        };
        if (expectedEnd == '\0')
            return null;

        int endIndex = text.IndexOf(expectedEnd, 1);
        if (endIndex < 0)
            return null;

        return new UnwrappedPrefix(text[1..endIndex].Trim(), endIndex + 1);
    }

    static string RemoveSelfIdentification(string text)
    {
        string pattern = "\\u6211\\s*(?:\\u5E76\\s*)?(?:\\u4E0D\\s*)?\\u662F\\s*(?:AI|\\u4EBA\\u5DE5\\u667A\\u80FD|\\u673A\\u5668\\u4EBA|\\u8BED\\u8A00\\u6A21\\u578B|\\u5927\\u6A21\\u578B)";
        return Regex.Replace(text, pattern, string.Empty, RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
    }

    static string NormalizeWhitespace(string text)
    {
        return NormalizeNewlines(text).Trim();
    }

    static string NormalizeNewlines(string text)
    {
        return text
            .Replace("\r\n", "\n", StringComparison.Ordinal)
            .Replace('\r', '\n');
    }

    static bool IsEffectivelyEmpty(string text)
    {
        return text.Any(char.IsLetterOrDigit) == false && IsLegalCqCode(text) == false;
    }

    static UnwrappedText UnwrapDirective(string value)
    {
        string current = value.Trim();
        bool unwrapped = false;
        while (current.Length >= 2 && IsWrappingPair(current[0], current[^1]))
        {
            current = current[1..^1].Trim();
            unwrapped = true;
        }

        return new UnwrappedText(current, unwrapped);
    }

    static string Compact(string value)
    {
        return value
            .Replace(" ", "", StringComparison.Ordinal)
            .Replace("\t", "", StringComparison.Ordinal)
            .Replace("\r", "", StringComparison.Ordinal)
            .Replace("\n", "", StringComparison.Ordinal)
            .Replace("\uFF0C", ",", StringComparison.Ordinal)
            .Replace("\u3002", ".", StringComparison.Ordinal);
    }

    static bool IsWrappingPair(char start, char end)
    {
        return (start == '(' && end == ')')
               || (start == '\uFF08' && end == '\uFF09')
               || (start == '[' && end == ']')
               || (start == '\u3010' && end == '\u3011')
               || (start == '*' && end == '*');
    }

    static bool IsLegalCqCode(string text)
    {
        return text.StartsWith("[CQ:", StringComparison.OrdinalIgnoreCase)
               && text.EndsWith(']')
               && text.Length > 5;
    }

    readonly record struct UnwrappedText(string Text, bool WasWrapped);
    readonly record struct UnwrappedPrefix(string Inner, int EndIndex);
}
