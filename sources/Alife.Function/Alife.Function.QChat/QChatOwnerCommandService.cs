using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading.Tasks;

namespace Alife.Function.QChat;

public sealed record QChatOwnerCommandContext(
    OneBotMessageEvent MessageEvent,
    QChatSenderRole SenderRole,
    string ReadableMessage);

public delegate Task<bool> QChatOwnerCommandHandler(QChatOwnerCommandContext context);

public sealed class QChatOwnerCommandService(IEnumerable<QChatOwnerCommandHandler> handlers)
{
    readonly IReadOnlyList<QChatOwnerCommandHandler> handlers = [.. handlers];

    public static bool TryParseApprovalCommand(string? text, out string command, out long approvalId)
    {
        command = "";
        approvalId = 0;
        if (string.IsNullOrWhiteSpace(text))
            return false;

        Match match = Regex.Match(
            text.Trim(),
            @"^/(approve|deny)\s+(\d+)$",
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
        if (match.Success == false)
            return false;

        if (long.TryParse(match.Groups[2].Value, out approvalId) == false)
            return false;

        command = match.Groups[1].Value.ToLowerInvariant();
        return true;
    }

    public static bool IsDiagnosticsCommand(string text)
    {
        const string prefix = "/qchat";
        return text.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)
               && (text.Length == prefix.Length || char.IsWhiteSpace(text[prefix.Length]));
    }

    public static bool IsHelpAliasCommand(string text)
    {
        string normalized = text.Trim();
        return normalized.Equals("/help", StringComparison.OrdinalIgnoreCase)
               || normalized.Equals("help", StringComparison.OrdinalIgnoreCase)
               || normalized.Equals("qchat help", StringComparison.OrdinalIgnoreCase)
               || normalized.Equals("qchat \u5e2e\u52a9", StringComparison.OrdinalIgnoreCase)
               || normalized.Equals("qchat\u5e2e\u52a9", StringComparison.OrdinalIgnoreCase)
               || normalized.Equals("\u5e2e\u52a9", StringComparison.Ordinal)
               || normalized.Equals("\u6307\u4ee4", StringComparison.Ordinal)
               || normalized.Equals("\u547d\u4ee4", StringComparison.Ordinal)
               || normalized.Equals("\u83dc\u5355", StringComparison.Ordinal);
    }

    public static bool IsStatusCommand(string text)
    {
        return text.Equals("/status", StringComparison.OrdinalIgnoreCase)
               || text.Equals("/tasks", StringComparison.OrdinalIgnoreCase);
    }

    public static bool IsRecallCommand(string text)
    {
        return QChatIntentClassifier.ClassifyRecall(QChatIntentInput.FromText(text)).IsConfirmed;
    }

    public static async Task<bool> TryHandleDiagnosticsCommandAsync(
        OneBotMessageEvent messageEvent,
        QChatSenderRole senderRole,
        QChatConfig config,
        Func<OneBotMessageType, long, string, Task> sendAsync,
        Action<string, string, object?, Exception?> writeDiagnostic)
    {
        ArgumentNullException.ThrowIfNull(messageEvent);

        string text = OneBotSegment.GetPlainText(messageEvent.RawMessage).Trim();
        bool isDiagnosticsCommand = IsDiagnosticsCommand(text);
        bool isHelpAliasCommand = IsHelpAliasCommand(text);
        if (isDiagnosticsCommand == false && isHelpAliasCommand == false)
            return false;

        ArgumentNullException.ThrowIfNull(config);
        ArgumentNullException.ThrowIfNull(sendAsync);
        ArgumentNullException.ThrowIfNull(writeDiagnostic);

        (OneBotMessageType targetType, long targetId) = GetReplyTarget(messageEvent);
        if (targetId <= 0)
            return true;

        if (senderRole != QChatSenderRole.Owner)
        {
            if (isHelpAliasCommand)
                return false;

            await sendAsync(
                targetType,
                targetId,
                "Only the owner can use QChat diagnostics.");
            writeDiagnostic("qchat-diagnostics-denied", "QChat diagnostics command denied for non-owner sender.", new {
                messageEvent.UserId,
                messageEvent.GroupId,
                command = text
            }, null);
            return true;
        }

        QChatAgentRoute route = BuildQChatDiagnosticsRoute(messageEvent, config);
        QChatAgentProfile profile = ResolveQChatDiagnosticsProfile(route);
        string commandText = isHelpAliasCommand ? "/qchat" : text;
        QChatDiagnosticsResult result = QChatDiagnosticsService.TryHandle(
            commandText,
            route,
            profile,
            new QChatDiagnosticsRuntimeState(
                ReplyTimingDelayEnabled: config.EnableReplyTimingDelay,
                ConversationSettleWindowEnabled: config.EnableConversationSettleWindow));
        if (result.Handled)
        {
            await sendAsync(targetType, targetId, result.Text);
            writeDiagnostic("qchat-diagnostics-command-handled", "QChat diagnostics command handled.", new {
                messageEvent.UserId,
                messageEvent.GroupId,
                command = text,
                route.AgentId,
                route.SessionKey
            }, null);
        }

        return result.Handled;
    }

