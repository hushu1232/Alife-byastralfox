using System;
using System.Collections.Generic;
using System.Linq;
using Alife.Function.Agent;

namespace Alife.Function.QChat;

public static class QChatBrowserAgentFormatter
{
    public static string Format(AgentBrowserAutomationResult result)
    {
        ArgumentNullException.ThrowIfNull(result);
        if (result.Success == false)
            return FormatFailure(result.Reason);

        List<string> lines = ["Conclusion: " + Compact(result.Evidence.FirstOrDefault()?.Summary ?? result.Answer, 180)];
        foreach (AgentBrowserEvidence item in result.Evidence.Take(3))
            lines.Add($"- {Compact(item.Title, 40)}: {Compact(item.Summary, 100)}");

        string sources = string.Join(" / ", result.Evidence.Take(3)
            .Select(item => item.Url)
            .Where(url => string.IsNullOrWhiteSpace(url) == false)
            .Distinct());
        if (sources.Length > 0)
            lines.Add("Sources: " + sources);

        return Limit(string.Join(Environment.NewLine, lines), 760);
    }

    public static IReadOnlyList<string> FormatMediaOutputs(IEnumerable<AgentBrowserMediaOutputResult> outputs)
    {
        ArgumentNullException.ThrowIfNull(outputs);

        List<string> messages = [];
        foreach (AgentBrowserMediaOutputResult output in outputs)
        {
            if (output.Success == false)
                continue;

            switch (output.Kind)
            {
                case AgentBrowserMediaOutputKind.Image when string.IsNullOrWhiteSpace(output.LocalPath) == false:
                    messages.Add($"[CQ:image,file={output.LocalPath.Replace('\\', '/')}]");
                    break;
                case AgentBrowserMediaOutputKind.VideoLink when string.IsNullOrWhiteSpace(output.ReturnText) == false:
                    messages.Add("Video: " + output.ReturnText.Trim());
                    break;
            }
        }

        return messages;
    }

    static string FormatFailure(string reason) => reason switch
    {
        "browser_agent_owner_required" => "Browser automation is owner-only.",
        "browser_agent_disabled" => "Browser automation is disabled.",
        "browser_agent_login_required" => "Cannot use that page because it requires login.",
        "browser_agent_anti_bot_challenge" => "Cannot use that page because it shows anti-bot verification.",
        "browser_agent_unsafe_url" => "That browser target is not a safe public URL.",
        "browser_agent_step_limit" => "Browser task stopped at the step limit.",
        "browser_agent_page_limit" => "Browser task stopped at the page limit.",
        "browser_agent_runtime_unavailable" => "Browser runtime is unavailable.",
        "browser_agent_no_reliable_evidence" => "No reliable browser evidence was found.",
        "search_provider_not_configured" => "Public search is not configured.",
        "search_failed" => "Public search failed.",
        "no_public_search_result" => "No public search result was found.",
        _ => "Browser automation failed."
    };

    static string Compact(string? value, int maxChars)
    {
        value = string.Join(" ", (value ?? "").Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));
        return value.Length <= maxChars ? value : value[..maxChars].TrimEnd() + "...";
    }

    static string Limit(string value, int maxChars) =>
        value.Length <= maxChars ? value : value[..maxChars].TrimEnd() + "...";
}
