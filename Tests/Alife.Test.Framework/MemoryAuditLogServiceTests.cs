using Alife.Function.Memory;

namespace Alife.Test.Framework;

public class MemoryAuditLogServiceTests
{
    [Test]
    public void RecordPersistsEntriesAndReloadsRecentEntries()
    {
        string root = Path.Combine(Path.GetTempPath(), "alife-memory-audit-tests", Guid.NewGuid().ToString("N"));
        string path = Path.Combine(root, "memory-audit.jsonl");
        MemoryAuditLogService service = new(path, maxRetainedEntries: 2);

        service.Record("insert", "MemoryService", "memory-1", "first", succeeded: true);
        service.Record("forget", "MemoryService", "memory-1", "removed from current context", succeeded: true);
        service.Record("insert", "MemoryService", "memory-2", "second", succeeded: false, error: "boom");

        MemoryAuditLogService reloaded = new(path, maxRetainedEntries: 2);
        IReadOnlyList<MemoryAuditLogEntry> entries = reloaded.GetRecentEntries(10);

        Assert.Multiple(() =>
        {
            Assert.That(File.Exists(path), Is.True);
            Assert.That(entries, Has.Count.EqualTo(2));
            Assert.That(entries[0].Action, Is.EqualTo("forget"));
            Assert.That(entries[0].MemoryName, Is.EqualTo("memory-1"));
            Assert.That(entries[1].Action, Is.EqualTo("insert"));
            Assert.That(entries[1].Succeeded, Is.False);
            Assert.That(entries[1].Error, Is.EqualTo("boom"));
        });
    }
}
