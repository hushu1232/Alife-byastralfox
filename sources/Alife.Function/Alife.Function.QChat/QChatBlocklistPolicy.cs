using System;
using System.Linq;

namespace Alife.Function.QChat;

public sealed record QChatBlockContext(
    long UserId,
    long BotId,
    long OwnerId,
    long? GroupId,
    string BlockedPrivateUserIds,
    string BlockedGroupIds,
    bool IsLocallyBlocked);

public sealed record QChatBlockDecision(bool IsBlocked, string Reason);

public static class QChatBlocklistPolicy
{
    public static QChatBlockDecision Evaluate(QChatBlockContext context)
    {
        if (context.UserId == context.OwnerId || context.UserId == context.BotId)
            return new QChatBlockDecision(false, "protected_identity");
        if (ContainsId(context.BlockedPrivateUserIds, context.UserId))
            return new QChatBlockDecision(true, "blocked_private_user");
        if (context.GroupId is > 0 && ContainsId(context.BlockedGroupIds, context.GroupId.Value))
            return new QChatBlockDecision(true, "blocked_group");
        if (context.IsLocallyBlocked)
            return new QChatBlockDecision(true, "risk_local_block");

        return new QChatBlockDecision(false, "allowed");
    }

    static bool ContainsId(string? csv, long id)
    {
        return (csv ?? "")
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Any(item => long.TryParse(item, out long parsed) && parsed == id);
    }
}
