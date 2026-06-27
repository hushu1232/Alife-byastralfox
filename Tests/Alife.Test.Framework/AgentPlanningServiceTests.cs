using Alife.Function.MessageFilter;
using NUnit.Framework;

namespace Alife.Test.Framework;

public class AgentPlanningServiceTests
{
    [Test]
    public void ShouldUsePlanner_ReturnsTrueForCodeAndLogMaintenanceRequests()
    {
        AgentPlanningService service = new(new AgentPlanningConfig
        {
            EnablePlanner = true,
            PlannerModelId = "deepseek-v4-pro",
            ExecutorModelId = "deepseek-v4-flash"
        });

        Assert.That(service.ShouldUsePlanner("检查 QChatService.cs 和日志，修复群聊状态外泄"), Is.True);
        Assert.That(service.ShouldUsePlanner("帮我看一下构建错误"), Is.True);
    }

    [Test]
    public void ShouldUsePlanner_ReturnsFalseForOrdinaryChat()
    {
        AgentPlanningService service = new(new AgentPlanningConfig
        {
            EnablePlanner = true,
            PlannerModelId = "deepseek-v4-pro",
            ExecutorModelId = "deepseek-v4-flash"
        });

        Assert.That(service.ShouldUsePlanner("晚安，羽"), Is.False);
        Assert.That(service.ShouldUsePlanner("你在干嘛"), Is.False);
    }

    [Test]
    public void BuildPlannerInstruction_IsReadOnlyAndAsksForExecutablePlan()
    {
        AgentPlanningService service = new(new AgentPlanningConfig
        {
            EnablePlanner = true,
            PlannerModelId = "deepseek-v4-pro",
            ExecutorModelId = "deepseek-v4-flash"
        });

        string instruction = service.BuildPlannerInstruction("修复文件下载链路");

        Assert.That(instruction, Does.Contain("read-only"));
        Assert.That(instruction, Does.Contain("do not execute tools that mutate files"));
        Assert.That(instruction, Does.Contain("return an execution plan"));
    }
}
