using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace Alife.Framework;

public sealed record AgentSelfStateItem(
    string Key,
    string Status,
    string Summary,
    int Priority = 0);

public sealed class AgentSelfStateSnapshot(IReadOnlyList<AgentSelfStateItem> items)
{
    readonly IReadOnlyList<AgentSelfStateItem> items = items;

    public IReadOnlyList<AgentSelfStateItem> Items => items;

    public string FormatCompact(int maxLength)
    {
        if (maxLength <= 0)
            return string.Empty;

        StringBuilder builder = new();
        builder.AppendLine("[Agent self-state]");
        foreach (AgentSelfStateItem item in items
                     .OrderByDescending(item => item.Priority)
                     .ThenBy(item => item.Key, StringComparer.OrdinalIgnoreCase))
        {
            builder.Append("- ");
            builder.Append(item.Key);
            builder.Append(": ");
            builder.Append(item.Status);
            if (string.IsNullOrWhiteSpace(item.Summary) == false)
            {
                builder.Append(" - ");
                builder.Append(item.Summary.Trim());
            }
            builder.AppendLine();
        }

        return TrimTo(builder.ToString().TrimEnd(), maxLength);
    }

    public ContextContribution ToContextContribution(int maxLength = 2048)
    {
        return new ContextContribution(
            Key: "agent.self-state",
            Content: FormatCompact(maxLength),
            Priority: 950,
            MaxLength: maxLength,
            TrustLevel: ContextTrustLevel.Trusted);
    }

    static string TrimTo(string content, int maxLength)
    {
        if (content.Length <= maxLength)
            return content;
        if (maxLength <= 3)
            return content[..maxLength];

        return content[..(maxLength - 3)] + "...";
    }
}
