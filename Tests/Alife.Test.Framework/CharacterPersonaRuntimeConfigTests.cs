using System.Diagnostics;
using System.Text.Json;
using Alife.Framework;
using Alife.Function.Agent;
using Microsoft.Extensions.Logging.Abstractions;
using NUnit.Framework;

namespace Alife.Test.Framework;

[TestFixture]
public class CharacterPersonaRuntimeConfigTests
{
    [Test]
    public void ActivePersonaLoadsAnthropomorphicContextModules()
    {
        using JsonDocument document = JsonDocument.Parse(File.ReadAllText(GetActiveCharacterPath()));
        string[] modules = document.RootElement
            .GetProperty("Modules")
            .EnumerateArray()
            .Select(item => item.GetString() ?? string.Empty)
            .ToArray();

        foreach (string requiredModule in GetRequiredAnthropomorphicModules())
            Assert.That(modules, Does.Contain(requiredModule), $"Active persona should load {requiredModule}.");

        Assert.That(
            Array.IndexOf(modules, "Alife.Function.MessageFilter.LifeEventStreamService"),
            Is.LessThan(Array.IndexOf(modules, "Alife.Function.MessageFilter.MessageFilterService")),
            "Life events should be available before message context composition starts.");
        Assert.That(
            Array.IndexOf(modules, "Alife.Function.MessageFilter.SelfContextService"),
            Is.LessThan(Array.IndexOf(modules, "Alife.Function.MessageFilter.MessageFilterService")),
            "Self context should be available before message context composition starts.");
    }

    [Test]
    public void ActivePersonaModulesResolveInModuleSystem()
    {
        using JsonDocument document = JsonDocument.Parse(File.ReadAllText(GetActiveCharacterPath()));
        string[] modules = document.RootElement
            .GetProperty("Modules")
            .EnumerateArray()
            .Select(item => item.GetString() ?? string.Empty)
            .ToArray();
        PreloadRuntimeModuleAssemblies();
        ModuleSystem moduleSystem = new(new StorageSystem(), new NullLogger<ModuleSystem>());

        foreach (string module in modules)
            Assert.That(moduleSystem.GetModule(module), Is.Not.Null, $"Active persona module should resolve: {module}");
    }

    [Test]
    public void CharacterModuleRepairScriptIsSourceControlledAndCoversSafeAgentModules()
    {
        string scriptPath = Path.Combine(FindRepositoryRoot(), "tools", "repair-active-character-modules.ps1");
        Assert.That(File.Exists(scriptPath), Is.True);

        string script = File.ReadAllText(scriptPath);
        foreach (string module in GetRequiredAnthropomorphicModules())
            Assert.That(script, Does.Contain(module), $"Repair script should include {module}.");

        Assert.That(script, Does.Contain("Storage\\Character"));
        Assert.That(script, Does.Contain("-WhatIf"));
        Assert.That(script, Does.Not.Contain("sk-"));
        Assert.That(script, Does.Not.Contain("APIkey"));
        Assert.That(script, Does.Not.Contain("OneBotToken"));
        Assert.That(script, Does.Not.Contain("Storage\\AgentWorkspace"));
    }

    [Test]
    public void CharacterModuleRepairScriptRunsAgainstTemporaryCharacterStorage()
    {
        string repositoryRoot = FindRepositoryRoot();
        string scriptPath = Path.Combine(repositoryRoot, "tools", "repair-active-character-modules.ps1");
        string storagePath = Path.Combine(Path.GetTempPath(), "alife-character-module-repair-tests", Guid.NewGuid().ToString("N"));
        string characterRoot = Path.Combine(storagePath, "Character", "Probe");
        Directory.CreateDirectory(characterRoot);
        File.WriteAllText(
            Path.Combine(characterRoot, "index.json"),
            """
            {
              "Name": "Probe",
              "Modules": [
                "Alife.Function.MessageFilter.AgentDiagnosticsService"
              ]
            }
            """);

        using Process process = Process.Start(new ProcessStartInfo
        {
            FileName = "powershell",
            ArgumentList =
            {
                "-ExecutionPolicy",
                "Bypass",
                "-File",
                scriptPath,
                "-StoragePath",
                storagePath,
                "-CharacterName",
                "Probe"
            },
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        })!;
        process.WaitForExit(30000);
        string output = process.StandardOutput.ReadToEnd();
        string error = process.StandardError.ReadToEnd();

        Assert.That(process.ExitCode, Is.EqualTo(0), output + error);
        using JsonDocument repaired = JsonDocument.Parse(File.ReadAllText(Path.Combine(characterRoot, "index.json")));
        string[] modules = repaired.RootElement
            .GetProperty("Modules")
            .EnumerateArray()
            .Select(item => item.GetString() ?? string.Empty)
            .ToArray();

        Assert.That(modules, Does.Contain("Alife.Function.Agent.AgentDiagnosticsService"));
        Assert.That(modules, Does.Not.Contain("Alife.Function.MessageFilter.AgentDiagnosticsService"));
        Assert.That(modules, Does.Contain("Alife.Function.Agent.AgentControlCenterService"));
    }

