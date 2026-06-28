using Alife.Function.DataAgent;

namespace Alife.Test.DataAgent;

[TestFixture]
public sealed class DataAgentPlannerTests
{
    [Test]
    public void PlansQChatTtsReadinessQuestion()
    {
        DeterministicDataAgentQueryPlanner planner = new();

        DataAgentQueryPlanEnvelope envelope = planner.Plan(new DataAgentQueryRequest(
            "Which readiness checks are related to QChat TTS and vision?",
            "developer",
            "en-US",
            false));
        DataAgentQueryPlan plan = RequirePlan(envelope);

        Assert.Multiple(() =>
        {
            Assert.That(plan.Dataset, Is.EqualTo("runtime_readiness_check"));
            Assert.That(plan.Intent, Is.EqualTo("find_qchat_tts_readiness"));
            Assert.That(plan.Filters, Has.Count.EqualTo(1));
            Assert.That(plan.Filters[0].Field, Is.EqualTo("capability"));
            Assert.That(plan.Filters[0].Operator, Is.EqualTo("contains"));
            Assert.That(plan.Filters[0].Value, Is.EqualTo("Tts"));
        });
        AssertExplanation(
            envelope,
            "high",
            ["readiness", "tts", "vision"],
            "question mentions QChat TTS or vision readiness");
    }

    [Test]
    public void PlansRuntimeReadinessRequiredQuestion()
    {
        DeterministicDataAgentQueryPlanner planner = new();

        DataAgentQueryPlanEnvelope envelope = planner.Plan(new DataAgentQueryRequest(
            "Which runtime readiness gate is required?",
            "developer",
            "en-US",
            false));
        DataAgentQueryPlan plan = RequirePlan(envelope);

        Assert.Multiple(() =>
        {
            Assert.That(plan.Dataset, Is.EqualTo("engineering_gate"));
            Assert.That(plan.Intent, Is.EqualTo("find_runtime_readiness_required_evidence"));
            Assert.That(plan.Limit, Is.EqualTo(10));
        });
        AssertExplanation(
            envelope,
            "high",
            ["runtime", "readiness", "required"],
            "question mentions runtime readiness required evidence");
    }

    [Test]
    public void RuntimeReadinessRequiredQuestionIncludesHighConfidenceExplanation()
    {
        DeterministicDataAgentQueryPlanner planner = new();

        DataAgentQueryPlanEnvelope envelope = planner.Plan(new DataAgentQueryRequest(
            "Which runtime readiness gate is required?",
            "developer",
            "en-US",
            false));

        AssertExplanation(
            envelope,
            "high",
            ["runtime", "readiness", "required"],
            "question mentions runtime readiness required evidence");
    }

    [Test]
    public void PlansLatestTestRunQuestion()
    {
        DeterministicDataAgentQueryPlanner planner = new();

        DataAgentQueryPlanEnvelope envelope = planner.Plan(new DataAgentQueryRequest(
            "What is the latest test result pass fail skipped count?",
            "developer",
            "en-US",
            false));
        DataAgentQueryPlan plan = RequirePlan(envelope);

        Assert.Multiple(() =>
        {
            Assert.That(plan.Dataset, Is.EqualTo("test_run"));
            Assert.That(plan.Intent, Is.EqualTo("latest_test_run_summary"));
            Assert.That(plan.OrderBy, Has.Count.EqualTo(1));
            Assert.That(plan.OrderBy[0].Field, Is.EqualTo("ran_at"));
            Assert.That(plan.OrderBy[0].Direction, Is.EqualTo("desc"));
            Assert.That(plan.Limit, Is.EqualTo(1));
        });
        AssertExplanation(
            envelope,
            "high",
            ["test", "result"],
            "question asks for latest test results");
    }

    [Test]
    public void PlansDataAgentDocumentQuestion()
    {
        DeterministicDataAgentQueryPlanner planner = new();

        DataAgentQueryPlanEnvelope envelope = planner.Plan(new DataAgentQueryRequest(
            "Which documents describe DataAgent NL2SQL?",
            "developer",
            "en-US",
            false));
        DataAgentQueryPlan plan = RequirePlan(envelope);

        Assert.Multiple(() =>
        {
            Assert.That(plan.Dataset, Is.EqualTo("document_index"));
            Assert.That(plan.Intent, Is.EqualTo("find_dataagent_documents"));
            Assert.That(plan.Filters, Has.Count.EqualTo(1));
            Assert.That(plan.Filters[0].Field, Is.EqualTo("tags"));
            Assert.That(plan.Filters[0].Operator, Is.EqualTo("contains"));
            Assert.That(plan.Filters[0].Value, Is.EqualTo("dataagent"));
        });
        AssertExplanation(
            envelope,
            "high",
            ["dataagent", "nl2sql", "document"],
            "question asks for DataAgent or NL2SQL documentation");
    }

