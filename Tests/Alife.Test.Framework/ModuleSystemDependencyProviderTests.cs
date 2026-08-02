using Alife.Framework;
using Microsoft.Extensions.Logging.Abstractions;

namespace Alife.Test.Framework;

public class ModuleSystemDependencyProviderTests
{
    [Test]
    public void ModuleSystemIncludesOpenAILanguageModelByDefault()
    {
        ModuleSystem moduleSystem = new(new StorageSystem(), new NullLogger<ModuleSystem>());

        Type? module = moduleSystem.GetModule(typeof(OpenAILanguageModel).FullName!);

        Assert.That(module, Is.EqualTo(typeof(OpenAILanguageModel)));
    }

    [Test]
    [NonParallelizable]
    public void ModuleSystemFallsBackToBuiltInsWhenPluginCompilationFails()
    {
        string previousStorage = Alife.Platform.AlifePath.StorageFolderPath;
        string storage = Path.Combine(Path.GetTempPath(), "alife-module-fallback-tests", Guid.NewGuid().ToString("N"));
        string plugin = Path.Combine(storage, "Plugins", "Broken.Plugin");
        Directory.CreateDirectory(plugin);
        File.WriteAllText(Path.Combine(plugin, "Broken.cs"), "public class Broken { invalid }");

        try
        {
            Alife.Platform.AlifePath.SetStorageFolderPath(storage, persist: false);
            ModuleSystem moduleSystem = new(new StorageSystem(), new NullLogger<ModuleSystem>());

            Assert.That(
                moduleSystem.GetModule(typeof(OpenAILanguageModel).FullName!),
                Is.EqualTo(typeof(OpenAILanguageModel)));
        }
        finally
        {
            Alife.Platform.AlifePath.SetStorageFolderPath(previousStorage, persist: false);
            Directory.Delete(storage, recursive: true);
        }
    }
}
