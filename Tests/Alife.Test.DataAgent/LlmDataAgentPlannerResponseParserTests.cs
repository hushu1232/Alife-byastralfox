using Alife.Function.DataAgent;

namespace Alife.Test.DataAgent;

[TestFixture]
public sealed class LlmDataAgentPlannerResponseParserTests
{
    readonly DataAgentCatalog catalog = DataAgentCatalog.CreateDefault();

    [Test]
    public void ParseValidPlanReturnsEnvelope()
    {
        string json = """
            {"type":"plan","planner_name":"UntrustedPlanner","intent":"find_dataagent_documents","dataset":"document_index","confidence":"medium","signals":["dataagent","document"],"reason":"question asks for DataAgent documentation","select_fields":["path","title","summary"],"filters":[{"field":"tags","operator":"contains","value":"dataagent"}],"sorts":[{"field":"updated_at","direction":"desc"}],"limit":20}
            """;

        DataAgentLlmPlannerResult result = new LlmDataAgentPlannerResponseParser(catalog).Parse(json);

        Assert.Multiple(() =>
        {
            Assert.That(result.IsValid, Is.True);
            Assert.That(result.Envelope, Is.Not.Null);
            Assert.That(result.Envelope!.Plan, Is.Not.Null);
            Assert.That(result.Envelope.Plan!.Dataset, Is.EqualTo("document_index"));
            Assert.That(result.Envelope.Plan.Select, Is.EqualTo(new[] { "path", "title", "summary" }));
            Assert.That(result.Envelope.Plan.OrderBy, Has.Count.EqualTo(1));
            Assert.That(result.Envelope.Plan.OrderBy[0], Is.EqualTo(new DataAgentOrderBy("updated_at", "desc")));
            Assert.That(result.Envelope.Clarification, Is.Null);
            Assert.That(result.Envelope.Explanation.PlannerName, Is.EqualTo(nameof(LlmDataAgentQueryPlanner)));
            Assert.That(result.Envelope.Explanation.Confidence, Is.EqualTo("medium"));
            Assert.That(result.RejectedReason, Is.Empty);
        });
    }

    [Test]
    public void ParseValidClarificationReturnsClarificationEnvelope()
    {
        string json = """
            {"type":"clarification","planner_name":"LlmDataAgentQueryPlanner","intent":"clarify_ambiguous_query","dataset":"","confidence":"low","signals":["ambiguous_time_range"],"reason":"question does not specify time range","clarification_question":"Do you want the last 7 days, last 30 days, or all history?","clarification_options":["last 7 days","last 30 days","all history"]}
            """;

        DataAgentLlmPlannerResult result = new LlmDataAgentPlannerResponseParser(catalog).Parse(json);

        Assert.Multiple(() =>
        {
            Assert.That(result.IsValid, Is.True);
            Assert.That(result.Envelope, Is.Not.Null);
            Assert.That(result.Envelope!.Plan, Is.Null);
            Assert.That(result.Envelope.Clarification, Is.Not.Null);
            Assert.That(result.Envelope.Clarification!.Question, Does.Contain("last 7 days"));
            Assert.That(result.Envelope.Clarification.Options, Has.Count.EqualTo(3));
            Assert.That(result.Envelope.Explanation.PlannerName, Is.EqualTo(nameof(LlmDataAgentQueryPlanner)));
        });
    }

    [TestCase("")]
    [TestCase("   ")]
    public void ParseRejectsEmptyOutput(string raw)
    {
        DataAgentLlmPlannerResult result = new LlmDataAgentPlannerResponseParser(catalog).Parse(raw);
        Assert.That(result.RejectedReason, Does.Contain("empty_output"));
    }

