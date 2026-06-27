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

        Assert.Multiple(() =>
        {
            Assert.That(envelope.Plan.Dataset, Is.EqualTo("runtime_readiness_check"));
            Assert.That(envelope.Plan.Intent, Is.EqualTo("find_qchat_tts_readiness"));
            Assert.That(envelope.Plan.Filters, Has.Count.EqualTo(1));
            Assert.That(envelope.Plan.Filters[0].Field, Is.EqualTo("capability"));
            Assert.That(envelope.Plan.Filters[0].Operator, Is.EqualTo("contains"));
            Assert.That(envelope.Plan.Filters[0].Value, Is.EqualTo("Tts"));
        });
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

        Assert.Multiple(() =>
        {
            Assert.That(envelope.Plan.Dataset, Is.EqualTo("engineering_gate"));
            Assert.That(envelope.Plan.Intent, Is.EqualTo("find_runtime_readiness_required_evidence"));
            Assert.That(envelope.Plan.Limit, Is.EqualTo(10));
        });
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

        Assert.Multiple(() =>
        {
            Assert.That(envelope.Plan.Dataset, Is.EqualTo("engineering_gate"));
            Assert.That(envelope.Plan.Intent, Is.EqualTo("find_runtime_readiness_required_evidence"));
            Assert.That(envelope.Explanation.PlannerName, Is.EqualTo(nameof(DeterministicDataAgentQueryPlanner)));
            Assert.That(envelope.Explanation.Dataset, Is.EqualTo(envelope.Plan.Dataset));
            Assert.That(envelope.Explanation.Intent, Is.EqualTo(envelope.Plan.Intent));
            Assert.That(envelope.Explanation.Confidence, Is.EqualTo("high"));
            Assert.That(envelope.Explanation.Signals, Does.Contain("runtime"));
            Assert.That(envelope.Explanation.Signals, Does.Contain("readiness"));
            Assert.That(envelope.Explanation.Signals, Does.Contain("required"));
            Assert.That(envelope.Explanation.Reason, Does.Contain("runtime readiness"));
        });
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

        Assert.Multiple(() =>
        {
            Assert.That(envelope.Plan.Dataset, Is.EqualTo("test_run"));
            Assert.That(envelope.Plan.Intent, Is.EqualTo("latest_test_run_summary"));
            Assert.That(envelope.Plan.OrderBy, Has.Count.EqualTo(1));
            Assert.That(envelope.Plan.OrderBy[0].Field, Is.EqualTo("ran_at"));
            Assert.That(envelope.Plan.OrderBy[0].Direction, Is.EqualTo("desc"));
            Assert.That(envelope.Plan.Limit, Is.EqualTo(1));
        });
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

        Assert.Multiple(() =>
        {
            Assert.That(envelope.Plan.Dataset, Is.EqualTo("document_index"));
            Assert.That(envelope.Plan.Intent, Is.EqualTo("find_dataagent_documents"));
            Assert.That(envelope.Plan.Filters, Has.Count.EqualTo(1));
            Assert.That(envelope.Plan.Filters[0].Field, Is.EqualTo("tags"));
            Assert.That(envelope.Plan.Filters[0].Operator, Is.EqualTo("contains"));
            Assert.That(envelope.Plan.Filters[0].Value, Is.EqualTo("dataagent"));
        });
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

        Assert.Multiple(() =>
        {
            Assert.That(envelope.Plan.Dataset, Is.EqualTo("engineering_gate"));
            Assert.That(envelope.Plan.Intent, Is.EqualTo("find_missing_required_gates"));
            Assert.That(envelope.Plan.Filters, Has.Count.EqualTo(2));
            Assert.That(envelope.Plan.Filters[0].Field, Is.EqualTo("required"));
            Assert.That(envelope.Plan.Filters[0].Operator, Is.EqualTo("="));
            Assert.That(envelope.Plan.Filters[0].Value, Is.EqualTo(true));
            Assert.That(envelope.Plan.Filters[1].Field, Is.EqualTo("status"));
            Assert.That(envelope.Plan.Filters[1].Operator, Is.EqualTo("!="));
            Assert.That(envelope.Plan.Filters[1].Value, Is.EqualTo("passed"));
        });
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

        Assert.Multiple(() =>
        {
            Assert.That(envelope.Plan.Dataset, Is.EqualTo("engineering_gate"));
            Assert.That(envelope.Plan.Intent, Is.EqualTo("find_missing_required_gates"));
            Assert.That(envelope.Explanation.Confidence, Is.EqualTo("low"));
            Assert.That(envelope.Explanation.Signals, Does.Contain("fallback"));
            Assert.That(envelope.Explanation.Reason, Does.Contain("fallback"));
        });
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
            DataAgentQueryPlan developerPlan = developerEnvelope.Plan;
            DataAgentQueryPlan analystPlan = analystEnvelope.Plan;

            Assert.That(analystPlan.Dataset, Is.EqualTo(developerPlan.Dataset));
            Assert.That(analystPlan.Intent, Is.EqualTo(developerPlan.Intent));
            Assert.That(analystPlan.Select, Is.EqualTo(developerPlan.Select));
            Assert.That(analystPlan.Filters.Select(filter => (filter.Field, filter.Operator, filter.Value)), Is.EqualTo(
                developerPlan.Filters.Select(filter => (filter.Field, filter.Operator, filter.Value))));
            Assert.That(analystPlan.OrderBy.Select(order => (order.Field, order.Direction)), Is.EqualTo(
                developerPlan.OrderBy.Select(order => (order.Field, order.Direction))));
            Assert.That(analystPlan.Limit, Is.EqualTo(developerPlan.Limit));
        });
    }
}
