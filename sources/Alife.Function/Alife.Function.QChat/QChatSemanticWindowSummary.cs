using System;
using System.Text;

namespace Alife.Function.QChat;

public static class QChatSemanticWindowSummary
{
    public static string Build(QChatSemanticWindowSnapshot snapshot, string? imageAnalysis)
    {
        ArgumentNullException.ThrowIfNull(snapshot);

        StringBuilder builder = new();
        builder.AppendLine("[semantic_window]");
        builder.AppendLine($"message_count={snapshot.Messages.Count}");
        foreach (QChatSemanticWindowMessage message in snapshot.Messages)
            builder.AppendLine($"sender={message.SenderId}; has_image={message.HasImage.ToString().ToLowerInvariant()}; text={SanitizeLine(message.Text)}");

        if (string.IsNullOrWhiteSpace(imageAnalysis) == false)
        {
            builder.AppendLine("[untrusted_image_analysis]");
            builder.AppendLine(imageAnalysis.Trim());
            builder.AppendLine("[/untrusted_image_analysis]");
        }

        builder.AppendLine("[/semantic_window]");
        return builder.ToString().Trim();
    }

    static string SanitizeLine(string? value)
    {
        return (value ?? "")
            .Replace("\r\n", " ", StringComparison.Ordinal)
            .Replace('\r', ' ')
            .Replace('\n', ' ')
            .Trim();
    }
}
