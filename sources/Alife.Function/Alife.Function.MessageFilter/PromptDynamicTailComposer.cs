using System.Text;

namespace Alife.Function.MessageFilter;

public sealed class PromptDynamicTailComposer
{
    public string BuildQqTail(
        string currentMessage,
        string routingHint,
        string memorySnippet,
        string toolResult)
    {
        StringBuilder builder = new();
        builder.AppendLine("[dynamic QQ turn tail]");
        AppendSection(builder, "Routing", routingHint);
        AppendSection(builder, "CurrentMessage", currentMessage);
        AppendSection(builder, "RelevantMemory", memorySnippet);
        AppendSection(builder, "ToolResult", toolResult);
        builder.AppendLine("[/dynamic QQ turn tail]");
        return builder.ToString().Trim();
    }

    static void AppendSection(StringBuilder builder, string name, string value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return;

        builder.AppendLine($"## {name}");
        builder.AppendLine(value.Trim());
    }
}
