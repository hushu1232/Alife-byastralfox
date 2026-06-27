using Alife.Function.DataAgent;

namespace Alife.Test.DataAgent;

[TestFixture]
public sealed class DataAgentSqliteExecutionTests
{
    [Test]
    public void InitializesSchemaImportsFixturesAndExecutesReadOnlyQuery()
    {
        string databasePath = NewDatabasePath();
        DataAgentSchemaInitializer.Initialize(databasePath);
        DataAgentFixtureImporter.Import(databasePath);

        DataAgentQueryPlan plan = new(
            "engineering_gate",
            "find_required_passed_gates",
            ["name", "status", "evidence_path"],
            [
                new DataAgentFilter("required", "=", true),
                new DataAgentFilter("status", "=", "passed")
            ],
            [],
            10);

        DataAgentCompiledSql compiled = new DataAgentSqlCompiler(DataAgentCatalog.CreateDefault()).Compile(plan);
        DataAgentSqlSafetyResult safety = new DataAgentSqlSafetyValidator().Validate(compiled.Sql);
        Assert.That(safety.IsSafe, Is.True, safety.Reason);

        DataAgentQueryResult result = new DataAgentQueryExecutor(databasePath).Execute(compiled);

        Assert.Multiple(() =>
        {
            Assert.That(result.Rows, Has.Count.EqualTo(1));
            Assert.That(result.Rows[0]["name"], Is.EqualTo("Runtime readiness script"));
            Assert.That(result.Rows[0]["status"], Is.EqualTo("passed"));
            Assert.That(result.Rows[0]["evidence_path"], Is.EqualTo("tools/check-qchat-runtime-readiness.ps1"));
        });
    }

    [Test]
    public void RecordsAcceptedAndRejectedQueryAuditRows()
    {
        string databasePath = NewDatabasePath();
        DataAgentSchemaInitializer.Initialize(databasePath);

        DataAgentAuditLog auditLog = new(databasePath);
        auditLog.RecordAccepted(
            "哪些 required gate 已通过？",
            "engineering_gate",
            """{"dataset":"engineering_gate"}""",
            "SELECT name FROM engineering_gate LIMIT 10",
            1,
            TimeSpan.FromMilliseconds(12));

        auditLog.RecordRejected(
            "删除所有 gate",
            "engineering_gate",
            """{"dataset":"engineering_gate"}""",
            "DELETE FROM engineering_gate",
            "unsafe_keyword_rejected",
            TimeSpan.FromMilliseconds(2));

        IReadOnlyList<DataAgentAuditRecord> records = auditLog.ReadAll();

        Assert.Multiple(() =>
        {
            Assert.That(records, Has.Count.EqualTo(2));
            Assert.That(records[0].Validated, Is.True);
            Assert.That(records[0].RejectedReason, Is.Empty);
            Assert.That(records[0].RowCount, Is.EqualTo(1));
            Assert.That(records[1].Validated, Is.False);
            Assert.That(records[1].RejectedReason, Is.EqualTo("unsafe_keyword_rejected"));
            Assert.That(records[1].GeneratedSql, Is.EqualTo("DELETE FROM engineering_gate"));
        });
    }

    static string NewDatabasePath()
    {
        string directory = Path.Combine(TestContext.CurrentContext.WorkDirectory, "dataagent-sqlite-tests");
        Directory.CreateDirectory(directory);
        return Path.Combine(directory, $"{Guid.NewGuid():N}.sqlite");
    }
}
