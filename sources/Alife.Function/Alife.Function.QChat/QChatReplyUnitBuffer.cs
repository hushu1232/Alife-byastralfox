using System;
using System.Collections.Generic;
using System.Linq;

namespace Alife.Function.QChat;

public sealed class QChatReplyUnitBuffer
{
    readonly int maxTextLength;
    readonly QChatReplyLayoutNormalizer layoutNormalizer = new();

    public QChatReplyUnitBuffer() : this(900)
    {
    }

    public QChatReplyUnitBuffer(int maxTextLength)
    {
        this.maxTextLength = Math.Max(1, maxTextLength);
    }

    public IReadOnlyList<string> Commit(string? text)
    {
        string normalized = layoutNormalizer.Normalize(QChatVisibleTextPolicy.SanitizeVisibleText(text));
        if (string.IsNullOrWhiteSpace(normalized))
            return [];

        if (TrySplitIndependentSupplement(normalized, out string primary, out string supplement))
            return [primary, supplement];

        return new QChatOutboundPlanner(maxTextLength)
            .PlanText(normalized)
            .Items
            .Where(item => item.Kind == QChatOutboundItemKind.Text)
            .Select(item => item.Text)
            .ToArray();
    }

    bool TrySplitIndependentSupplement(string normalized, out string primary, out string supplement)
    {
        primary = string.Empty;
        supplement = string.Empty;

        int separator = normalized.IndexOf("\n\n", StringComparison.Ordinal);
        if (separator <= 0 || normalized.IndexOf("\n\n", separator + 2, StringComparison.Ordinal) >= 0)
            return false;

        string candidatePrimary = normalized[..separator].Trim();
        string candidateSupplement = normalized[(separator + 2)..].Trim();
        if (candidatePrimary.Length == 0 || candidateSupplement.Length == 0
            || candidatePrimary.Length > maxTextLength || candidateSupplement.Length > maxTextLength
            || EndsAsCompleteThought(candidatePrimary) == false
            || StartsAsIndependentSupplement(candidateSupplement) == false)
            return false;

        primary = candidatePrimary;
        supplement = candidateSupplement;
        return true;
    }

    static bool EndsAsCompleteThought(string text)
    {
        return text.EndsWith('。') || text.EndsWith('！') || text.EndsWith('？')
            || text.EndsWith('.') || text.EndsWith('!') || text.EndsWith('?');
    }

    static bool StartsAsIndependentSupplement(string text)
    {
        return text.StartsWith("另外", StringComparison.Ordinal)
            || text.StartsWith("还有", StringComparison.Ordinal)
            || text.StartsWith("补充", StringComparison.Ordinal)
            || text.StartsWith("对了", StringComparison.Ordinal)
            || text.StartsWith("顺带", StringComparison.Ordinal);
    }
}
