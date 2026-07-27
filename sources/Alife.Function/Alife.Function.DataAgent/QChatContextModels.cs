namespace Alife.Function.DataAgent;

public enum QChatConversationSpeaker
{
    Self,
    Peer
}

public sealed record QChatConversationTurn(
    string ConversationKey,
    long Sequence,
    QChatConversationSpeaker Speaker,
    string Content,
    DateTimeOffset OccurredAt,
    bool IsRecalled,
    string SourceMessageKey = "");

public sealed record QChatTopicReplayQuery(
    string ConversationKey,
    string QueryText,
    IReadOnlySet<long> ExcludedSequences,
    int MaxTurns,
    int MaxCharacters);

public sealed record QChatTopicReplayResult(
    IReadOnlyList<QChatConversationTurn> Turns,
    bool HasOlderMatches);

public sealed record QChatRuntimeAuditRecord(
    string AgentId,
    string EventKind,
    string Outcome,
    string Summary,
    DateTimeOffset OccurredAt);
