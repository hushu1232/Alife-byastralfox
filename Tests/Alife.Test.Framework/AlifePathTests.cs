using Alife.Platform;

namespace Alife.Test.Framework;

public class AlifePathTests
{
    [Test]
    public void SetStorageFolderPathCanAvoidPersistingRuntimeStorageOverride()
    {
        string previousStorage = AlifePath.StorageFolderPath;
        string storageFile = Path.Combine(AlifePath.RuntimeFolderPath, "storage_path.txt");
        string? previousFileContent = File.Exists(storageFile)
            ? File.ReadAllText(storageFile)
            : null;
        string storageRoot = Path.Combine(Path.GetTempPath(), "alife-path-tests", Guid.NewGuid().ToString("N"));

        try
        {
            Directory.CreateDirectory(storageRoot);

            AlifePath.SetStorageFolderPath(storageRoot, persist: false);

            Assert.That(AlifePath.StorageFolderPath, Is.EqualTo(storageRoot));
            string? currentFileContent = File.Exists(storageFile)
                ? File.ReadAllText(storageFile)
                : null;
            Assert.That(currentFileContent, Is.EqualTo(previousFileContent));
        }
        finally
        {
            AlifePath.SetStorageFolderPath(previousStorage, persist: false);
            if (previousFileContent == null)
            {
                if (File.Exists(storageFile))
                    File.Delete(storageFile);
            }
            else
            {
                File.WriteAllText(storageFile, previousFileContent);
            }
        }
    }
}
