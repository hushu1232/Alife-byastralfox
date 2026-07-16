using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Alife.Platform;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json;

namespace Alife.Framework;

public class ChatActivitySystem
{
    /// <summary>角色激活进度更新（taskDescription, progressValue）</summary>
    public event Action<Character>? Activating;
    public event Action<Character, (string Task, float Progress)>? ActivatingProcess;
    public event Action<Character, Exception>? ActivationFailed;
    public event Action<ChatActivity>? ActivatingCreated;
    public event Action<ChatActivity>? Activated;
    public event Action<ChatActivity>? Destroying;
    public event Action<ChatActivity>? Destroyed;

    public IEnumerable<ChatActivity> GetAllChatActivities()
    {
        lock (activationGate)
            return activities.Values.ToArray();
    }

    public bool IsActivated(Character character)
    {
        lock (activationGate)
            return activities.ContainsKey(character.Name);
    }

    public ChatActivity? GetChatActivity(Character character)
    {
        lock (activationGate)
            return activities.GetValueOrDefault(character.Name);
    }

    public async Task ActivateAutoActivateCharacters()
    {
        List<Character> startingCharacters;
        lock (activationGate)
        {
            startingCharacters = characterSystem.GetAllCharacters()
                .Where(c => c.AutoActivate && TryReserveActivation(c))
                .ToList();
        }

        foreach (Character startingCharacter in startingCharacters)
            await ActivateReserved(startingCharacter);
    }

    /// <summary>
    /// 激活角色。UI 应通过订阅 Activating/Activated/ActivationFailed 事件来感知流程。
    /// </summary>
    public async Task Activate(Character character)
    {
        lock (activationGate)
        {
            if (!TryReserveActivation(character))
                return;
        }

        await ActivateReserved(character);
    }

    async Task ActivateReserved(Character character)
    {
        try
        {
            Progress<(string, float)> progress = new(tuple => {
                ActivatingProcess?.Invoke(character, tuple);
                WriteActivationDiagnostic(character, "activation-progress", tuple.Item1, progress: tuple.Item2);
            });

            characterSystem.LoadCharacter(character);

            string[] missingModules = GetMissingCharacterModules(character).ToArray();
            if (missingModules.Length > 0)
            {
                WriteActivationDiagnostic(
                    character,
                    "activation-missing-modules",
                    $"Character references {missingModules.Length} module(s) that are not available: {string.Join(", ", missingModules)}");
            }

            WriteActivationDiagnostic(character, "activation-start", "Character activation started.");
            Activating?.Invoke(character);
            ChatActivity chatActivity = await ChatActivity.Create(
                character, configurationSystem, moduleSystem, progress,
                appendObjects.ToArray()
            );
            ActivatingCreated?.Invoke(chatActivity);
            await chatActivity.Launch(progress);
            lock (activationGate)
                activities.Add(character.Name, chatActivity);
            WriteActivationDiagnostic(character, "activation-succeeded", "Character activation completed.");
            Activated?.Invoke(chatActivity);
        }
        catch (Exception ex)
        {
            WriteActivationDiagnostic(character, "activation-failed", ex.Message, ex);
            ActivationFailed?.Invoke(character, ex);
        }
        finally
        {
            lock (activationGate)
                activatingCharacters.Remove(character.Name);
        }
    }

    /// <summary>
    /// 销毁角色。UI 应通过订阅 Destroying/Destroyed 事件来感知流程。
    /// </summary>
    public async Task Deactivate(Character character)
    {
        ChatActivity? chatActivity;
        lock (activationGate)
        {
            if (!activities.TryGetValue(character.Name, out chatActivity))
                return;

            activities.Remove(character.Name);
        }

        Destroying?.Invoke(chatActivity);
        await chatActivity.DisposeAsync();
        Destroyed?.Invoke(chatActivity);
    }

    public ChatActivitySystem(
        CharacterSystem characterSystem,
        ConfigurationSystem configurationSystem,
        ModuleSystem moduleSystem,
        StorageSystem storageSystem)
    {
        appendObjects.Add(characterSystem);
        appendObjects.Add(configurationSystem);
        appendObjects.Add(moduleSystem);
        appendObjects.Add(storageSystem);
        appendObjects.Add(this);
        this.characterSystem = characterSystem;
        this.moduleSystem = moduleSystem;
        this.configurationSystem = configurationSystem;
        this.storageSystem = storageSystem;
    }

    readonly CharacterSystem characterSystem;
    readonly ModuleSystem moduleSystem;
    readonly ConfigurationSystem configurationSystem;
    readonly StorageSystem storageSystem;
    readonly List<object> appendObjects = new();
    readonly Dictionary<string, ChatActivity> activities = new();
    readonly HashSet<string> activatingCharacters = new(StringComparer.Ordinal);
    readonly object activationGate = new();

    bool TryReserveActivation(Character character)
    {
        if (activities.ContainsKey(character.Name))
            return false;

        return activatingCharacters.Add(character.Name);
    }

    IEnumerable<string> GetMissingCharacterModules(Character character)
    {
        return character.Modules
            .Where(moduleId => string.IsNullOrWhiteSpace(moduleId) == false)
            .Where(moduleId => moduleSystem.GetModule(moduleId) == null)
            .Distinct(StringComparer.Ordinal);
    }

    void WriteActivationDiagnostic(
        Character character,
        string eventName,
        string detail,
        Exception? exception = null,
        float? progress = null)
    {
        try
        {
            string path = Path.Combine(
                AlifePath.StorageFolderPath,
                "AgentWorkspace",
                "activation-diagnostics.jsonl");
            string? directory = Path.GetDirectoryName(path);
            if (string.IsNullOrWhiteSpace(directory) == false)
                Directory.CreateDirectory(directory);

            string line = JsonConvert.SerializeObject(new {
                timestamp = DateTimeOffset.Now,
                character = character.Name,
                eventName,
                detail,
                progress,
                exception = exception?.ToString()
            });
            File.AppendAllText(path, line + Environment.NewLine);
        }
        catch (Exception ex)
        {
            AlifeTerminal.LogWarning($"Failed to write activation diagnostics: {ex.Message}");
        }
    }
}
