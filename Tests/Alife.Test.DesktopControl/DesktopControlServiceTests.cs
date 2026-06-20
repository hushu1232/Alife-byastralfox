using Alife.Function.DesktopControl;

namespace Alife.Test.DesktopControl;

public sealed class DesktopControlServiceTests
{
    [Test]
    public async Task GetStatusAsync_ReturnsCompactSnapshot()
    {
        DesktopSnapshot snapshot = new(
            DateTimeOffset.Parse("2026-06-20T12:00:00+08:00"),
            new SystemHealthSnapshot(4, 8000, 3000, 100000, 50000),
            [new ProcessSnapshot(1, "Alife.Client", 100)],
            [new WindowSnapshot(1, "Alife", "Alife.Client")],
            []);
        DesktopControlService service = new(new FakeReader(snapshot));

        string status = await service.GetStatusAsync();

        Assert.That(status, Does.Contain("desktop_status=ok"));
        Assert.That(status, Does.Contain("processes=1"));
        Assert.That(status, Does.Contain("windows=1"));
    }

    [Test]
    public async Task GetProcessListAsync_LimitsOutputAndKeepsNoStackTrace()
    {
        DesktopSnapshot snapshot = new(
            DateTimeOffset.Parse("2026-06-20T12:00:00+08:00"),
            new SystemHealthSnapshot(4, 8000, 3000, 100000, 50000),
            Enumerable.Range(1, 20).Select(i => new ProcessSnapshot(i, $"proc-{i}", i)).ToArray(),
            [],
            []);
        DesktopControlService service = new(new FakeReader(snapshot));

        string text = await service.GetProcessListAsync(maxItems: 5);

        Assert.That(text.Split(Environment.NewLine), Has.Length.EqualTo(6));
        Assert.That(text, Does.Contain("processes_shown=5"));
        Assert.That(text, Does.Contain("proc-20"));
        Assert.That(text, Does.Not.Contain("proc-15"));
        Assert.That(text, Does.Not.Contain(" at "));
    }

    [Test]
    public async Task GetWindowListAsync_LimitsOutput()
    {
        DesktopSnapshot snapshot = new(
            DateTimeOffset.Parse("2026-06-20T12:00:00+08:00"),
            new SystemHealthSnapshot(4, 8000, 3000, 100000, 50000),
            [],
            [
                new WindowSnapshot(1, "Alife", "Alife.Client"),
                new WindowSnapshot(2, "QQ", "QQ"),
                new WindowSnapshot(3, "Browser", "msedge")
            ],
            []);
        DesktopControlService service = new(new FakeReader(snapshot));

        string text = await service.GetWindowListAsync(maxItems: 2);

        Assert.That(text.Split(Environment.NewLine), Has.Length.EqualTo(3));
        Assert.That(text, Does.Contain("windows_shown=2"));
        Assert.That(text, Does.Contain("Alife.Client: Alife"));
        Assert.That(text, Does.Contain("QQ: QQ"));
        Assert.That(text, Does.Not.Contain("Browser"));
    }

    sealed class FakeReader(DesktopSnapshot snapshot) : IDesktopRuntimeReader
    {
        public Task<DesktopSnapshot> CaptureAsync(CancellationToken cancellationToken = default)
        {
            return Task.FromResult(snapshot);
        }
    }
}