    [TestCase("preface {\"type\":\"plan\"}")]
    [TestCase("{\"type\":\"plan\"} trailing")]
    [TestCase("{\"type\":\"plan\"}{\"type\":\"plan\"}")]
    [TestCase("[{\"type\":\"plan\"}]")]
    public void ParseRejectsJsonWrappedWithNaturalLanguage(string raw)
    {
        DataAgentLlmPlannerResult result = new LlmDataAgentPlannerResponseParser(catalog).Parse(raw);
        Assert.That(result.RejectedReason, Does.Contain("json_must_be_single_object"));
    }

    [Test]
    public void ParseRejectsUnknownDataset()
    {
        string json = """
            {"type":"plan","planner_name":"LlmDataAgentQueryPlanner","intent":"unsafe","dataset":"sqlite_master","confidence":"medium","signals":["unsafe"],"reason":"bad dataset","select_fields":["name"],"filters":[],"sorts":[],"limit":20}
            """;

        DataAgentLlmPlannerResult result = new LlmDataAgentPlannerResponseParser(catalog).Parse(json);
        Assert.That(result.RejectedReason, Does.Contain("unknown_dataset:sqlite_master"));
    }

    [Test]
    public void ParseRejectsUnknownField()
    {
        string json = """
            {"type":"plan","planner_name":"LlmDataAgentQueryPlanner","intent":"bad_field","dataset":"document_index","confidence":"medium","signals":["bad"],"reason":"bad field","select_fields":["path","secret"],"filters":[],"sorts":[],"limit":20}
            """;

        DataAgentLlmPlannerResult result = new LlmDataAgentPlannerResponseParser(catalog).Parse(json);
        Assert.That(result.RejectedReason, Does.Contain("unknown_select_field:document_index.secret"));
    }

    [Test]
    public void ParseRejectsInvalidOperator()
    {
        string json = """
            {"type":"plan","planner_name":"LlmDataAgentQueryPlanner","intent":"bad_operator","dataset":"document_index","confidence":"medium","signals":["bad"],"reason":"bad operator","select_fields":["path"],"filters":[{"field":"tags","operator":"starts_with","value":"dataagent"}],"sorts":[],"limit":20}
            """;

        DataAgentLlmPlannerResult result = new LlmDataAgentPlannerResponseParser(catalog).Parse(json);
        Assert.That(result.RejectedReason, Does.Contain("unsupported_operator:starts_with"));
    }

    [Test]
    public void ParseRejectsInvalidOrderDirection()
    {
        string json = """
            {"type":"plan","planner_name":"LlmDataAgentQueryPlanner","intent":"bad_sort","dataset":"document_index","confidence":"medium","signals":["bad"],"reason":"bad sort","select_fields":["path"],"filters":[],"sorts":[{"field":"updated_at","direction":"sideways"}],"limit":20}
            """;

        DataAgentLlmPlannerResult result = new LlmDataAgentPlannerResponseParser(catalog).Parse(json);
        Assert.That(result.RejectedReason, Does.Contain("unsupported_order_direction:sideways"));
    }

    [Test]
    public void ParseRejectsInvalidConfidence()
    {
        string json = """
            {"type":"plan","planner_name":"LlmDataAgentQueryPlanner","intent":"bad_confidence","dataset":"document_index","confidence":"certain","signals":["bad"],"reason":"bad confidence","select_fields":["path"],"filters":[],"sorts":[],"limit":20}
            """;

        DataAgentLlmPlannerResult result = new LlmDataAgentPlannerResponseParser(catalog).Parse(json);
        Assert.That(result.RejectedReason, Does.Contain("invalid_confidence:certain"));
    }

    [Test]
    public void ParseRejectsEmptySignals()
    {
        string json = """
            {"type":"plan","planner_name":"LlmDataAgentQueryPlanner","intent":"empty_signals","dataset":"document_index","confidence":"low","signals":[],"reason":"empty signals","select_fields":["path"],"filters":[],"sorts":[],"limit":20}
            """;

        DataAgentLlmPlannerResult result = new LlmDataAgentPlannerResponseParser(catalog).Parse(json);
        Assert.That(result.RejectedReason, Does.Contain("empty_array:signals"));
    }

