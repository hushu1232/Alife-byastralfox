namespace Alife.Function.DataAgent;

public sealed class PostgresDataAgentStore : IDataAgentStore
{
    public PostgresDataAgentStore(string connectionString)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(connectionString);
    }

    public string ProviderName => "postgres";

    public void Initialize() => throw NotImplemented();
    public void ImportFixtures() => throw NotImplemented();
    public DataAgentQueryResult Query(DataAgentCompiledSql compiledSql) => throw NotImplemented();
    public void RecordAccepted(DataAgentAcceptedAuditInput input) => throw NotImplemented();
    public void RecordRejected(DataAgentRejectedAuditInput input) => throw NotImplemented();
    public IReadOnlyList<DataAgentAuditRecord> ReadQueryAudit() => throw NotImplemented();
    public void RecordToolBrokerAudit(DataAgentToolBrokerAuditRecord record) => throw NotImplemented();
    public IReadOnlyList<DataAgentToolBrokerAuditRecord> ReadToolBrokerAudit() => throw NotImplemented();

    static NotSupportedException NotImplemented()
    {
        return new NotSupportedException("PostgreSQL DataAgent store implementation is added in the V2 provider task.");
    }
}