    public static async Task<bool> TryHandleStatusCommandAsync(
        OneBotMessageEvent messageEvent,
        QChatSenderRole senderRole,
        Func<string> formatStatus,
        Func<OneBotMessageType, long, string, Task> sendAsync,
        Action<string, string, object?, Exception?> writeDiagnostic)
    {
        ArgumentNullException.ThrowIfNull(messageEvent);

        string text = OneBotSegment.GetPlainText(messageEvent.RawMessage).Trim();
        if (IsStatusCommand(text) == false)
            return false;

        ArgumentNullException.ThrowIfNull(formatStatus);
        ArgumentNullException.ThrowIfNull(sendAsync);
        ArgumentNullException.ThrowIfNull(writeDiagnostic);

        (OneBotMessageType targetType, long targetId) = GetReplyTarget(messageEvent);
        if (targetId <= 0)
            return true;

        if (senderRole != QChatSenderRole.Owner)
        {
            await sendAsync(targetType, targetId, "Only the owner can view agent task status.");
            return true;
        }

        string status = formatStatus();
        await sendAsync(targetType, targetId, status);
        writeDiagnostic("agent-status-command", "QQ task status command handled.", new {
            messageEvent.UserId,
            messageEvent.GroupId,
            command = text
        }, null);
        return true;
    }

    public async Task<bool> TryHandleAsync(QChatOwnerCommandContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        foreach (QChatOwnerCommandHandler handler in handlers)
        {
            if (await handler(context))
                return true;
        }

        return false;
    }

    static bool ContainsAny(string text, params string[] values)
    {
        return values.Any(value => text.Contains(value, StringComparison.OrdinalIgnoreCase));
    }

    static (OneBotMessageType Type, long TargetId) GetReplyTarget(OneBotMessageEvent messageEvent)
    {
        OneBotMessageType targetType = messageEvent.MessageType;
        long targetId = targetType == OneBotMessageType.Group
            ? messageEvent.GroupId
            : messageEvent.UserId;

        return (targetType, targetId);
    }

    static QChatAgentRoute BuildQChatDiagnosticsRoute(OneBotMessageEvent messageEvent, QChatConfig config)
    {
        long botAccountId = messageEvent.SelfId > 0
            ? messageEvent.SelfId
            : config.BotId;
        QChatAgentRouteService routeService = new(new QChatAgentRouteConfig
        {
            OwnerUserId = config.OwnerId,
        });

        return routeService.Resolve(botAccountId, messageEvent);
    }

    static QChatAgentProfile ResolveQChatDiagnosticsProfile(QChatAgentRoute route)
    {
        try
        {
            return QChatProfileService.CreateDefault().Get(route);
        }
        catch (InvalidOperationException)
        {
            return new QChatAgentProfile(
                route.AgentId,
                route.AgentId,
                string.Empty,
                $"qchat/{route.AgentId}",
                "unknown",
                string.Empty,
                [],
                new QChatAgentCapabilities(
                    AllowComputerFileTools: false,
                    AllowProjectModification: false,
                    AllowRecall: false,
                    AllowPoke: false));
        }
    }
}
