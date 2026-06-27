using Alife.Function.DesktopControl;

namespace Alife.Test.DesktopControl;

public sealed class DesktopActionAuditLogServiceTests
{
    [Test]
    public void Record_PersistsEntriesAndReloadsRecentEntries()
    {
        string root = Path.Combine(Path.GetTempPath(), "alife-desktop-action-audit-tests", Guid.NewGuid().ToString("N"));
        string path = Path.Combine(root, "desktop-action-audit.jsonl");
        DesktopActionAuditLogService service = new(path, maxRetainedEntries: 2);

        service.Record(new DesktopActionAuditEntry(
            DateTimeOffset.Parse("2026-06-20T12:00:00+08:00"),
            "qchat.desktop.status",
            3045846738,
            "xiayu",
            DesktopCapabilityRisk.ReadOnly,
            true,
            "desktop_status=ok"));
        service.Record(new DesktopActionAuditEntry(
            DateTimeOffset.Parse("2026-06-20T12:01:00+08:00"),
            "qchat.desktop.processes",
            3045846738,
            "xiayu",
            DesktopCapabilityRisk.ReadOnly,
            true,
            "processes_shown=20"));
        service.Record(new DesktopActionAuditEntry(
            DateTimeOffset.Parse("2026-06-20T12:02:00+08:00"),
            "qchat.desktop.open",
            3045846738,
            "xiayu",
            DesktopCapabilityRisk.Low,
            false,
            "desktop_mutation=disabled"));

        DesktopActionAuditLogService reloaded = new(path, maxRetainedEntries: 2);
        IReadOnlyList<DesktopActionAuditEntry> entries = reloaded.GetRecentEntries(10);

        Assert.Multiple(() =>
        {
            Assert.That(File.Exists(path), Is.True);
            Assert.That(entries, Has.Count.EqualTo(2));
            Assert.That(entries[0].ActionName, Is.EqualTo("qchat.desktop.processes"));
            Assert.That(entries[0].Risk, Is.EqualTo(DesktopCapabilityRisk.ReadOnly));
            Assert.That(entries[1].ActionName, Is.EqualTo("qchat.desktop.open"));
            Assert.That(entries[1].Succeeded, Is.False);
            Assert.That(entries[1].Message, Is.EqualTo("desktop_mutation=disabled"));
        });
    }

    [Test]
    public void Record_SanitizesMessagesBeforePersisting()
    {
        string root = Path.Combine(Path.GetTempPath(), "alife-desktop-action-audit-tests", Guid.NewGuid().ToString("N"));
        string path = Path.Combine(root, "desktop-action-audit.jsonl");
        DesktopActionAuditLogService service = new(path);

        service.Record(new DesktopActionAuditEntry(
            DateTimeOffset.Parse("2026-06-20T12:00:00+08:00"),
            " qchat.desktop.status ",
            3045846738,
            " xiayu ",
            DesktopCapabilityRisk.ReadOnly,
            true,
            "line1\r\nline2\t" + new string('x', 260)));

        string line = File.ReadAllText(path);
        DesktopActionAuditLogService reloaded = new(path);
        DesktopActionAuditEntry entry = reloaded.GetRecentEntries(1).Single();

        Assert.Multiple(() =>
        {
            Assert.That(line, Does.Not.Contain("line1\r\nline2"));
            Assert.That(entry.ActionName, Is.EqualTo("qchat.desktop.status"));
            Assert.That(entry.AgentId, Is.EqualTo("xiayu"));
            Assert.That(entry.Message, Does.Contain("line1 line2"));
            Assert.That(entry.Message.Length, Is.LessThanOrEqualTo(200));
        });
    }
}
