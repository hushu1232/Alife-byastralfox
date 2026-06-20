using Alife.Function.DesktopControl;

namespace Alife.Test.DesktopControl;

public sealed class WindowsDesktopRuntimeReaderTests
{
    [Test]
    public async Task CaptureAsync_ReturnsSnapshotWithoutThrowing()
    {
        WindowsDesktopRuntimeReader reader = new();

        DesktopSnapshot snapshot = await reader.CaptureAsync();

        Assert.That(snapshot.CapturedAt, Is.Not.EqualTo(default(DateTimeOffset)));
        Assert.That(snapshot.Health.ProcessorCount, Is.GreaterThan(0));
        Assert.That(snapshot.Processes.Count, Is.GreaterThan(0));
        Assert.That(snapshot.Health.TotalDiskMb, Is.GreaterThanOrEqualTo(0));
        Assert.That(snapshot.Health.FreeDiskMb, Is.GreaterThanOrEqualTo(0));
    }
}
