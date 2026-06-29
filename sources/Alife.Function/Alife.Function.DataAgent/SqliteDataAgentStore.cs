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
}
