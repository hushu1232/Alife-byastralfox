using Alife.Framework;
using Alife.Function.Agent;
using Alife.Function.FunctionCaller;
using Alife.Function.MessageFilter;
using Alife.Function.QChat;
using Alife.Platform;
using Autofac;
using Microsoft.Extensions.Logging.Abstractions;

namespace Alife.Test.Framework;

public class ChatActivityActivationDiagnosticsTests
{
    [Test]
    public async Task ActivateWritesDiagnosticsWhenCharacterActivationFails()
    {
        await WithTemporaryStorageAsync(async () => {
            string logPath = Path.Combine(AlifePath.StorageFolderPath, "AgentWorkspace", "activation-diagnostics.jsonl");
            if (File.Exists(logPath))
                File.Delete(logPath);

            StorageSystem storage = new();
            CharacterSystem characterSystem = new(storage);
            ConfigurationSystem configurationSystem = new(storage);
            ModuleSystem moduleSystem = new(storage, new NullLogger<ModuleSystem>());
            ChatActivitySystem activitySystem = new(characterSystem, configurationSystem, moduleSystem, storage);
            Character character = characterSystem.CreateCharacter($"ActivationFailureProbe-{Guid.NewGuid():N}");
            character.Modules.Clear();
            characterSystem.SaveCharacter(character);

            await activitySystem.Activate(character);

            Assert.That(File.Exists(logPath), Is.True);
            string diagnostics = await File.ReadAllTextAsync(logPath);
            Assert.That(diagnostics, Does.Contain(character.Name));
            Assert.That(diagnostics, Does.Contain("activation-failed"));
        });
    }

    [Test]
    public async Task ActivateWritesDiagnosticsForUnknownCharacterModules()
    {
        await WithTemporaryStorageAsync(async () => {
            string logPath = Path.Combine(AlifePath.StorageFolderPath, "AgentWorkspace", "activation-diagnostics.jsonl");
            if (File.Exists(logPath))
                File.Delete(logPath);

            StorageSystem storage = new();
            CharacterSystem characterSystem = new(storage);
            ConfigurationSystem configurationSystem = new(storage);
            ModuleSystem moduleSystem = new(storage, new NullLogger<ModuleSystem>());
            ChatActivitySystem activitySystem = new(characterSystem, configurationSystem, moduleSystem, storage);
            Character character = characterSystem.CreateCharacter($"UnknownModuleProbe-{Guid.NewGuid():N}");
            character.Modules.Clear();
            character.Modules.Add("Alife.Function.Agent.MissingCapabilityService");
            characterSystem.SaveCharacter(character);

            await activitySystem.Activate(character);

            Assert.That(File.Exists(logPath), Is.True);
            string diagnostics = await File.ReadAllTextAsync(logPath);
            Assert.That(diagnostics, Does.Contain(character.Name));
            Assert.That(diagnostics, Does.Contain("activation-missing-modules"));
            Assert.That(diagnostics, Does.Contain("Alife.Function.Agent.MissingCapabilityService"));
        });
    }

    [Test]
    public async Task ModuleContainerResolvesSelfContextDiagnosticsAndQChatWithoutCircularDependency()
    {
        await WithTemporaryStorageAsync(async () => {
            Character character = new() { Name = $"CircularDependencyProbe-{Guid.NewGuid():N}" };
            Type[] moduleTypes =
            [
                typeof(XmlFunctionCaller),
                typeof(MessageFilterService),
                typeof(SelfContextService),
                typeof(AgentDiagnosticsService),
                typeof(AgentControlCenterService),
                typeof(QChatService)
            ];

            await using IContainer container = ChatActivity.BuildModuleContainer(
                moduleTypes,
                character,
                new ConfigurationSystem(new StorageSystem()));

            Assert.That(() => container.Resolve<MessageFilterService>(), Throws.Nothing);
            Assert.That(() => container.Resolve<SelfContextService>(), Throws.Nothing);
            Assert.That(() => container.Resolve<AgentDiagnosticsService>(), Throws.Nothing);
            Assert.That(() => container.Resolve<AgentControlCenterService>(), Throws.Nothing);
            Assert.That(() => container.Resolve<QChatService>(), Throws.Nothing);
        });
    }

    [Test]
    public async Task ActivateAutoActivateCharactersAttemptsOnlyAutoActivateCharacters()
    {
        await WithTemporaryStorageAsync(async () => {
            string logPath = Path.Combine(AlifePath.StorageFolderPath, "AgentWorkspace", "activation-diagnostics.jsonl");
            if (File.Exists(logPath))
                File.Delete(logPath);

            StorageSystem storage = new();
            CharacterSystem characterSystem = new(storage);
            ConfigurationSystem configurationSystem = new(storage);
            ModuleSystem moduleSystem = new(storage, new NullLogger<ModuleSystem>());
            ChatActivitySystem activitySystem = new(characterSystem, configurationSystem, moduleSystem, storage);

            Character autoCharacter = characterSystem.CreateCharacter($"AutoActivateProbe-{Guid.NewGuid():N}");
            autoCharacter.AutoActivate = true;
            autoCharacter.Modules.Clear();
            characterSystem.SaveCharacter(autoCharacter);

            Character manualCharacter = characterSystem.CreateCharacter($"ManualActivateProbe-{Guid.NewGuid():N}");
            manualCharacter.AutoActivate = false;
            manualCharacter.Modules.Clear();
            characterSystem.SaveCharacter(manualCharacter);

            await activitySystem.ActivateAutoActivateCharacters();

            Assert.That(File.Exists(logPath), Is.True);
            string diagnostics = await File.ReadAllTextAsync(logPath);
            Assert.That(diagnostics, Does.Contain(autoCharacter.Name));
            Assert.That(diagnostics, Does.Not.Contain(manualCharacter.Name));
        });
    }

    static async Task WithTemporaryStorageAsync(Func<Task> test)
    {
        string previousStorage = AlifePath.StorageFolderPath;
        string storageRoot = Path.Combine(Path.GetTempPath(), "alife-activation-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(storageRoot);
        try
        {
            AlifePath.SetStorageFolderPath(storageRoot, persist: false);
            await test();
        }
        finally
        {
            AlifePath.SetStorageFolderPath(previousStorage, persist: false);
        }
    }
}
