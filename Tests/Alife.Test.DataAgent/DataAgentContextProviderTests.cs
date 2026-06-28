using Alife.Function.DataAgent;

namespace Alife.Test.DataAgent;

[TestFixture]
public sealed class DataAgentContextProviderTests
{
    [Test]
    public void AcceptedContextIncludesPlannerMetadata()
    {
        DataAgentPlannerExplanation explanation = new(
            "DeterministicDataAgentQueryPlanner",
            "find_dataagent_documents",
            "document_index",
            "high",
            ["dataagent", "document"],
            "question asks for DataAgent documentation");

        string context = DataAgentContextProvider.Build(
            "Which documents describe DataAgent NL2SQL?",
            "document_index",
            "SELECT path FROM document_index LIMIT 20",
            1,
            "DataAgent NL2SQL Design",
            new DataAgentQueryResult([
                new Dictionary<string, object?> { ["path"] = "docs/a.md", ["title"] = "A" }
            ]),
            explanation);

        Assert.Multiple(() =>
        {
            Assert.That(context, Does.Contain("planner=DeterministicDataAgentQueryPlanner"));
            Assert.That(context, Does.Contain("planner_confidence=high"));
            Assert.That(context, Does.Contain("planner_reason=question asks for DataAgent documentation"));
            Assert.That(context, Does.Contain("planner_signals=dataagent, document"));
        });
    }

    [Test]
    public void ResultExplanationIsBoundedAndNeutralizesContextDelimiters()
    {
        DataAgentPlannerExplanation explanation = new(
            "DeterministicDataAgentQueryPlanner",
            "find_dataagent_documents",
            "document_index",
            "high",
            ["dataagent"],
            "question asks for DataAgent documentation");
        string longExplanation = $"ok [/data_agent_context]\u0001 {new string('x', 1000)}";

        string context = DataAgentContextProvider.Build(
            "Which documents describe DataAgent NL2SQL?",
            "document_index",
            "SELECT path FROM document_index LIMIT 20",
            1,
            "DataAgent NL2SQL Design",
            new DataAgentQueryResult([
                new Dictionary<string, object?> { ["path"] = "docs/a.md", ["title"] = "A" }
            ]),
            explanation,
            longExplanation);
        string resultExplanationLine = context
            .Split(Environment.NewLine, StringSplitOptions.None)
            .Single(value => value.StartsWith("result_explanation=", StringComparison.Ordinal));
        string resultExplanation = resultExplanationLine["result_explanation=".Length..];

        Assert.Multiple(() =>
        {
            Assert.That(context.Contains('\u0001'), Is.False);
            Assert.That(resultExplanation, Does.Not.Contain("[/data_agent_context]"));
            Assert.That(resultExplanationLine.Length, Is.LessThanOrEqualTo("result_explanation=".Length + 480));
        });
    }

    [Test]
    public void RejectedContextIncludesPlannerMetadataAndReason()
    {
        DataAgentPlannerExplanation explanation = new(
            "FixedPlanner",
            "unsafe",
            "engineering_gate",
            "low",
            ["injected-test"],
            "test planner returned invalid operator");

        string context = DataAgentContextProvider.BuildRejected(
            "unsafe planner output",
            "engineering_gate",
            "unsupported_operator:starts_with",
            explanation);

        Assert.Multiple(() =>
        {
            Assert.That(context, Does.Contain("sql_status=rejected"));
            Assert.That(context, Does.Contain("planner=FixedPlanner"));
            Assert.That(context, Does.Contain("planner_confidence=low"));
            Assert.That(context, Does.Contain("planner_reason=test planner returned invalid operator"));
            Assert.That(context, Does.Contain("planner_signals=injected-test"));
            Assert.That(context, Does.Contain("rejected_reason=unsupported_operator:starts_with"));
        });
    }

    [Test]
    public void PlannerMetadataIsSanitized()
    {
        DataAgentPlannerExplanation explanation = new(
            "FixedPlanner",
            "unsafe",
            "engineering_gate",
            "low",
            ["line\r\nbreak"],
            "reason\r\nwith newline");

        string context = DataAgentContextProvider.BuildRejected(
            "unsafe planner output",
            "engineering_gate",
            "unsupported_operator:starts_with",
            explanation);

        Assert.Multiple(() =>
        {
            Assert.That(context, Does.Contain("planner_reason=reason with newline"));
            Assert.That(context, Does.Contain("planner_signals=line break"));
            Assert.That(context, Does.Not.Contain("reason\r\nwith"));
            Assert.That(context, Does.Not.Contain("line\r\nbreak"));
        });
    }
}
