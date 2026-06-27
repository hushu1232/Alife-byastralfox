namespace Alife.Function.QChat;

public static class QChatRiskOwnerNotifier
{
    public static string FormatLocalBlockReport(QChatRiskUserState state, int threshold = 120)
    {
        return $"""
                action=local_block
                agent={state.AgentId}
                bot={state.BotId}
                user_id={state.UserId}
                risk_score={state.Score}
                threshold={threshold}
                reason={string.Join(';', state.Reasons)}
                events={state.EventCount}
                first_seen={state.FirstSeenAt:O}
                last_seen={state.LastSeenAt:O}
                effect=ignore_private_messages
                """;
    }

    public static string FormatFriendDeleteReport(QChatRiskUserState state, QChatFriendDeleteResult result, int threshold = 160)
    {
        string status = result.Succeeded ? "success" : "failed";
        return $"""
                action=delete_friend
                result={status}
                agent={state.AgentId}
                bot={state.BotId}
                user_id={state.UserId}
                risk_score={state.Score}
                threshold={threshold}
                reason={string.Join(';', state.Reasons)}
                events={state.EventCount}
                first_seen={state.FirstSeenAt:O}
                last_seen={state.LastSeenAt:O}
                gateway={result.Message}
                effect=remove_qq_friend_if_success
                """;
    }
}
