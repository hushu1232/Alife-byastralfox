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
