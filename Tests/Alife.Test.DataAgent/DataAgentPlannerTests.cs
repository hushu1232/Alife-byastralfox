using Alife.Function.DataAgent;

namespace Alife.Test.DataAgent;

[TestFixture]
public sealed class DataAgentPlannerTests
{
    [Test]
    public void PlansQChatTtsReadinessQuestion()
    {
        DeterministicDataAgentQueryPlanner planner = new();

        DataAgentQueryPlan plan = planner.Plan(new DataAgentQueryRequest(
            "Which readiness checks are related to QChat TTS and vision?",
            "developer",
            "en-US",
            false));

        Assert.Multiple(() =>
        {
            Assert.That(plan.Dataset, Is.EqualTo("runtime_readiness_check"));
            Assert.That(plan.Intent, Is.EqualTo("find_qchat_tts_readiness"));
            Assert.That(plan.Filters, Has.Count.EqualTo(1));
            Assert.That(plan.Filters[0].Field, Is.EqualTo("capability"));
            Assert.That(plan.Filters[0].Operator, Is.EqualTo("contains"));
            Assert.That(plan.Filters[0].Value, Is.EqualTo("Tts"));
        });
    }

    [Test]
    public void PlansRuntimeReadinessRequiredQuestion()
    {
        DeterministicDataAgentQueryPlanner planner = new();

        DataAgentQueryPlan plan = planner.Plan(new DataAgentQueryRequest(
            "Which runtime readiness gate is required?",
            "developer",
            "en-US",
            false));

        Assert.Multiple(() =>
        {
            Assert.That(plan.Dataset, Is.EqualTo("engineering_gate"));
            Assert.That(plan.Intent, Is.EqualTo("find_runtime_readiness_required_evidence"));
            Assert.That(plan.Limit, Is.EqualTo(10));
        });
    }

    [Test]
    public void PlansLatestTestRunQuestion()
    {
        DeterministicDataAgentQueryPlanner planner = new();

        DataAgentQueryPlan plan = planner.Plan(new DataAgentQueryRequest(
            "What is the latest test result pass fail skipped count?",
            "developer",
            "en-US",
            false));

        Assert.Multiple(() =>
        {
            Assert.That(plan.Dataset, Is.EqualTo("test_run"));
            Assert.That(plan.Intent, Is.EqualTo("latest_test_run_summary"));
            Assert.That(plan.OrderBy, Has.Count.EqualTo(1));
            Assert.That(plan.OrderBy[0].Field, Is.EqualTo("ran_at"));
            Assert.That(plan.OrderBy[0].Direction, Is.EqualTo("desc"));
            Assert.That(plan.Limit, Is.EqualTo(1));
        });
    }

    [Test]
    public void PlansDataAgentDocumentQuestion()
    {
        DeterministicDataAgentQueryPlanner planner = new();

        DataAgentQueryPlan plan = planner.Plan(new DataAgentQueryRequest(
            "Which documents describe DataAgent NL2SQL?",
            "developer",
            "en-US",
            false));

        Assert.Multiple(() =>
        {
            Assert.That(plan.Dataset, Is.EqualTo("document_index"));
            Assert.That(plan.Intent, Is.EqualTo("find_dataagent_documents"));
            Assert.That(plan.Filters, Has.Count.EqualTo(1));
            Assert.That(plan.Filters[0].Field, Is.EqualTo("tags"));
            Assert.That(plan.Filters[0].Operator, Is.EqualTo("contains"));
            Assert.That(plan.Filters[0].Value, Is.EqualTo("dataagent"));
        });
    }

    [Test]
    public void PlansUnknownProjectStateQuestionAsMissingRequiredGateQuery()
    {
        DeterministicDataAgentQueryPlanner planner = new();

        DataAgentQueryPlan plan = planner.Plan(new DataAgentQueryRequest(
            "What project state still needs attention?",
            "developer",
            "en-US",
            false));

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
    }

    [Test]
    public void RoleLocaleAndLiveFlagsDoNotChangeDeterministicPlannerOutput()
    {
        DeterministicDataAgentQueryPlanner planner = new();

        DataAgentQueryPlan developerPlan = planner.Plan(new DataAgentQueryRequest(
            "Which documents describe DataAgent NL2SQL?",
            "developer",
            "zh-CN",
            false));
        DataAgentQueryPlan analystPlan = planner.Plan(new DataAgentQueryRequest(
            "Which documents describe DataAgent NL2SQL?",
            "analyst",
            "ja-JP",
            true));

        Assert.Multiple(() =>
        {
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
