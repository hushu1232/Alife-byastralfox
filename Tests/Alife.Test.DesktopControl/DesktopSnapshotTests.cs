using Alife.Function.DesktopControl;

namespace Alife.Test.DesktopControl;

public sealed class DesktopSnapshotTests
{
    [Test]
    public void FormatCompact_IncludesCountsAndHealthWithoutProcessDetails()
    {
        DesktopSnapshot snapshot = new(
            DateTimeOffset.Parse("2026-06-20T12:00:00+08:00"),
            new SystemHealthSnapshot(8, 16384, 8192, 512000, 256000),
            [
                new ProcessSnapshot(10, "Alife.Client", 120),
                new ProcessSnapshot(20, "msedge", 300)
            ],
            [
                new WindowSnapshot(100, "Alife", "Alife.Client"),
                new WindowSnapshot(200, "QQ", "QQ")
            ],
            []);

        string text = snapshot.FormatCompact();

        Assert.That(text, Does.Contain("desktop_status=ok"));
        Assert.That(text, Does.Contain("processes=2"));
        Assert.That(text, Does.Contain("windows=2"));
        Assert.That(text, Does.Contain("memory_used_mb=8192"));
        Assert.That(text, Does.Not.Contain("Alife.Client"));
        Assert.That(text, Does.Not.Contain("msedge"));
    }
}
