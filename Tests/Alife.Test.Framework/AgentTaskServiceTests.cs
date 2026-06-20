using Alife.Function.Agent;
using NUnit.Framework;

namespace Alife.Test.Framework;

public class AgentTaskServiceTests
{
    [Test]
    public void FormatStatus_ReturnsLatestTaskSummary()
    {
        string root = Path.Combine(TestContext.CurrentContext.WorkDirectory, "agent-task-test-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        AgentTaskService service = new(taskStorePath: Path.Combine(root, "agent-tasks.json"));
        AgentTaskState task = service.CreateTask("agent", "检查后台错误", ["读取 qchat diagnostics"]);
        service.StartTask(task.Id, "agent");
        service.RecordProgress(task.Id, "agent", "读取最新日志");

        string status = service.FormatStatus();

        Assert.That(status, Does.Contain(task.Id));
        Assert.That(status, Does.Contain("检查后台错误"));
        Assert.That(status, Does.Contain("读取最新日志"));
    }
}
