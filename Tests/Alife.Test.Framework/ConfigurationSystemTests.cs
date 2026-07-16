using Alife.Framework;
using Alife.Platform;

namespace Alife.Test.Framework;

public class ConfigurationSystemTests
{
    [Test]
    public void OpenAiLanguageModelDefaultConfigurationTargetsGrokThroughKrill()
    {
        OpenAILanguageModelConfig config = new();

        Assert.Multiple(() =>
        {
            Assert.That(config.endpoint, Is.EqualTo("https://api.krill-ai.com/v1"));
            Assert.That(config.modelId, Is.EqualTo("grok-4.5"));
            Assert.That(config.reasoningEffort, Is.Empty);
            Assert.That(config.extraBody, Is.EqualTo("{}"));
        });
    }

    [Test]
    public void GetConfiguration_ReplacesDefaultListInsteadOfMerging()
    {
        string previousStorage = AlifePath.StorageFolderPath;
        string storageRoot = Path.Combine(Path.GetTempPath(), "alife-configuration-system-tests", Guid.NewGuid().ToString("N"));
        try
        {
            AlifePath.SetStorageFolderPath(storageRoot, persist: false);
            StorageSystem storage = new();
            ConfigurationSystem configurationSystem = new(storage);

            storage.SetObject(
                Path.Combine("Configuration", typeof(ConfigurableModule).FullName!),
                new
                {
                    Rules = new[] { "configured-only" }
                });

            TestConfig config = (TestConfig)configurationSystem.GetConfiguration(typeof(ConfigurableModule))!;

            Assert.That(config.Rules, Is.EqualTo(new[] { "configured-only" }));
        }
        finally
        {
            AlifePath.SetStorageFolderPath(previousStorage, persist: false);
        }
    }

    sealed class ConfigurableModule : IConfigurable<TestConfig>
    {
        public TestConfig? Configuration { get; set; }
    }

    sealed record TestConfig
    {
        public List<string> Rules { get; set; } = ["default-a", "default-b"];
    }
}
