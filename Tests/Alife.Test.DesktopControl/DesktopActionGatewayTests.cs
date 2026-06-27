using Alife.Function.DesktopControl;

namespace Alife.Test.DesktopControl;

public sealed class DesktopActionGatewayTests
{
    [Test]
    public async Task ExecuteAsync_DeniesUnknownActionWithoutExecution()
    {
        FakeAuditSink audit = new();
        DesktopActionGateway gateway = new([], audit);

        DesktopActionResult result = await gateway.ExecuteAsync(new DesktopActionRequest(
            "unknown.action",
            ActorUserId: 3045846738,
            AgentId: "xiayu",
            IsOwner: true));

        Assert.Multiple(() =>
        {
            Assert.That(result.Executed, Is.False);
            Assert.That(result.Status, Is.EqualTo(DesktopActionStatus.Denied));
            Assert.That(result.Message, Does.Contain("unknown_action"));
            Assert.That(audit.Entries, Has.Count.EqualTo(1));
            Assert.That(audit.Entries.Single().Succeeded, Is.False);
            Assert.That(audit.Entries.Single().ActionName, Is.EqualTo("unknown.action"));
        });
    }

    [Test]
    public async Task ExecuteAsync_DeniesNonOwnerWithoutExecution()
    {
        FakeDesktopAction action = new("desktop.read", DesktopCapabilityRisk.ReadOnly);
        DesktopActionGateway gateway = new([action], new FakeAuditSink());

        DesktopActionResult result = await gateway.ExecuteAsync(new DesktopActionRequest(
            "desktop.read",
            ActorUserId: 100200300,
            AgentId: "xiayu",
            IsOwner: false));

        Assert.Multiple(() =>
        {
            Assert.That(result.Executed, Is.False);
            Assert.That(result.Status, Is.EqualTo(DesktopActionStatus.Denied));
            Assert.That(result.Message, Does.Contain("owner"));
            Assert.That(action.ExecutionCount, Is.Zero);
        });
    }

    [Test]
    public async Task ExecuteAsync_DeniesNonXiaYuAgentWithoutExecution()
    {
        FakeDesktopAction action = new("desktop.read", DesktopCapabilityRisk.ReadOnly);
        DesktopActionGateway gateway = new([action], new FakeAuditSink());

        DesktopActionResult result = await gateway.ExecuteAsync(new DesktopActionRequest(
            "desktop.read",
            ActorUserId: 3045846738,
            AgentId: "mixu",
            IsOwner: true));

        Assert.Multiple(() =>
        {
            Assert.That(result.Executed, Is.False);
            Assert.That(result.Status, Is.EqualTo(DesktopActionStatus.Denied));
            Assert.That(result.Message, Does.Contain("xiayu"));
            Assert.That(action.ExecutionCount, Is.Zero);
        });
    }

    [Test]
    public async Task ExecuteAsync_DeniesMutatingActionWhenMutationDisabled()
    {
        FakeDesktopAction action = new("desktop.open", DesktopCapabilityRisk.Low);
        DesktopActionGateway gateway = new(
            [action],
            new FakeAuditSink(),
            new DesktopCapabilityRegistry([]));

        DesktopActionResult result = await gateway.ExecuteAsync(new DesktopActionRequest(
            "desktop.open",
            ActorUserId: 3045846738,
            AgentId: "xiayu",
            IsOwner: true));

        Assert.Multiple(() =>
        {
            Assert.That(result.Executed, Is.False);
            Assert.That(result.Status, Is.EqualTo(DesktopActionStatus.Denied));
            Assert.That(result.Message, Does.Contain("desktop_mutation=disabled"));
            Assert.That(action.ExecutionCount, Is.Zero);
        });
    }

    [Test]
    public async Task ExecuteAsync_ExecutesRegisteredReadOnlyActionForOwnerXiaYu()
    {
        FakeDesktopAction action = new("desktop.read", DesktopCapabilityRisk.ReadOnly, "read-result");
        FakeAuditSink audit = new();
        DesktopActionGateway gateway = new([action], audit);

        DesktopActionResult result = await gateway.ExecuteAsync(new DesktopActionRequest(
            "desktop.read",
            ActorUserId: 3045846738,
            AgentId: "xiayu",
            IsOwner: true));

        Assert.Multiple(() =>
        {
            Assert.That(result.Executed, Is.True);
            Assert.That(result.Status, Is.EqualTo(DesktopActionStatus.Executed));
            Assert.That(result.Message, Is.EqualTo("read-result"));
            Assert.That(result.Risk, Is.EqualTo(DesktopCapabilityRisk.ReadOnly));
            Assert.That(action.ExecutionCount, Is.EqualTo(1));
            Assert.That(audit.Entries, Has.Count.EqualTo(1));
            Assert.That(audit.Entries.Single().Succeeded, Is.True);
        });
    }

    sealed class FakeDesktopAction(
        string name,
        DesktopCapabilityRisk risk,
        string result = "ok") : IDesktopAction
    {
        public string Name { get; } = name;
        public DesktopCapabilityRisk Risk { get; } = risk;
        public bool Enabled { get; init; } = true;
        public string Summary { get; init; } = "fake test action";
        public int ExecutionCount { get; private set; }

        public Task<string> ExecuteAsync(DesktopActionRequest request, CancellationToken cancellationToken = default)
        {
            ExecutionCount++;
            return Task.FromResult(result);
        }
    }

    sealed class FakeAuditSink : IDesktopActionAuditSink
    {
        public List<DesktopActionAuditEntry> Entries { get; } = new();

        public void Record(DesktopActionAuditEntry entry)
        {
            Entries.Add(entry);
        }
    }
}
