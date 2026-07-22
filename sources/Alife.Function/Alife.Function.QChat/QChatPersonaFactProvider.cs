using System;
using System.Collections.Generic;
using System.Linq;

namespace Alife.Function.QChat;

public enum QChatPersonaFactCategory
{
    Origin,
    Relationship,
    SpeechStyle,
    BehaviorBoundary,
    ConfirmedPreference
}

public sealed class QChatPersonaFactProvider(QChatPersonaMemoryContextProvider memoryProvider)
{
    const string CapabilityName = "persona_fact";
    const int MaximumCharacters = 600;
    readonly QChatPersonaMemoryContextProvider memoryProvider = memoryProvider ?? throw new ArgumentNullException(nameof(memoryProvider));

    public QChatCapabilityFeedback Read(
        QChatAgentIdentity? identity,
        QChatPersonaFactCategory category,
        DateTimeOffset observedAt)
    {
        if (identity == null)
            return QChatCapabilityFeedback.Denied(CapabilityName);

        string? document = memoryProvider.TryReadApprovedProfile(identity);
        if (string.IsNullOrWhiteSpace(document))
            return QChatCapabilityFeedback.NoRelevantData(CapabilityName, observedAt);

        string fact = ExtractSection(document, Keywords(category));
        return string.IsNullOrWhiteSpace(fact)
            ? QChatCapabilityFeedback.NoRelevantData(CapabilityName, observedAt)
            : QChatCapabilityFeedback.Succeeded(CapabilityName, fact, observedAt);
    }

    static string[] Keywords(QChatPersonaFactCategory category) => category switch
    {
        QChatPersonaFactCategory.Origin => ["起源", "经历", "故事", "身份"],
        QChatPersonaFactCategory.Relationship => ["关系", "情感", "主人", "妈妈", "前辈"],
        QChatPersonaFactCategory.SpeechStyle => ["说话", "语言", "口癖", "语感", "风格"],
        QChatPersonaFactCategory.BehaviorBoundary => ["行为", "边界", "权限", "禁止", "规则"],
        QChatPersonaFactCategory.ConfirmedPreference => ["偏好", "喜好", "喜欢", "厌恶"],
        _ => []
    };

    static string ExtractSection(string document, IReadOnlyList<string> keywords)
    {
        string[] lines = document.ReplaceLineEndings("\n").Split('\n');
        int start = Array.FindIndex(lines, line => IsHeading(line) && ContainsKeyword(line, keywords));
        if (start < 0)
            return string.Empty;

        List<string> selected = [];
        int remaining = MaximumCharacters;
        for (int index = start; index < lines.Length; index++)
        {
            if (index > start && IsHeading(lines[index]))
                break;

            string line = lines[index].Trim();
            if (line.Length == 0)
                continue;
            if (line.Length > remaining)
                line = line[..remaining].TrimEnd();
            if (line.Length == 0)
                break;

            selected.Add(line);
            remaining -= line.Length + Environment.NewLine.Length;
            if (remaining <= 0)
                break;
        }

        return string.Join(Environment.NewLine, selected);
    }

    static bool IsHeading(string line)
    {
        string trimmed = line.TrimStart();
        return trimmed.StartsWith('#') || (trimmed.Length > 1 && trimmed[1] == '、');
    }

    static bool ContainsKeyword(string line, IEnumerable<string> keywords) =>
        keywords.Any(keyword => line.Contains(keyword, StringComparison.OrdinalIgnoreCase));
}
