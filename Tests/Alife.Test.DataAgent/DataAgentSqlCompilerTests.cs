using Alife.Function.DataAgent;

namespace Alife.Test.DataAgent;

[TestFixture]
public sealed class DataAgentSqlCompilerTests
{
    [Test]
    public void CompilesValidatedQueryPlanToParameterizedSql()
    {
        DataAgentQueryPlan plan = new(
            "engineering_gate",
            "find_missing_required_gates",
            ["name", "status", "evidence_path"],
            [
                new DataAgentFilter("required", "=", true),
                new DataAgentFilter("status", "!=", "passed")
            ],
            [],
            50);

        DataAgentSqlCompiler compiler = new(DataAgentCatalog.CreateDefault());

        DataAgentCompiledSql compiled = compiler.Compile(plan);

        Assert.Multiple(() =>
        {
            Assert.That(compiled.Sql, Is.EqualTo("SELECT name, status, evidence_path FROM engineering_gate WHERE required = @p0 AND status <> @p1 LIMIT 50"));
            Assert.That(compiled.Parameters, Has.Count.EqualTo(2));
            Assert.That(compiled.Parameters[0].Name, Is.EqualTo("@p0"));
            Assert.That(compiled.Parameters[0].Value, Is.EqualTo(true));
            Assert.That(compiled.Parameters[1].Name, Is.EqualTo("@p1"));
            Assert.That(compiled.Parameters[1].Value, Is.EqualTo("passed"));
        });
    }

    [Test]
    public void CompilesContainsFilterAsLikeParameter()
    {
        DataAgentQueryPlan plan = new(
            "document_index",
            "find_dataagent_docs",
            ["path", "title"],
            [new DataAgentFilter("tags", "contains", "dataagent")],
            [new DataAgentOrderBy("updated_at", "desc")],
            20);

        DataAgentSqlCompiler compiler = new(DataAgentCatalog.CreateDefault());

        DataAgentCompiledSql compiled = compiler.Compile(plan);

        Assert.Multiple(() =>
        {
            Assert.That(compiled.Sql, Is.EqualTo("SELECT path, title FROM document_index WHERE tags LIKE @p0 ORDER BY updated_at DESC LIMIT 20"));
            Assert.That(compiled.Parameters, Has.Count.EqualTo(1));
            Assert.That(compiled.Parameters[0].Value, Is.EqualTo("%dataagent%"));
        });
    }
}
