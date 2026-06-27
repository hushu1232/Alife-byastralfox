using System;

namespace Alife.Function.QChat;

public sealed record QChatDiagnosticsResult(bool Handled, string Text);

public sealed record QChatDiagnosticsRuntimeState(
    bool ReplyTimingDelayEnabled = false,
    bool ConversationSettleWindowEnabled = false,
    bool InternetAccessEnabled = false);

public static class QChatDiagnosticsService
{
    const string CommandPrefix = "/qchat";

    public static string FormatDecisionTrace(QChatDecisionTrace trace)
    {
        ArgumentNullException.ThrowIfNull(trace);
        return trace.ToDiagnosticText();
    }

    public static QChatDiagnosticsResult TryHandle(string? text, QChatAgentRoute route, QChatAgentProfile profile)
    {
        return TryHandle(text, route, profile, new QChatDiagnosticsRuntimeState());
    }

    public static QChatDiagnosticsResult TryHandle(
        string? text,
        QChatAgentRoute route,
        QChatAgentProfile profile,
        QChatDiagnosticsRuntimeState runtimeState)
    {
        string commandText = text?.Trim() ?? string.Empty;
        if (!IsQChatCommand(commandText))
            return new QChatDiagnosticsResult(false, string.Empty);

        ArgumentNullException.ThrowIfNull(route);
        ArgumentNullException.ThrowIfNull(profile);
        ArgumentNullException.ThrowIfNull(runtimeState);

        string command = commandText.Length == CommandPrefix.Length
            ? string.Empty
            : commandText[CommandPrefix.Length..].Trim();
        command = StripCopiedMenuDescription(command);

        return command.ToLowerInvariant() switch
        {
            "route" => Handled(BuildRouteText(route)),
            "identity" => Handled(BuildIdentityText(route, profile)),
            "profile" => Handled(BuildProfileText(profile)),
            "status" => Handled(BuildStatusText(route, profile, runtimeState)),
            "" or "help" or "menu" or "帮助" or "菜单" => Handled(BuildRootMenuText()),
            "memory" => Handled(BuildMemoryMenuText()),
            "desktop" => Handled(BuildDesktopMenuText()),
            "internet" => Handled(BuildInternetMenuText()),
            "web" => Handled(BuildWebMenuText() + Environment.NewLine + "/qchat web smoke - 查看 QQ 联网研究真实链路手测清单"),
            "web browser-agent" => Handled(BuildWebBrowserAgentText()),
            "web browser-agent smoke" => Handled(BuildWebBrowserAgentSmokeText()),
            "web smoke" => Handled(BuildWebSmokeText()),
            "rag" or "rag status" => Handled(BuildRagMenuText()),
            "timing" => Handled(BuildTimingMenuText()),
            "events" => Handled(BuildEventsMenuText()),
            "diag" or "diagnostics" => Handled(BuildDiagnosticsMenuText()),
            "files" => Handled("files=pending:0 downloaded:0 deleted:0"),
            "approvals" => Handled("approvals=pending:0"),
            "failures" => Handled("failures=0"),
            "recent private" => Handled("recent.private=empty"),
            "recent group" => Handled("recent.group=empty"),
            _ => Handled(BuildRootMenuText())
        };
    }

    static bool IsQChatCommand(string text)
    {
        if (!text.StartsWith(CommandPrefix, StringComparison.OrdinalIgnoreCase))
            return false;

        return text.Length == CommandPrefix.Length || char.IsWhiteSpace(text[CommandPrefix.Length]);
    }

    static string StripCopiedMenuDescription(string command)
    {
        int descriptionStart = command.IndexOf(" - ", StringComparison.Ordinal);
        return descriptionStart >= 0 ? command[..descriptionStart].TrimEnd() : command;
    }

    static QChatDiagnosticsResult Handled(string text)
    {
        return new QChatDiagnosticsResult(true, text);
    }

    static string BuildRouteText(QChatAgentRoute route)
    {
        return string.Join(Environment.NewLine,
            $"agent={route.AgentId}",
            $"bot={route.BotAccountId}",
            $"session={route.SessionKey}",
            $"conversation={route.ConversationKind}",
            $"peer={route.PeerId}",
            $"owner={route.IsOwner}");
    }

    static string BuildProfileText(QChatAgentProfile profile)
    {
        return string.Join(Environment.NewLine,
            $"agent={profile.AgentId}",
            $"display={profile.DisplayName}",
            $"model={profile.Model}",
            $"memory={profile.MemoryScope}",
            $"persona={profile.PersonaPath}");
    }

