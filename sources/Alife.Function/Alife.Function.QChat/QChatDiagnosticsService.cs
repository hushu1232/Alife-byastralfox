using System;

namespace Alife.Function.QChat;

public sealed record QChatDiagnosticsResult(bool Handled, string Text);

public static class QChatDiagnosticsService
{
    const string CommandPrefix = "/qchat";

    public static QChatDiagnosticsResult TryHandle(string? text, QChatAgentRoute route, QChatAgentProfile profile)
    {
        string commandText = text?.Trim() ?? string.Empty;
        if (!IsQChatCommand(commandText))
            return new QChatDiagnosticsResult(false, string.Empty);

        ArgumentNullException.ThrowIfNull(route);
        ArgumentNullException.ThrowIfNull(profile);

        string command = commandText.Length == CommandPrefix.Length
            ? string.Empty
            : commandText[CommandPrefix.Length..].Trim();

        return command.ToLowerInvariant() switch
        {
            "route" => Handled(BuildRouteText(route)),
            "identity" => Handled(BuildIdentityText(route, profile)),
            "profile" => Handled(BuildProfileText(profile)),
            "status" => Handled(BuildStatusText(route, profile)),
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

    static string BuildStatusText(QChatAgentRoute route, QChatAgentProfile profile)
    {
        return string.Join(Environment.NewLine,
            $"agent={route.AgentId}",
            $"session={route.SessionKey}",
            $"model={profile.Model}",
            "status=online");
    }

    static string BuildHelpText()
    {
        return string.Join(Environment.NewLine,
            "Supported commands:",
            "/qchat route",
            "/qchat identity",
            "/qchat profile",
            "/qchat status",
            "/qchat files",
            "/qchat approvals",
            "/qchat failures",
            "/qchat recent private",
            "/qchat recent group");
    }
}
