namespace Alife.Function.DataAgent;

public sealed class SqliteDataAgentStore : IDataAgentStore
{
    readonly string databasePath;

    public SqliteDataAgentStore(string databasePath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(databasePath);

        this.databasePath = databasePath;
    }

    public string ProviderName => "sqlite";

    public void Initialize()
    {
        DataAgentSchemaInitializer.Initialize(databasePath);
    }

    public void ImportFixtures()
    {
        DataAgentFixtureImporter.Import(databasePath);
    }

    public DataAgentQueryResult Query(DataAgentCompiledSql compiledSql)
    {
        return new DataAgentQueryExecutor(databasePath).Execute(compiledSql);
    }

    public void RecordAccepted(DataAgentAcceptedAuditInput input)
    {
        new DataAgentAuditLog(databasePath).RecordAccepted(
            input.Question,
            input.Dataset,
            input.QueryPlanJson,
            input.GeneratedSql,
            input.RowCount,
            input.Elapsed);
    }

    public void RecordRejected(DataAgentRejectedAuditInput input)
    {
        new DataAgentAuditLog(databasePath).RecordRejected(
            input.Question,
            input.Dataset,
            input.QueryPlanJson,
            input.GeneratedSql,
            input.RejectedReason,
            input.Elapsed);
    }

    public IReadOnlyList<DataAgentAuditRecord> ReadQueryAudit()
    {
        return new DataAgentAuditLog(databasePath).ReadAll();
    }

    public void RecordToolBrokerAudit(DataAgentToolBrokerAuditRecord record)
    {
        new DataAgentToolBrokerAuditLog(databasePath).Record(record);
    }

    public IReadOnlyList<DataAgentToolBrokerAuditRecord> ReadToolBrokerAudit()
    {
        return new DataAgentToolBrokerAuditLog(databasePath).ReadAll();
    }

    public DataAgentLangGraphShadowArtifactWriteResult RecordLangGraphShadowArtifact(
        DataAgentLangGraphShadowArtifact artifact,
        DateTimeOffset now)
    {
        return new DataAgentLangGraphShadowArtifactStore(databasePath).Write(artifact, now);
    }

    public DataAgentLangGraphShadowArtifactReadResult ReadLangGraphShadowArtifactAggregate(DateTimeOffset now)
    {
        return new DataAgentLangGraphShadowArtifactStore(databasePath).ReadAggregate(now);
    }

    public void RecordQChatConversationTurn(QChatConversationTurn turn)
    {
        new QChatContextSqliteStore(databasePath).RecordConversationTurn(turn);
    }

    public int MarkQChatConversationTurnsRecalled(string sourceMessageKey)
    {
        return new QChatContextSqliteStore(databasePath).MarkConversationTurnsRecalled(sourceMessageKey);
    }

    public QChatTopicReplayResult SearchQChatTopicReplay(QChatTopicReplayQuery query)
    {
        return new QChatContextSqliteStore(databasePath).SearchTopicReplay(query);
    }

    public void RecordQChatRuntimeAudit(QChatRuntimeAuditRecord record)
    {
        new QChatContextSqliteStore(databasePath).RecordRuntimeAudit(record);
    }

    public IReadOnlyList<QChatRuntimeAuditRecord> ReadQChatRuntimeAudit(int maxRecords)
    {
        return new QChatContextSqliteStore(databasePath).ReadRuntimeAudit(maxRecords);
    }

    public DataAgentImageAssetRecord? FindImageAssetById(string assetId)
    {
        return new DataAgentImageAssetSqliteStore(databasePath).FindById(assetId);
    }

    public DataAgentImageAssetRecord? FindImageAssetBySha256(string sha256)
    {
        return new DataAgentImageAssetSqliteStore(databasePath).FindBySha256(sha256);
    }

    public IReadOnlyList<DataAgentImageAssetMatch> FindSimilarImageAssets(
        string perceptualHash,
        int maxDistance,
        int maxResults)
    {
        return new DataAgentImageAssetSqliteStore(databasePath).FindSimilar(perceptualHash, maxDistance, maxResults);
    }

    public void UpsertImageAsset(DataAgentImageAssetRecord record)
    {
        new DataAgentImageAssetSqliteStore(databasePath).Upsert(record);
    }

    public void UpdateImageAssetUnderstanding(
        string assetId,
        string visualSummary,
        string ocrText,
        DateTimeOffset updatedAt)
    {
        new DataAgentImageAssetSqliteStore(databasePath).UpdateUnderstanding(
            assetId,
            visualSummary,
            ocrText,
            updatedAt);
    }
}