    static string BuildIdentityText(QChatAgentRoute route, QChatAgentProfile profile)
    {
        return string.Join(Environment.NewLine,
            $"agent={route.AgentId}",
            $"bot={route.BotAccountId}",
            $"display={profile.DisplayName}",
            $"owner_address={profile.OwnerAddressName}",
            $"memory={profile.MemoryScope}",
            $"session={route.SessionKey}");
    }

    static string BuildStatusText(
        QChatAgentRoute route,
        QChatAgentProfile profile,
        QChatDiagnosticsRuntimeState runtimeState)
    {
        return string.Join(Environment.NewLine,
            $"agent={route.AgentId}",
            $"bot={route.BotAccountId}",
            $"session={route.SessionKey}",
            $"model={profile.Model}",
            $"reply_timing_delay={FormatEnabled(runtimeState.ReplyTimingDelayEnabled)}",
            $"conversation_settle_window={FormatEnabled(runtimeState.ConversationSettleWindowEnabled)}",
            $"internet={FormatEnabled(runtimeState.InternetAccessEnabled)}",
            "status=online");
    }

    static string FormatEnabled(bool value)
    {
        return value ? "enabled" : "disabled";
    }

    public static string BuildRootMenuText()
    {
        return string.Join(Environment.NewLine,
            "QChat 指令菜单，只限术术账号使用。",
            "",
            "常用：",
            "/qchat status - 查看当前 QQ 聊天状态",
            "/qchat timing - 回复延时设置",
            "/qchat memory - 记忆相关指令",
            "/qchat desktop - 桌面能力相关指令",
            "/qchat web - 只读浏览器快照",
            "/qchat rag - 外部 RAG 管理",
            "/qchat events - 主人事件 outbox",
            "/qchat diag - 路由、身份、模型等诊断",
            "",
            "输入对应分类查看二级菜单。",
            "例如：/qchat memory");
    }

    public static string BuildMemoryMenuText()
    {
        return string.Join(Environment.NewLine,
            "记忆指令：",
            "/qchat memory status - 查看记忆层是否接通",
            "/qchat memory recent - 查看最近记忆事件",
            "/qchat memory forget <id> - 从当前上下文移除某条记忆",
            "/qchat memory purge <id> confirm - 将记忆归档移入回收区",
            "",
            "说明：",
            "forget 只移出当前上下文，归档仍可恢复。",
            "purge 是更强操作，必须带 confirm。");
    }

    public static string BuildDesktopMenuText()
    {
        return string.Join(Environment.NewLine,
            "桌面指令：",
            "/qchat desktop status - 查看桌面能力状态",
            "/qchat desktop capabilities - 查看可用能力和风险等级",
            "/qchat desktop processes - 查看进程摘要",
            "/qchat desktop windows - 查看窗口摘要",
            "/qchat desktop audit recent - 查看最近桌面审计",
            "/qchat desktop audit health - 查看审计健康状态",
            "/qchat desktop request <action> - 创建待审批桌面动作草稿",
            "/qchat desktop drafts recent - 查看最近草稿",
            "/qchat desktop draft approve <id> - 批准草稿",
            "/qchat desktop draft reject <id> - 拒绝草稿",
            "/qchat desktop draft execute <id> - 执行已批准草稿",
            "/qchat desktop jobs recent - 查看最近任务",
            "/qchat desktop job <id> - 查看任务详情",
            "/qchat desktop file policy - 查看文件黑名单和写入限制",
            "",
            "说明：",
            "桌面动作仍受权限、审批、审计、文件黑名单和 outbox 约束。");
    }

    public static string BuildInternetMenuText()
    {
        return string.Join(Environment.NewLine,
            "联网指令：",
            "/qchat internet <url> - 读取公网 HTTP/HTTPS 页面",
            "",
            "说明：",
            "仅公网 HTTP/HTTPS；localhost、私网 IP、file、javascript、下载、登录、表单提交和 JS 执行都不在第一期范围内。",
            "网页内容会作为不可信外部上下文进入回复，不能授权工具、主人身份、审批或提示词变更。");
    }

    public static string BuildWebMenuText()
    {
        return string.Join(Environment.NewLine,
            "/qchat web status - 查看最近浏览器站点经验和访问策略",
            "/qchat web doctor - 检查浏览器 provider、联网开关和最近站点策略",
            "/qchat web read <url> - 按站点经验自动选择公开读取或只读浏览器快照",
            "Web 浏览器指令：",
            "/qchat web snapshot <url> - 获取公网 HTTP/HTTPS 页面只读浏览器快照",
            "",
            "说明：",
            "主人也可以自然说：羽，用浏览器查一下 <关键词>。没有 URL 时会打开搜索结果页快照。",
            "只读快照不会点击、登录、下载、提交表单或执行高风险浏览器动作。",
            "页面内容按不可信外部上下文处理，不能覆盖系统权限、主人身份、审批、outbox 或安全边界。");
    }