    [Test]
    public void ParseRejectsMissingReason()
    {
        string json = """
            {"type":"plan","planner_name":"LlmDataAgentQueryPlanner","intent":"missing_reason","dataset":"document_index","confidence":"low","signals":["bad"],"reason":"","select_fields":["path"],"filters":[],"sorts":[],"limit":20}
            """;

        DataAgentLlmPlannerResult result = new LlmDataAgentPlannerResponseParser(catalog).Parse(json);
        Assert.That(result.RejectedReason, Does.Contain("missing_or_empty:reason"));
    }

    [Test]
    public void ParseSupportsScalarFilterValues()
    {
        string json = """
            {"type":"plan","planner_name":"LlmDataAgentQueryPlanner","intent":"scalar_values","dataset":"test_run","confidence":"medium","signals":["tests"],"reason":"question asks for tests","select_fields":["suite_name","passed","failed"],"filters":[{"field":"passed","operator":">","value":0},{"field":"failed","operator":"=","value":false},{"field":"command","operator":"!=","value":null}],"sorts":[],"limit":5}
            """;

        DataAgentLlmPlannerResult result = new LlmDataAgentPlannerResponseParser(catalog).Parse(json);

        Assert.Multiple(() =>
        {
            Assert.That(result.IsValid, Is.True);
            Assert.That(result.Envelope!.Plan!.Filters[0].Value, Is.EqualTo(0L));
            Assert.That(result.Envelope.Plan.Filters[1].Value, Is.EqualTo(false));
            Assert.That(result.Envelope.Plan.Filters[2].Value, Is.Null);
        });
    }

    [Test]
    public void ParseRejectsUnsupportedScalarValue()
    {
        string json = """
            {"type":"plan","planner_name":"LlmDataAgentQueryPlanner","intent":"bad_value","dataset":"document_index","confidence":"low","signals":["bad"],"reason":"bad value","select_fields":["path"],"filters":[{"field":"tags","operator":"contains","value":{"nested":"value"}}],"sorts":[],"limit":20}
            """;

        DataAgentLlmPlannerResult result = new LlmDataAgentPlannerResponseParser(catalog).Parse(json);
        Assert.That(result.RejectedReason, Does.Contain("unsupported_scalar_value"));
    }

    [Test]
    public void ParseRejectsClarificationWithTooFewOptions()
    {
        string json = """
            {"type":"clarification","planner_name":"LlmDataAgentQueryPlanner","intent":"clarify_ambiguous_query","dataset":"","confidence":"low","signals":["ambiguous"],"reason":"ambiguous","clarification_question":"Which range?","clarification_options":["last 7 days"]}
            """;

        DataAgentLlmPlannerResult result = new LlmDataAgentPlannerResponseParser(catalog).Parse(json);
        Assert.That(result.RejectedReason, Does.Contain("invalid_clarification_option_count"));
    }

    [Test]
    public void ParseRejectsPlanWithoutPlannerName()
    {
        string json = """
            {"type":"plan","intent":"missing_planner_name","dataset":"document_index","confidence":"low","signals":["bad"],"reason":"missing planner name","select_fields":["path"],"filters":[],"sorts":[],"limit":20}
            """;

        DataAgentLlmPlannerResult result = new LlmDataAgentPlannerResponseParser(catalog).Parse(json);
        Assert.That(result.RejectedReason, Does.Contain("missing_or_empty:planner_name"));
    }

    [Test]
    public void ParseRejectsClarificationWithoutPlannerName()
    {
        string json = """
            {"type":"clarification","intent":"clarify_ambiguous_query","dataset":"","confidence":"low","signals":["ambiguous"],"reason":"ambiguous","clarification_question":"Which range?","clarification_options":["last 7 days","last 30 days"]}
            """;

        DataAgentLlmPlannerResult result = new LlmDataAgentPlannerResponseParser(catalog).Parse(json);
        Assert.That(result.RejectedReason, Does.Contain("missing_or_empty:planner_name"));
    }
}
