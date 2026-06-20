using System.Text;

namespace Alife.Function.MessageFilter;

public sealed class PromptStablePrefixService
{
    public string BuildStablePrefix(
        string characterName,
        string characterPrompt,
        string toolProtocol,
        string safetyBoundary)
    {
        StringBuilder builder = new();
        builder.AppendLine("[stable character prefix]");
        builder.AppendLine($"character={characterName.Trim()}");
        builder.AppendLine("## Character");
        builder.AppendLine((characterPrompt ?? "").Trim());
        builder.AppendLine("## Tools");
        builder.AppendLine((toolProtocol ?? "").Trim());
        builder.AppendLine("## Safety");
        builder.AppendLine((safetyBoundary ?? "").Trim());
        builder.AppendLine("[/stable character prefix]");
        return builder.ToString().Trim();
    }
}
