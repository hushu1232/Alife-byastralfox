using Alife.Function.DataAgent;

namespace Alife.Test.DataAgent;

[TestFixture]
public sealed class DataAgentQueryPlanValidatorTests
{
    readonly DataAgentQueryPlanValidator validator = new(DataAgentCatalog.CreateDefault());

    [Test]
    public void ValidEngineeringGateQueryPlanPasses()
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

        DataAgentValidationResult result = validator.Validate(plan);

        Assert.That(result.IsValid, Is.True, string.Join(Environment.NewLine, result.Errors));
    }

    [Test]
    public void UnknownDatasetIsRejected()
    {
        DataAgentQueryPlan plan = ValidPlan() with { Dataset = "unknown_table" };

        DataAgentValidationResult result = validator.Validate(plan);

        Assert.Multiple(() =>
        {
            Assert.That(result.IsValid, Is.False);
            Assert.That(result.Errors, Does.Contain("unknown_dataset:unknown_table"));
        });
    }

    [Test]
    public void UnknownSelectedFieldIsRejected()
    {
        DataAgentQueryPlan plan = ValidPlan() with { Select = ["name", "secret_token"] };

        DataAgentValidationResult result = validator.Validate(plan);

        Assert.Multiple(() =>
        {
            Assert.That(result.IsValid, Is.False);
            Assert.That(result.Errors, Does.Contain("unknown_select_field:engineering_gate.secret_token"));
        });
    }

    [TestCase(0)]
    [TestCase(101)]
    public void LimitOutsideOneToOneHundredIsRejected(int limit)
    {
        DataAgentQueryPlan plan = ValidPlan() with { Limit = limit };

        DataAgentValidationResult result = validator.Validate(plan);

        Assert.Multiple(() =>
        {
            Assert.That(result.IsValid, Is.False);
            Assert.That(result.Errors, Does.Contain($"invalid_limit:{limit}"));
        });
    }

    [Test]
    public void UnsupportedOperatorIsRejected()
    {
        DataAgentQueryPlan plan = ValidPlan() with
        {
            Filters = [new DataAgentFilter("status", "starts_with", "pass")]
        };

        DataAgentValidationResult result = validator.Validate(plan);

        Assert.Multiple(() =>
        {
            Assert.That(result.IsValid, Is.False);
            Assert.That(result.Errors, Does.Contain("unsupported_operator:starts_with"));
        });
    }

    [Test]
    public void ContainsOnTextFieldPasses()
    {
        DataAgentQueryPlan plan = new(
            "document_index",
            "find_dataagent_docs",
            ["path", "title"],
            [new DataAgentFilter("tags", "contains", "dataagent")],
            [],
            20);

        DataAgentValidationResult result = validator.Validate(plan);

        Assert.That(result.IsValid, Is.True, string.Join(Environment.NewLine, result.Errors));
    }

    [Test]
    public void ContainsOnBooleanFieldIsRejected()
    {
        DataAgentQueryPlan plan = ValidPlan() with
        {
            Filters = [new DataAgentFilter("required", "contains", true)]
        };

        DataAgentValidationResult result = validator.Validate(plan);

        Assert.Multiple(() =>
        {
            Assert.That(result.IsValid, Is.False);
            Assert.That(result.Errors, Does.Contain("unsupported_operator_for_field:contains:engineering_gate.required"));
        });
    }

    [Test]
    public void ContainsOnIntegerFieldIsRejected()
    {
        DataAgentQueryPlan plan = new(
            "test_run",
            "find_test_runs",
            ["suite_name", "passed"],
            [new DataAgentFilter("passed", "contains", 1)],
            [],
            20);

        DataAgentValidationResult result = validator.Validate(plan);

        Assert.Multiple(() =>
        {
            Assert.That(result.IsValid, Is.False);
            Assert.That(result.Errors, Does.Contain("unsupported_operator_for_field:contains:test_run.passed"));
        });
    }

    [Test]
    public void UnknownOrderByFieldIsRejected()
    {
        DataAgentQueryPlan plan = ValidPlan() with
        {
            OrderBy = [new DataAgentOrderBy("secret_token", "asc")]
        };

        DataAgentValidationResult result = validator.Validate(plan);

        Assert.Multiple(() =>
        {
            Assert.That(result.IsValid, Is.False);
            Assert.That(result.Errors, Does.Contain("unknown_order_field:engineering_gate.secret_token"));
        });
    }

    static DataAgentQueryPlan ValidPlan()
    {
        return new DataAgentQueryPlan(
            "engineering_gate",
            "find_missing_required_gates",
            ["name", "status", "evidence_path"],
            [new DataAgentFilter("required", "=", true)],
            [],
            50);
    }
}
