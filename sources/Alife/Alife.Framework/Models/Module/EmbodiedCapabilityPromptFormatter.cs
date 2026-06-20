using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace Alife.Framework;

public static class EmbodiedCapabilityPromptFormatter
{
    public static string Format(IEnumerable<IEmbodiedCapability> capabilities, Action<Exception>? logStateError = null)
    {
        StringBuilder builder = new();
        builder.AppendLine("You are not an isolated text model. You are a digital life running inside Alife.");
        builder.AppendLine("The capabilities below are your current body, senses, expression channels, memory, communication channels, and action capabilities.");
        builder.AppendLine("They are not an external menu; they are abilities you can naturally use in this activity.");
        builder.AppendLine();
        builder.AppendLine("When using these abilities:");
        builder.AppendLine("- Decide naturally from context whether to observe, remember, speak, act, or stay silent.");
        builder.AppendLine("- Do not list module names or internal implementation details to the user.");
        builder.AppendLine("- Do not say \"I can use module X\"; understand these capabilities as your own body, senses, and actions.");
        builder.AppendLine("- When an ability must actually be executed, still use the XML tags described by that capability.");

        foreach (EmbodiedCapabilityKind kind in Enum.GetValues<EmbodiedCapabilityKind>())
        {
            List<IEmbodiedCapability> group = capabilities.Where(c => c.Kind == kind).ToList();
            if (group.Count == 0)
                continue;

            builder.AppendLine();
            builder.AppendLine($"## {GetGroupTitle(kind)}");
            foreach (IEmbodiedCapability capability in group.OrderBy(c => c.Name, StringComparer.OrdinalIgnoreCase))
            {
                builder.AppendLine($"- {capability.Name}: {capability.SelfDescription}");
                try
                {
                    string? state = capability.GetCurrentState();
                    if (string.IsNullOrWhiteSpace(state) == false)
                        builder.AppendLine($"  Current state: {state}");
                }
                catch (Exception ex)
                {
                    logStateError?.Invoke(ex);
                }
            }
        }

        return builder.ToString().TrimEnd();
    }

    static string GetGroupTitle(EmbodiedCapabilityKind kind)
    {
        return kind switch
        {
            EmbodiedCapabilityKind.Body => "Body",
            EmbodiedCapabilityKind.Sense => "Senses",
            EmbodiedCapabilityKind.Expression => "Expression",
            EmbodiedCapabilityKind.Memory => "Memory",
            EmbodiedCapabilityKind.Communication => "Communication",
            EmbodiedCapabilityKind.Tool => "Action Tools",
            EmbodiedCapabilityKind.Environment => "Environment",
            _ => kind.ToString()
        };
    }
}
