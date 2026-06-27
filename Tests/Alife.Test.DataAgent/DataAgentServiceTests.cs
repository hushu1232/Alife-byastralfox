using Alife.Function.DataAgent;

namespace Alife.Test.DataAgent;

[TestFixture]
public sealed class DataAgentServiceTests
{
    [Test]
    public void AnswersMissingRequiredGateQuestionWithEvidenceBackedContext()
    {
        DataAgentService service = CreateService();

        DataAgentAnswer answer = service.Answer("当前还有哪些 required gate 没通过？");

        Assert.Multiple(() =>
        {
            Assert.That(answer.Dataset, Is.EqualTo("engineering_gate"));
            Assert.That(answer.RowCount, Is.EqualTo(0));
            Assert.That(answer.Summary, Does.Contain("0 required gate missing"));
            Assert.That(answer.Context, Does.Contain("[data_agent_context]"));
            Assert.That(answer.Context, Does.Contain("dataset=engineering_gate"));
            Assert.That(answer.Context, Does.Contain("sql_status=validated"));
            Assert.That(answer.Context, Does.Contain("[/data_agent_context]"));
        });
    }

    [Test]
    public void AnswersQChatTtsReadinessQuestion()
    {
        DataAgentService service = CreateService();

        DataAgentAnswer answer = service.Answer("哪些 readiness check 和 QChat 视图/TTS 有关？");

        Assert.Multiple(() =>
        {
            Assert.That(answer.Dataset, Is.EqualTo("runtime_readiness_check"));
            Assert.That(answer.RowCount, Is.EqualTo(1));
            Assert.That(answer.Summary, Does.Contain("MixuTts9881Reachable"));
            Assert.That(answer.Summary, Does.Contain("missing"));
            Assert.That(answer.Context, Does.Contain("tools/check-qchat-runtime-readiness.ps1"));
        });
    }

    [Test]
    public void AnswersRuntimeReadinessEvidenceQuestion()
    {
        DataAgentService service = CreateService();

        DataAgentAnswer answer = service.Answer("哪些测试证明 runtime readiness 是 required？");

        Assert.Multiple(() =>
        {
            Assert.That(answer.Dataset, Is.EqualTo("engineering_gate"));
            Assert.That(answer.RowCount, Is.EqualTo(1));
            Assert.That(answer.Summary, Does.Contain("Runtime readiness script"));
            Assert.That(answer.Summary, Does.Contain("tools/check-qchat-runtime-readiness.ps1"));
        });
    }

    [Test]
    public void AnswersLatestTestRunQuestion()
    {
        DataAgentService service = CreateService();

        DataAgentAnswer answer = service.Answer("最近一次测试通过、失败、跳过数量是多少？");

        Assert.Multiple(() =>
        {
            Assert.That(answer.Dataset, Is.EqualTo("test_run"));
            Assert.That(answer.Summary, Does.Contain("1168 passed"));
            Assert.That(answer.Summary, Does.Contain("0 failed"));
            Assert.That(answer.Summary, Does.Contain("10 skipped"));
        });
    }

    [Test]
    public void AnswersDataAgentDocumentQuestion()
    {
        DataAgentService service = CreateService();

        DataAgentAnswer answer = service.Answer("哪些文档和 DataAgent/NL2SQL 计划有关？");

        Assert.Multiple(() =>
        {
            Assert.That(answer.Dataset, Is.EqualTo("document_index"));
            Assert.That(answer.RowCount, Is.EqualTo(1));
            Assert.That(answer.Summary, Does.Contain("DataAgent NL2SQL Design"));
            Assert.That(answer.Context, Does.Contain("docs/superpowers/specs/2026-06-27-dataagent-nl2sql-design.md"));
        });
    }

    static DataAgentService CreateService()
    {
        string directory = Path.Combine(TestContext.CurrentContext.WorkDirectory, "dataagent-service-tests");
        Directory.CreateDirectory(directory);
        string databasePath = Path.Combine(directory, $"{Guid.NewGuid():N}.sqlite");
        DataAgentSchemaInitializer.Initialize(databasePath);
        DataAgentFixtureImporter.Import(databasePath);
        return new DataAgentService(databasePath);
    }
}
