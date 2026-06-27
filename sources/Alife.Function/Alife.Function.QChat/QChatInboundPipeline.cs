using System;

namespace Alife.Function.QChat;

public sealed record QChatInboundEnvelope(long BotAccountId, OneBotBasicMessageEvent Message);

public sealed record QChatInboundContext(
    QChatInboundEnvelope Envelope,
    QChatAgentRoute Route,
    QChatAgentProfile Profile,
    string RawText);

public enum QChatInboundDecisionKind
{
    Ignore,
    ListenOnly,
    DispatchToModel,
    ExecuteLocalCommand
}

public sealed record QChatInboundDecision(QChatInboundDecisionKind Kind, string Reason);

public sealed class QChatInboundPipeline(QChatAgentRouteService routes, QChatProfileService profiles)
{
    readonly QChatAgentRouteService routes = routes ?? throw new ArgumentNullException(nameof(routes));
    readonly QChatProfileService profiles = profiles ?? throw new ArgumentNullException(nameof(profiles));

    public QChatInboundContext BuildContext(QChatInboundEnvelope envelope)
    {
        ArgumentNullException.ThrowIfNull(envelope);
        ArgumentNullException.ThrowIfNull(envelope.Message);

        QChatAgentRoute route = routes.Resolve(envelope.BotAccountId, envelope.Message);
        QChatAgentProfile profile = profiles.Get(route);
        string rawText = envelope.Message is OneBotMessageEvent messageEvent
            ? messageEvent.RawMessage?.Trim() ?? string.Empty
            : string.Empty;

        return new QChatInboundContext(envelope, route, profile, rawText);
    }
}