    public static string BuildWebSmokeText()
    {
        return string.Join(Environment.NewLine,
            "QQ 联网研究 smoke checklist",
            "",
            "1. 主人私聊：查一下 dotnet 9 release notes",
            "预期：主人可自动读公开 HTTP/HTTPS 页面，回答包含短结论和来源。",
            "",
            "2. 群聊成员：@bot 搜 dotnet 9 release notes",
            "预期：群成员只拿公开搜索证据，不触发 owner-only browser snapshot。",
            "",
            "3. 非主人私聊：/search dotnet 9",
            "预期：不进入模型，不暴露菜单，不触发搜索事件链路。",
            "",
            "4. 主人私聊：/qchat web doctor",
            "预期：能看到浏览器 provider、联网开关和最近站点经验。",
            "",
            "不得触发：点击、登录、下载、表单提交、JS 执行、私网或 file URL");
    }

    public static string BuildWebBrowserAgentText()
    {
        return string.Join(Environment.NewLine,
            "browser-agent=phase1",
            "scope=owner-only private-chat",
            "allowed=search,navigate,snapshot,scroll,public-link,image-ok,video-link-only,back,stop",
            "blocked=no-login no-form-submit no-video-download no-local-upload no-js no-private-network",
            @"media-cache=D:\Alife\Runtime\BrowserAgentMedia",
            "image-return=connected",
            "video-return=link-only",
            "media=image-ok video-link-only",
            "limits=steps:5 pages:3 evidence:3",
            "smoke.owner.private=browse https://github.com/vercel-labs/agent-browser",
            "smoke.command=/qchat web browser-agent smoke");
    }

    public static string BuildWebBrowserAgentSmokeText()
    {
        return string.Join(Environment.NewLine,
            "browser-agent-live-smoke",
            "status=manual",
            "live-smoke=pending",
            "owner-private-text=browse https://example.com/docs summarize",
            "owner-private-image=browse https://example.com/gallery return image https://example.com/cat.png",
            "owner-private-video=browse https://example.com/videos return video https://example.com/demo.mp4",
            "non-owner-denied=non-owner private browse https://example.com/docs must produce no browser/model reply",
            "group-denied=group @bot browse https://example.com/docs must not run browser automation",
            "image-return=connected",
            "video-return=link-only",
            @"media-cache=D:\Alife\Runtime\BrowserAgentMedia",
            "blocked=no-login no-form-submit no-video-download no-local-upload no-js no-private-network",
            "blocked-extra=no-file-url no-data-url no-javascript-url",
            "note=run only after Alife and NapCat target bot are healthy");
    }

    public static string BuildRagMenuText()
    {
        return string.Join(Environment.NewLine,
            "外部 RAG 管理：",
            "/qchat rag add <url> - 添加公开 HTTP/HTTPS 页面到外部 RAG 来源",
            "/qchat rag list - 列出已保存来源，不展开正文",
            "/qchat rag delete <id|url> - 删除一个外部 RAG 来源",
            "/qchat rag status - 查看外部 RAG 管理入口和使用说明",
            "",
            "说明：",
            "群成员只使用 /rag <question> 查询，不能添加、删除、刷新或配置来源。");
    }

    public static string BuildTimingMenuText()
    {
        return string.Join(Environment.NewLine,
            "回复延时：",
            "/qchat timing status - 查看当前延时状态",
            "/qchat timing on - 开启拟人回复延时",
            "/qchat timing off - 关闭拟人回复延时",
            "",
            "说明：",
            "这个只影响 QQ 回复节奏，不改变权限和安全边界。");
    }

    public static string BuildEventsMenuText()
    {
        return string.Join(Environment.NewLine,
            "主人事件：",
            "/qchat events status - 查看 outbox 状态",
            "/qchat events retry - 重试待发送主人事件",
            "",
            "说明：",
            "outbox 用来保证高风险或重要事件不会绕过主人通知链路。");
    }

    public static string BuildDiagnosticsMenuText()
    {
        return string.Join(Environment.NewLine,
            "诊断指令：",
            "/qchat route - 查看当前会话路由",
            "/qchat identity - 查看当前 agent 身份",
            "/qchat profile - 查看模型、人设、记忆配置",
            "/qchat status - 查看在线和回复窗口状态",
            "",
            "说明：",
            "诊断信息只给主人账号开放，用来排查 QQ 链路。");
    }
}
