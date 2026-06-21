namespace Alife.Function.QChat;

public enum QChatEventRouteKind
{
    PrivateMessage,
    GroupMessage,
    OwnerCommand,
    IntentCommandCandidate,
    NoticeEvent,
    RequestEvent,
    Unsupported
}

public sealed record QChatEventRoute(
    QChatEventRouteKind Kind,
    OneBotMessageType? MessageType,
    QChatIntentKind IntentKind,
    bool IntentConfirmed,
    string? CommandText,
    string Reason);

public static class QChatEventRouter
{
    public static QChatEventRoute Route(OneBotBaseEvent oneBotEvent, QChatSenderRole senderRole)
    {
        return oneBotEvent switch
        {
            OneBotMessageEvent messageEvent => RouteMessage(messageEvent, senderRole),
            OneBotNoticeEvent noticeEvent => new QChatEventRoute(
                QChatEventRouteKind.NoticeEvent,
                noticeEvent.MessageType,
                QChatIntentKind.None,
                false,
                null,
                "notice event"),
            OneBotRequestEvent => new QChatEventRoute(
                QChatEventRouteKind.RequestEvent,
                null,
                QChatIntentKind.None,
                false,
                null,
                "request event"),
            _ => new QChatEventRoute(
                QChatEventRouteKind.Unsupported,
                null,
                QChatIntentKind.None,
                false,
                null,
                "unsupported event")
        };
    }

    static QChatEventRoute RouteMessage(OneBotMessageEvent messageEvent, QChatSenderRole senderRole)
    {
        string plainText = OneBotSegment.GetPlainText(messageEvent.RawMessage).Trim();
        if (senderRole == QChatSenderRole.Owner && IsOwnerCommand(plainText))
        {
            return new QChatEventRoute(
                QChatEventRouteKind.OwnerCommand,
                messageEvent.MessageType,
                QChatIntentKind.None,
                false,
                plainText,
                "owner command");
        }

        QChatIntentDecision intent = ClassifyFirstIntentCandidate(messageEvent, plainText);
        if (intent.IsCandidate)
        {
            return new QChatEventRoute(
                QChatEventRouteKind.IntentCommandCandidate,
                messageEvent.MessageType,
                intent.Kind,
                intent.IsConfirmed,
                null,
                "intent candidate");
        }

        return new QChatEventRoute(
            messageEvent.MessageType == OneBotMessageType.Private
                ? QChatEventRouteKind.PrivateMessage
                : QChatEventRouteKind.GroupMessage,
            messageEvent.MessageType,
            QChatIntentKind.None,
            false,
            null,
            messageEvent.MessageType == OneBotMessageType.Private ? "private message" : "group message");
    }

    static bool IsOwnerCommand(string plainText)
    {
        return QChatOwnerCommandService.IsDiagnosticsCommand(plainText) ||
               QChatOwnerCommandService.IsHelpAliasCommand(plainText) ||
               QChatOwnerCommandService.IsStatusCommand(plainText) ||
               QChatOwnerCommandService.TryParseApprovalCommand(plainText, out _, out _);
    }

    static QChatIntentDecision ClassifyFirstIntentCandidate(OneBotMessageEvent messageEvent, string plainText)
    {
        QChatIntentInput input = new(
            plainText,
            plainText,
            messageEvent.RawMessage,
            HasReply(messageEvent.RawMessage),
            null);

        QChatIntentDecision recall = QChatIntentClassifier.ClassifyRecall(input);
        if (recall.IsCandidate)
            return recall;

        QChatIntentDecision quiet = QChatIntentClassifier.ClassifyQuietMode(input);
        if (quiet.IsCandidate)
            return quiet;

        QChatIntentDecision fileUpload = QChatIntentClassifier.ClassifyFileUpload(input);
        if (fileUpload.IsCandidate)
            return fileUpload;

        return QChatIntentClassifier.ClassifyAllowlist(input, messageEvent.GroupId);
    }

    static bool HasReply(string rawMessage)
    {
        return rawMessage.Contains("[CQ:reply", System.StringComparison.OrdinalIgnoreCase);
    }
}
