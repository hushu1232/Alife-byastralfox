using System;
using System.Collections.Generic;
using System.Text;

namespace Alife.Function.QChat;

public sealed class QChatOutboundPlanner(int maxTextLength = 900)
{
    readonly int maxTextLength = Math.Max(1, maxTextLength);

    public QChatOutboundMessagePlan PlanText(string? text)
    {
        string normalized = NormalizeText(QChatVisibleTextPolicy.SanitizeVisibleText(text));
        if (string.IsNullOrWhiteSpace(normalized))
            return new QChatOutboundMessagePlan([]);

        IReadOnlyList<string> blocks = SplitIntoSafeBlocks(normalized.Trim());
        if (blocks.Count == 0)
            return new QChatOutboundMessagePlan([]);

        List<QChatOutboundMessageItem> items = [];
        StringBuilder current = new();

        foreach (string block in blocks)
        {
            if (current.Length == 0)
            {
                current.Append(block);
                continue;
            }

            string candidate = $"{current}\n\n{block}";
            if (candidate.Length <= maxTextLength)
            {
                current.Clear();
                current.Append(candidate);
                continue;
            }

            AddTextItem(items, current.ToString());
            current.Clear();
            current.Append(block);
        }

        AddTextItem(items, current.ToString());
        return new QChatOutboundMessagePlan(items);
    }

    static void AddTextItem(List<QChatOutboundMessageItem> items, string text)
    {
        string trimmed = text.Trim();
        if (trimmed.Length == 0)
            return;

        items.Add(new QChatOutboundMessageItem(QChatOutboundItemKind.Text, trimmed));
    }

    static string NormalizeText(string? text)
    {
        return text?
            .Replace("\r\n", "\n", StringComparison.Ordinal)
            .Replace('\r', '\n') ?? string.Empty;
    }

    static IReadOnlyList<string> SplitIntoSafeBlocks(string text)
    {
        List<string> blocks = [];
        StringBuilder current = new();
        bool inCodeBlock = false;
        bool inListBlock = false;

        string[] lines = text.Split('\n');
        for (int index = 0; index < lines.Length; index++)
        {
            string line = lines[index];
            bool isFence = line.TrimStart().StartsWith("```", StringComparison.Ordinal);
            bool isBlank = string.IsNullOrWhiteSpace(line);
            bool isListMarker = IsListMarker(line);
            bool isIndentedContinuation = IsIndentedContinuation(line);

            if (isBlank && inCodeBlock == false)
            {
                if (inListBlock && HasListContinuation(lines, index + 1))
                {
                    AppendLine(current, line);
                    continue;
                }

                AddBlock(blocks, current);
                inListBlock = false;
                continue;
            }

            if (inCodeBlock == false)
            {
                if (isListMarker && inListBlock == false)
                {
                    AddBlock(blocks, current);
                    inListBlock = true;
                }
                else if (inListBlock && isListMarker == false && isIndentedContinuation == false)
                {
                    AddBlock(blocks, current);
                    inListBlock = false;
                }
            }

            AppendLine(current, line);

            if (isFence)
                inCodeBlock = inCodeBlock == false;
        }

        AddBlock(blocks, current);
        return blocks;
    }

    static void AppendLine(StringBuilder current, string line)
    {
        if (current.Length > 0)
            current.Append('\n');

        current.Append(line);
    }

    static bool HasListContinuation(string[] lines, int startIndex)
    {
        for (int index = startIndex; index < lines.Length; index++)
        {
            string line = lines[index];
            if (string.IsNullOrWhiteSpace(line))
                continue;

            return IsListMarker(line) || IsIndentedContinuation(line);
        }

        return false;
    }

    static bool IsIndentedContinuation(string line)
    {
        return line.Length > 0 && (line[0] == ' ' || line[0] == '\t');
    }

    static bool IsListMarker(string line)
    {
        string trimmed = line.TrimStart();
        if (trimmed.StartsWith("- ", StringComparison.Ordinal)
            || trimmed.StartsWith("* ", StringComparison.Ordinal)
            || trimmed.StartsWith("+ ", StringComparison.Ordinal))
            return true;

        int markerIndex = 0;
        while (markerIndex < trimmed.Length && char.IsDigit(trimmed[markerIndex]))
            markerIndex++;

        return markerIndex > 0
            && markerIndex + 1 < trimmed.Length
            && (trimmed[markerIndex] == '.' || trimmed[markerIndex] == ')')
            && trimmed[markerIndex + 1] == ' ';
    }

    static void AddBlock(List<string> blocks, StringBuilder current)
    {
        string block = current.ToString().Trim();
        current.Clear();

        if (block.Length > 0)
            blocks.Add(block);
    }
}