    [Test]
    public void ActivePersonaPromptDoesNotEncourageSpammyFragmentedChat()
    {
        using JsonDocument document = JsonDocument.Parse(File.ReadAllText(GetActiveCharacterPath()));
        string prompt = document.RootElement.GetProperty("Prompt").GetString() ?? string.Empty;
        string qchatPrompt = File.ReadAllText(GetQChatConfigPath());

        Assert.That(prompt, Does.Not.Contain("每句话结尾必带"));
        Assert.That(prompt, Does.Not.Contain("分条发送"));
        Assert.That(prompt, Does.Not.Contain("每天都用，频繁地用"));
        Assert.That(prompt, Does.Not.Contain("主人和妈妈的私聊全回且必须第一时间回"));
        Assert.That(qchatPrompt, Does.Contain("完整句子后再发送"));
        Assert.That(qchatPrompt, Does.Contain("群聊要选择性回复"));
    }

    [Test]
    public void SelfModelPromptMarksRuntimeStateAsInternalNotUserFacingSpeech()
    {
        AgentSelfModelSnapshot snapshot = new(
            "雨宫咪绪",
            DateTimeOffset.Parse("2026-06-16T12:00:00+08:00"),
            new AgentStateSnapshot(
                "雨宫咪绪",
                IsChatting: false,
                PendingPokeCount: 0,
                ChatHistoryCount: 3,
                LastError: null,
                RecentEvents: [],
                ModuleHealth: [new ModuleHealth("QChat", ModuleHealthStatus.Healthy, "QQ is connected.")],
                Capabilities: [new AgentCapabilityInfo("QQ", EmbodiedCapabilityKind.Communication, "QQ channel.", "listening")]),
            Capabilities: [new AgentCapabilityInfo("QQ", EmbodiedCapabilityKind.Communication, "QQ channel.", "listening")],
            ModuleHealth: [new ModuleHealth("QChat", ModuleHealthStatus.Healthy, "QQ is connected.")],
            LatestTask: null,
            SafetyBoundaries: ["High-risk actions require owner confirmation."],
            RecentExperiences:
            [
                new LifeEvent(
                    DateTimeOffset.Parse("2026-06-16T11:59:00+08:00"),
                    LifeEventKind.Communication,
                    "QChat",
                    "Owner asked the bot to stay quiet.")
            ]);

        string prompt = AgentSelfModelService.FormatForPrompt(snapshot);

        Assert.That(prompt, Does.Contain("internal self context"));
        Assert.That(prompt, Does.Contain("Do not repeat these state labels"));
        Assert.That(prompt, Does.Contain("Use them only to choose whether to listen, remember, speak, act, or stay silent"));
        Assert.That(prompt, Does.Contain("Current felt situation"));
        Assert.That(prompt, Does.Contain("Social desire factors are internal"));
        Assert.That(prompt, Does.Contain("attention, fatigue, relationship weight, and conversation need"));
        Assert.That(prompt, Does.Contain("Recent lived experiences"));
    }

    static string GetActiveCharacterPath()
    {
        return Path.Combine(FindRepositoryRoot(), "Storage", "Character", "真央", "index.json");
    }

    static string GetQChatConfigPath()
    {
        return Path.Combine(
            FindRepositoryRoot(),
            "Storage",
            "Character",
            "真央",
            "Configuration",
            "Alife.Function.QChat.QChatService.json");
    }

    static string[] GetRequiredAnthropomorphicModules()
    {
        return
        [
            "Alife.Function.MessageFilter.LifeEventStreamService",
            "Alife.Function.MessageFilter.SystemHealthService",
            "Alife.Function.MessageFilter.SelfContextService",
            "Alife.Function.Agent.AgentDiagnosticsService",
            "Alife.Function.Agent.AgentCapabilityInventoryService",
            "Alife.Function.Agent.AgentSelfModelService",
            "Alife.Function.Agent.AgentIssueReportService",
            "Alife.Function.Agent.AgentTaskService",
            "Alife.Function.Agent.AgentWorkspaceService",
            "Alife.Function.Agent.AgentCommandService",
            "Alife.Function.Agent.AgentProjectStatusService",
            "Alife.Function.Agent.AgentMaintenanceService",
            "Alife.Function.Agent.AgentProactiveBehaviorService",
            "Alife.Function.Agent.AgentControlCenterService",
            "Alife.Function.MessageFilter.EmbodiedActionService",
            "Alife.Function.QChat.QChatRelationCacheService",
            "Alife.Function.Memory.AutobiographicalMemoryService"
        ];
    }

    static string FindRepositoryRoot()
    {
        DirectoryInfo? current = new(TestContext.CurrentContext.TestDirectory);
        while (current != null)
        {
            if (File.Exists(Path.Combine(current.FullName, "Alife.slnx")))
                return current.FullName;
            current = current.Parent;
        }

        throw new DirectoryNotFoundException("Could not locate Alife repository root.");
    }

    static void PreloadRuntimeModuleAssemblies()
    {
        _ = new[]
        {
            typeof(Alife.Framework.OpenAILanguageModel).Assembly,
            typeof(Alife.Function.QChat.QChatService).Assembly,
            typeof(Alife.Function.FunctionCaller.XmlFunctionCaller).Assembly,
            typeof(Alife.Function.Mcp.McpService).Assembly,
            typeof(Alife.Function.Skill.SkillService).Assembly,
            typeof(Alife.Function.Emotion.PADEmotionEngine).Assembly,
            typeof(Alife.Function.Developer.DeveloperService).Assembly,
            typeof(Alife.Function.Memory.MemoryService).Assembly,
            typeof(Alife.Function.MessageFilter.MessageFilterService).Assembly,
            typeof(Alife.Function.SystemEvent.SystemEventService).Assembly,
            typeof(Alife.Function.VirtualWorld.VirtualWorldService).Assembly
        };
    }
}
