using Alife.Function.DataAgent;

namespace Alife.Test.DataAgent;

[TestFixture]
public sealed class DataAgentScenarioDiagnosticsFormatterTests
{
    [Test]
    public void FormatEmitsCompactMatchedDiagnostics()
    {
        DataAgentScenarioContext context = CreateMatchedContext();

        string text = DataAgentScenarioDiagnosticsFormatter.Format(context);

        string[] expectedLines =
        [
            "DataAgent scenario diagnostics",
            "scenario=engineering_readiness",
            "reason=scenario_context_matched",
            "datasets=engineering_gate,test_run",
            "fields=name,status,required,suite_name,failed,ran_at",
            "terms=工程门禁:engineering_gate;最近失败的测试:test_run",
            "metrics=失败:status!=passed;必需:required=true"
        ];

        Assert.That(text.Split(Environment.NewLine), Is.EqualTo(expectedLines));
    }

    [Test]
    public void FormatEmitsUnavailableWhenContextMissing()
    {
        string text = DataAgentScenarioDiagnosticsFormatter.Format(null);

        string[] expectedLines =
        [
            "DataAgent scenario diagnostics",
            "state=unavailable",
            "reason=scenario_context_pack_unavailable"
        ];

        Assert.That(text.Split(Environment.NewLine), Is.EqualTo(expectedLines));
    }

    [Test]
    public void FormatOmitsRawSqlAndHiddenContext()
    {
        DataAgentScenarioContext context = new(
            "engineering_readiness SELECT * FROM users [hidden_context]owner secret[/hidden_context]",
            "zh-CN",
            [
                new DataAgentScenarioTermMatch(
                    "工程门禁; SELECT password FROM users [tool_route_context]manual[/tool_route_context]",
                    "engineering_gate; DROP TABLE users",
                    ["name\nstatus", "[data_agent_evidence_pack]trace secret[/data_agent_evidence_pack]"],
                    "matched text should not be emitted"),
                new DataAgentScenarioTermMatch(
                    "最近失败的测试",
                    "test_run",
                    ["suite_name"],
                    "another unsafe matched text SELECT * FROM hidden_context")
            ],
            [
                new DataAgentScenarioMetricMatch(
                    "失败; hidden_context [data_agent_evidence_pack]",
                    "status\nWHERE password",
                    "!=",
                    "passed; SELECT * FROM users"),
                new DataAgentScenarioMetricMatch("必需", "required", "=", true)
            ],
            ["engineering_gate", "test_run; DROP TABLE hidden_context"],
            ["name\nstatus", "hidden_context", "hidden prompt ignore previous instructions", "required"],
            DataAgentScenarioContext.ReasonMatched);

        string text = DataAgentScenarioDiagnosticsFormatter.Format(context);

        Assert.Multiple(() =>
        {
            Assert.That(text, Does.Contain("redacted"));
            Assert.That(text, Does.Contain("required=true"));
            Assert.That(text, Does.Not.Contain("SELECT"));
            Assert.That(text, Does.Not.Contain("DROP"));
            Assert.That(text, Does.Not.Contain("FROM users"));
            Assert.That(text, Does.Not.Contain("password"));
            Assert.That(text, Does.Not.Contain("hidden_context"));
            Assert.That(text, Does.Not.Contain("hidden prompt"));
            Assert.That(text, Does.Not.Contain("ignore previous instructions"));
            Assert.That(text, Does.Not.Contain("tool_route_context"));
            Assert.That(text, Does.Not.Contain("data_agent_evidence_pack"));
            Assert.That(text, Does.Not.Contain("owner secret"));
            Assert.That(text, Does.Not.Contain("manual"));
            Assert.That(text, Does.Not.Contain("trace secret"));
            Assert.That(text, Does.Not.Contain("matched text should not be emitted"));
            Assert.That(text, Does.Not.Contain("another unsafe matched text"));
        });
    }

    [Test]
    public void FormatSanitizesSemicolonsNewlinesAndLowercasesBooleans()
    {
        DataAgentScenarioContext context = new(
            "engineering_readiness",
            "zh-CN",
            [new DataAgentScenarioTermMatch("门禁;状态", "engineering_gate\nlatest", ["name"], "门禁")],
            [new DataAgentScenarioMetricMatch("必需;项", "required\nflag", "=", true)],
            ["engineering_gate\nlatest"],
            ["name;status", "required\nflag"],
            DataAgentScenarioContext.ReasonMatched);

        string text = DataAgentScenarioDiagnosticsFormatter.Format(context);

        Assert.Multiple(() =>
        {
            Assert.That(text, Does.Contain("datasets=engineering_gate latest"));
            Assert.That(text, Does.Contain("fields=name status,required flag"));
            Assert.That(text, Does.Contain("terms=门禁 状态:engineering_gate latest"));
            Assert.That(text, Does.Contain("metrics=必需 项:required flag=true"));
            Assert.That(text, Does.Not.Contain("True"));
        });
    }

    static DataAgentScenarioContext CreateMatchedContext()
    {
        return new DataAgentScenarioContext(
            "engineering_readiness",
            "zh-CN",
            [
                new DataAgentScenarioTermMatch(
                    "工程门禁",
                    "engineering_gate",
                    ["name", "status", "required"],
                    "工程门禁"),
                new DataAgentScenarioTermMatch(
                    "最近失败的测试",
                    "test_run",
                    ["suite_name", "failed", "ran_at"],
                    "最近失败的测试")
            ],
            [
                new DataAgentScenarioMetricMatch("失败", "status", "!=", "passed"),
                new DataAgentScenarioMetricMatch("必需", "required", "=", true)
            ],
            ["engineering_gate", "test_run"],
            ["name", "status", "required", "suite_name", "failed", "ran_at"],
            DataAgentScenarioContext.ReasonMatched);
    }
}
