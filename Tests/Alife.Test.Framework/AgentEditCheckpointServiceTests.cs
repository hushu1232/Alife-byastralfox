using Alife.Function.MessageFilter;
using NUnit.Framework;

namespace Alife.Test.Framework;

public class AgentEditCheckpointServiceTests
{
    [Test]
    public void CaptureBeforeWrite_SavesOriginalContentOncePerTask()
    {
        string root = Path.Combine(TestContext.CurrentContext.WorkDirectory, "checkpoint-test-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        string file = Path.Combine(root, "config.json");
        File.WriteAllText(file, "old");
        AgentEditCheckpointService service = new(root);

        service.CaptureBeforeWrite("task-1", file);
        File.WriteAllText(file, "new");
        service.CaptureBeforeWrite("task-1", file);

        AgentEditCheckpoint checkpoint = service.GetCheckpoint("task-1")!;
        Assert.That(checkpoint.Files, Has.Count.EqualTo(1));
        Assert.That(File.ReadAllText(checkpoint.Files.Single().BackupPath), Is.EqualTo("old"));
    }

    [Test]
    public void Rollback_RestoresCapturedFile()
    {
        string root = Path.Combine(TestContext.CurrentContext.WorkDirectory, "checkpoint-test-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        string file = Path.Combine(root, "config.json");
        File.WriteAllText(file, "old");
        AgentEditCheckpointService service = new(root);

        service.CaptureBeforeWrite("task-2", file);
        File.WriteAllText(file, "new");

        AgentEditRollbackResult result = service.Rollback("task-2");

        Assert.That(result.RestoredFiles, Is.EqualTo(1));
        Assert.That(File.ReadAllText(file), Is.EqualTo("old"));
    }
}
