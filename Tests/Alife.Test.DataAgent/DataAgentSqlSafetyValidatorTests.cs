using Alife.Function.DataAgent;

namespace Alife.Test.DataAgent;

[TestFixture]
public sealed class DataAgentSqlSafetyValidatorTests
{
    readonly DataAgentSqlSafetyValidator validator = new();

    [TestCase("DELETE FROM engineering_gate")]
    [TestCase("DROP TABLE engineering_gate")]
    [TestCase("UPDATE engineering_gate SET status = 'passed'")]
    [TestCase("PRAGMA table_info(engineering_gate)")]
    [TestCase("ATTACH DATABASE 'x' AS y")]
    [TestCase("CREATE TABLE unsafe(id INTEGER)")]
    [TestCase("ALTER TABLE engineering_gate ADD COLUMN x TEXT")]
    public void RejectsDestructiveOrUnsafeSql(string sql)
    {
        DataAgentSqlSafetyResult result = validator.Validate(sql);

        Assert.Multiple(() =>
        {
            Assert.That(result.IsSafe, Is.False);
            Assert.That(result.Reason, Is.Not.Empty);
        });
    }

    [Test]
    public void RejectsMultipleStatements()
    {
        DataAgentSqlSafetyResult result = validator.Validate("SELECT name FROM engineering_gate LIMIT 10; SELECT name FROM query_audit LIMIT 10");

        Assert.Multiple(() =>
        {
            Assert.That(result.IsSafe, Is.False);
            Assert.That(result.Reason, Is.EqualTo("multi_statement_sql_rejected"));
        });
    }

    [Test]
    public void RejectsSelectWithoutLimit()
    {
        DataAgentSqlSafetyResult result = validator.Validate("SELECT name FROM engineering_gate");

        Assert.Multiple(() =>
        {
            Assert.That(result.IsSafe, Is.False);
            Assert.That(result.Reason, Is.EqualTo("limit_required"));
        });
    }

    [Test]
    public void AcceptsReadOnlySelectWithLimit()
    {
        DataAgentSqlSafetyResult result = validator.Validate("SELECT name FROM engineering_gate LIMIT 10");

        Assert.Multiple(() =>
        {
            Assert.That(result.IsSafe, Is.True);
            Assert.That(result.Reason, Is.Empty);
        });
    }
}