    [Test]
    public void PlansUnknownProjectStateQuestionAsMissingRequiredGateQuery()
    {
        DeterministicDataAgentQueryPlanner planner = new();

        DataAgentQueryPlanEnvelope envelope = planner.Plan(new DataAgentQueryRequest(
            "What project state still needs attention?",
            "developer",
            "en-US",
            false));
        DataAgentQueryPlan plan = RequirePlan(envelope);

        Assert.Multiple(() =>
        {
            Assert.That(plan.Dataset, Is.EqualTo("engineering_gate"));
            Assert.That(plan.Intent, Is.EqualTo("find_missing_required_gates"));
            Assert.That(plan.Filters, Has.Count.EqualTo(2));
            Assert.That(plan.Filters[0].Field, Is.EqualTo("required"));
            Assert.That(plan.Filters[0].Operator, Is.EqualTo("="));
            Assert.That(plan.Filters[0].Value, Is.EqualTo(true));
            Assert.That(plan.Filters[1].Field, Is.EqualTo("status"));
            Assert.That(plan.Filters[1].Operator, Is.EqualTo("!="));
            Assert.That(plan.Filters[1].Value, Is.EqualTo("passed"));
        });
        AssertExplanation(
            envelope,
            "low",
            ["fallback"],
            "fallback to missing required engineering gates");
    }

    [Test]
    public void UnknownProjectStateFallbackIncludesLowConfidenceExplanation()
    {
        DeterministicDataAgentQueryPlanner planner = new();

        DataAgentQueryPlanEnvelope envelope = planner.Plan(new DataAgentQueryRequest(
            "What project state still needs attention?",
            "developer",
            "en-US",
            false));

        AssertExplanation(
            envelope,
            "low",
            ["fallback"],
            "fallback to missing required engineering gates");
    }

    [Test]
    public void RoleLocaleAndLiveFlagsDoNotChangeDeterministicPlannerOutput()
    {
        DeterministicDataAgentQueryPlanner planner = new();

        DataAgentQueryPlanEnvelope developerEnvelope = planner.Plan(new DataAgentQueryRequest(
            "Which documents describe DataAgent NL2SQL?",
            "developer",
            "zh-CN",
            false));
        DataAgentQueryPlanEnvelope analystEnvelope = planner.Plan(new DataAgentQueryRequest(
            "Which documents describe DataAgent NL2SQL?",
            "analyst",
            "ja-JP",
            true));

        Assert.Multiple(() =>
        {
            DataAgentQueryPlan developerPlan = RequirePlan(developerEnvelope);
            DataAgentQueryPlan analystPlan = RequirePlan(analystEnvelope);

            Assert.That(analystPlan.Dataset, Is.EqualTo(developerPlan.Dataset));
            Assert.That(analystPlan.Intent, Is.EqualTo(developerPlan.Intent));
            Assert.That(analystPlan.Select, Is.EqualTo(developerPlan.Select));
            Assert.That(analystPlan.Filters.Select(filter => (filter.Field, filter.Operator, filter.Value)), Is.EqualTo(
                developerPlan.Filters.Select(filter => (filter.Field, filter.Operator, filter.Value))));
            Assert.That(analystPlan.OrderBy.Select(order => (order.Field, order.Direction)), Is.EqualTo(
                developerPlan.OrderBy.Select(order => (order.Field, order.Direction))));
            Assert.That(analystPlan.Limit, Is.EqualTo(developerPlan.Limit));
        });
        AssertSameExplanation(analystEnvelope.Explanation, developerEnvelope.Explanation);
    }

    static void AssertSameExplanation(
        DataAgentPlannerExplanation actual,
        DataAgentPlannerExplanation expected)
    {
        Assert.Multiple(() =>
        {
            Assert.That(actual.PlannerName, Is.EqualTo(expected.PlannerName));
            Assert.That(actual.Dataset, Is.EqualTo(expected.Dataset));
            Assert.That(actual.Intent, Is.EqualTo(expected.Intent));
            Assert.That(actual.Confidence, Is.EqualTo(expected.Confidence));
            Assert.That(actual.Signals, Is.EqualTo(expected.Signals));
            Assert.That(actual.Reason, Is.EqualTo(expected.Reason));
        });
    }

    static void AssertExplanation(
        DataAgentQueryPlanEnvelope envelope,
        string confidence,
        string[] signals,
        string reason)
    {
        DataAgentQueryPlan plan = RequirePlan(envelope);

        Assert.Multiple(() =>
        {
            Assert.That(envelope.Explanation.PlannerName, Is.EqualTo(nameof(DeterministicDataAgentQueryPlanner)));
            Assert.That(envelope.Explanation.Dataset, Is.EqualTo(plan.Dataset));
            Assert.That(envelope.Explanation.Intent, Is.EqualTo(plan.Intent));
            Assert.That(envelope.Explanation.Confidence, Is.EqualTo(confidence));
            Assert.That(envelope.Explanation.Signals, Is.EqualTo(signals));
            Assert.That(envelope.Explanation.Reason, Is.EqualTo(reason));
        });
    }

    static DataAgentQueryPlan RequirePlan(DataAgentQueryPlanEnvelope envelope)
    {
        Assert.That(envelope.Plan, Is.Not.Null);
        return envelope.Plan!;
    }
}
