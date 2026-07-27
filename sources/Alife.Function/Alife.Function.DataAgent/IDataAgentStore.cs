namespace Alife.Function.DataAgent;

public interface IDataAgentStore
{
    string ProviderName { get; }
    void Initialize();
    void ImportFixtures();
    DataAgentQueryResult Query(DataAgentCompiledSql compiledSql);
    void RecordAccepted(DataAgentAcceptedAuditInput input);
    void RecordRejected(DataAgentRejectedAuditInput input);
    IReadOnlyList<DataAgentAuditRecord> ReadQueryAudit();
    void RecordToolBrokerAudit(DataAgentToolBrokerAuditRecord record);
    IReadOnlyList<DataAgentToolBrokerAuditRecord> ReadToolBrokerAudit();
    DataAgentLangGraphShadowArtifactWriteResult RecordLangGraphShadowArtifact(
        DataAgentLangGraphShadowArtifact artifact,
        DateTimeOffset now) => throw new NotSupportedException("LangGraph shadow artifact storage is only available for SQLite stores.");
    DataAgentLangGraphShadowArtifactReadResult ReadLangGraphShadowArtifactAggregate(DateTimeOffset now) =>
        throw new NotSupportedException("LangGraph shadow artifact storage is only available for SQLite stores.");
    void RecordQChatConversationTurn(QChatConversationTurn turn) =>
        throw new NotSupportedException("QChat context storage is only available for SQLite stores.");
    int MarkQChatConversationTurnsRecalled(string sourceMessageKey) =>
        throw new NotSupportedException("QChat context storage is only available for SQLite stores.");
    QChatTopicReplayResult SearchQChatTopicReplay(QChatTopicReplayQuery query) =>
        throw new NotSupportedException("QChat context storage is only available for SQLite stores.");
    void RecordQChatRuntimeAudit(QChatRuntimeAuditRecord record) =>
        throw new NotSupportedException("QChat runtime audit storage is only available for SQLite stores.");
    IReadOnlyList<QChatRuntimeAuditRecord> ReadQChatRuntimeAudit(int maxRecords) =>
        throw new NotSupportedException("QChat runtime audit storage is only available for SQLite stores.");
    DataAgentImageAssetRecord? FindImageAssetById(string assetId) =>
        throw new NotSupportedException("Image asset storage is only available for SQLite stores.");
    DataAgentImageAssetRecord? FindImageAssetBySha256(string sha256) =>
        throw new NotSupportedException("Image asset storage is only available for SQLite stores.");
    IReadOnlyList<DataAgentImageAssetMatch> FindSimilarImageAssets(
        string perceptualHash,
        int maxDistance,
        int maxResults) => throw new NotSupportedException("Image asset storage is only available for SQLite stores.");
    void UpsertImageAsset(DataAgentImageAssetRecord record) =>
        throw new NotSupportedException("Image asset storage is only available for SQLite stores.");
    void UpdateImageAssetUnderstanding(
        string assetId,
        string visualSummary,
        string ocrText,
        DateTimeOffset updatedAt) => throw new NotSupportedException("Image asset storage is only available for SQLite stores.");
}

public sealed record DataAgentAcceptedAuditInput(
    string Question,
    string Dataset,
    string QueryPlanJson,
    string GeneratedSql,
    int RowCount,
    TimeSpan Elapsed);

public sealed record DataAgentRejectedAuditInput(
    string Question,
    string Dataset,
    string QueryPlanJson,
    string GeneratedSql,
    string RejectedReason,
    TimeSpan Elapsed);
