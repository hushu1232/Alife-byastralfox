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
}
