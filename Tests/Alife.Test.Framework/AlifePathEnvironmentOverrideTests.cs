using Alife.Platform;

namespace Alife.Test.Framework;

public class AlifePathEnvironmentOverrideTests
{
    [Test]
    public void ResolveLocalProductionPathsNormalizesFullyQualifiedOverrides()
    {
        string testRoot = Path.Combine(TestContext.CurrentContext.WorkDirectory, "alife-local-paths", Guid.NewGuid().ToString("N"));
        string runtime = Path.Combine(testRoot, "runtime", "..");
        string storage = Path.Combine(testRoot, "storage", "nested", "..");
        string temp = Path.Combine(testRoot, "temp", ".", "client");

        AlifeLocalPaths paths = AlifePath.ResolveLocalProductionPaths(runtime, storage, temp);

        Assert.Multiple(() =>
        {
            Assert.That(paths.RuntimeFolderPath, Is.EqualTo(Path.GetFullPath(runtime)));
            Assert.That(paths.StorageFolderPath, Is.EqualTo(Path.GetFullPath(storage)));
            Assert.That(paths.TempFolderPath, Is.EqualTo(Path.GetFullPath(temp)));
        });
    }

    [Test]
    public void ResolveLocalProductionPathsUsesProjectDefaultsForMissingOverrides()
    {
        AlifeLocalPaths paths = AlifePath.ResolveLocalProductionPaths(null, string.Empty, null);

        Assert.Multiple(() =>
        {
            Assert.That(paths.RuntimeFolderPath, Is.EqualTo(Path.Combine(AlifePath.RootFolderPath, "Runtime")));
            Assert.That(paths.StorageFolderPath, Is.EqualTo(Path.Combine(AlifePath.RootFolderPath, "Storage")));
            Assert.That(paths.TempFolderPath, Is.EqualTo(Path.Combine(AlifePath.RootFolderPath, ".tmp", "Alife.Client")));
        });
    }

    [TestCase("runtime")]
    [TestCase("storage")]
    [TestCase("temp")]
    public void ResolveLocalProductionPathsRejectsRelativeOverrides(string overriddenPath)
    {
        string root = TestContext.CurrentContext.WorkDirectory;

        TestDelegate resolve = overriddenPath switch
        {
            "runtime" => () => AlifePath.ResolveLocalProductionPaths("relative", null, null),
            "storage" => () => AlifePath.ResolveLocalProductionPaths(null, "relative", null),
            "temp" => () => AlifePath.ResolveLocalProductionPaths(null, null, "relative"),
            _ => throw new ArgumentOutOfRangeException(nameof(overriddenPath)),
        };

        ArgumentException exception = Assert.Throws<ArgumentException>(resolve)!;

        Assert.That(exception.Message, Does.Contain("absolute"));
    }

    [Test]
    public void ResolveLocalProductionPathsUsesProjectDefaultsForWhitespaceOverrides()
    {
        AlifeLocalPaths paths = AlifePath.ResolveLocalProductionPaths(" ", null, null);

        Assert.That(paths.RuntimeFolderPath, Is.EqualTo(Path.Combine(AlifePath.RootFolderPath, "Runtime")));
    }
}
