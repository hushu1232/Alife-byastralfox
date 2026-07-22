using System;

namespace Alife.Framework;

public readonly record struct ChatStreamChunkClassification(string VisibleText, string ReasoningText);

public sealed class ChatStreamChunkClassifier(string thinkPrefix)
{
    readonly string thinkPrefix = string.IsNullOrWhiteSpace(thinkPrefix)
        ? throw new ArgumentException("A think prefix is required.", nameof(thinkPrefix))
        : thinkPrefix;
    string pendingPrefix = string.Empty;

    public ChatStreamChunkClassification Push(string? content)
    {
        if (string.IsNullOrEmpty(content))
            return new ChatStreamChunkClassification(string.Empty, string.Empty);

        string pending = pendingPrefix + content;
        pendingPrefix = string.Empty;
        if (thinkPrefix.StartsWith(pending, StringComparison.Ordinal))
        {
            pendingPrefix = pending;
            return new ChatStreamChunkClassification(string.Empty, string.Empty);
        }

        if (pending.StartsWith(thinkPrefix, StringComparison.Ordinal))
            return new ChatStreamChunkClassification(string.Empty, pending[thinkPrefix.Length..]);

        return new ChatStreamChunkClassification(pending, string.Empty);
    }

    public ChatStreamChunkClassification Flush()
    {
        string visible = pendingPrefix;
        pendingPrefix = string.Empty;
        return new ChatStreamChunkClassification(visible, string.Empty);
    }
}
