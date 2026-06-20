using System;

namespace Alife.Function.QChat;

public sealed record QChatDiagnosticsResult(bool Handled, string Text);

public sealed record QChatDiagnosticsRuntimeState(
    bool ReplyTimingDelayEnabled = false,
    bool ConversationSettleWindowEnabled = false);

public static class QChatDiagnosticsService
{
    const string CommandPrefix = "/qchat";

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

        return command.ToLowerInvariant() switch
        {
            "route" => Handled(BuildRouteText(route)),
            "identity" => Handled(BuildIdentityText(route, profile)),
            "profile" => Handled(BuildProfileText(profile)),
            "status" => Handled(BuildStatusText(route, profile, runtimeState)),
            "files" => Handled("files=pending:0 downloaded:0 deleted:0"),
            "approvals" => Handled("approvals=pending:0"),
            "failures" => Handled("failures=0"),
            "recent private" => Handled("recent.private=empty"),
            "recent group" => Handled("recent.group=empty"),
            _ => Handled(BuildHelpText())
        };
    }

    static bool IsQChatCommand(string text)
    {
        if (!text.StartsWith(CommandPrefix, StringComparison.OrdinalIgnoreCase))
            return false;

        return text.Length == CommandPrefix.Length || char.IsWhiteSpace(text[CommandPrefix.Length]);
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
            "status=online");
    }

    static string FormatEnabled(bool value)
    {
        return value ? "enabled" : "disabled";
    }

    static string BuildHelpText()
    {
        return string.Join(Environment.NewLine,
            "Supported commands:",
            "/qchat route - show route/session ids",
            "/qchat identity - show agent identity",
            "/qchat profile - show model/persona/memory",
            "/qchat status - show online and timing state",
            "/qchat timing on|off|status - toggle humanlike reply timing",
            "/qchat memory status - show QChat memory layer wiring",
            "/qchat memory recent - show recent memory events",
            "/qchat memory forget <id> - remove a memory from current context",
            "/qchat memory purge <id> confirm - move a memory archive to trash",
            "/qchat desktop status - read-only desktop status",
            "/qchat desktop capabilities - show enabled read-only desktop capabilities",
            "/qchat desktop processes - read-only process summary",
            "/qchat desktop windows - read-only window summary",
            "/qchat desktop audit recent - show recent desktop action audit entries",
            "/qchat desktop audit health - show desktop action audit safety state",
            "/qchat files - show file task summary",
            "/qchat approvals - show pending approvals",
            "/qchat failures - show failure count",
            "/qchat recent private - show recent private context",
            "/qchat recent group - show recent group context");
    }
}
