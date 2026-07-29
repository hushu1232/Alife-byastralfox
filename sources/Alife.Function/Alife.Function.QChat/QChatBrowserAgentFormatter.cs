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

        string summary = Compact(result.Evidence.FirstOrDefault()?.Summary ?? result.Answer, 360);
        List<string> lines =
        [
            summary.Length == 0
                ? "网页已经打开，但没有读取到可用正文。"
                : "主要内容：" + summary
        ];

        string sources = string.Join(" / ", result.Evidence.Take(3)
            .Select(item => AgentBrowserSnapshotFormatter.FormatSourceUrl(item.Url))
            .Where(url => string.IsNullOrWhiteSpace(url) == false)
            .Distinct());
        if (sources.Length > 0)
            lines.Add("来源：" + sources);

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
                    messages.Add("视频链接：" + output.ReturnText.Trim());
                    break;
            }
        }

        return messages;
    }

    static string FormatFailure(string reason) => reason switch
    {
        "browser_agent_owner_required" => "这项浏览器操作只接受主人账号。",
        "browser_agent_disabled" => "浏览器自动化现在没有开启。",
        "browser_agent_login_required" => "这个页面需要登录，我没有继续。",
        "browser_agent_anti_bot_challenge" => "页面出现了反爬验证，我没有继续。",
        "browser_agent_unsafe_url" => "这个地址不是安全的公网网页，我没有打开。",
        "browser_agent_step_limit" => "我已经停在安全的步骤限制内。",
        "browser_agent_page_limit" => "我已经停在安全的页面限制内。",
        "browser_agent_runtime_unavailable" => "浏览器暂时不可用，请稍后再试。",
        "browser_agent_no_reliable_evidence" => "网页打开了，但没有找到可靠内容。",
        "search_provider_not_configured" => "公开搜索还没有配置好。",
        "search_failed" => "公开搜索暂时失败，请稍后再试。",
        "no_public_search_result" => "没有找到可用的公开搜索结果。",
        _ => "这次浏览器任务没有完成。"
    };

    static string Compact(string? value, int maxChars)
    {
        value = string.Join(" ", (value ?? "").Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));
        return value.Length <= maxChars ? value : value[..maxChars].TrimEnd() + "...";
    }

    static string Limit(string value, int maxChars) =>
        value.Length <= maxChars ? value : value[..maxChars].TrimEnd() + "...";
}
