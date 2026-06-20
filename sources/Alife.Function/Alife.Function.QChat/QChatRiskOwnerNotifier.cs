namespace Alife.Function.QChat;

public static class QChatRiskOwnerNotifier
{
    public static string FormatLocalBlockReport(QChatRiskUserState state)
    {
        return $"""
                action=local_block
                agent={state.AgentId}
                bot={state.BotId}
                user_id={state.UserId}
                risk_score={state.Score}
                threshold=120
                reason={string.Join(';', state.Reasons)}
                events={state.EventCount}
                first_seen={state.FirstSeenAt:O}
                last_seen={state.LastSeenAt:O}
                effect=ignore_private_messages
                """;
    }
}
