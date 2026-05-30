using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace Alife.Framework;

public class ChatActivitySystem
{
    public event Action<ChatActivity>? Created;
    public event Action<ChatActivity>? Destroyed;

    public IEnumerable<ChatActivity> GetAllChatActivities()
    {
        return activities.Values;
    }

    public bool IsActivated(Character character)
    {
        return activities.ContainsKey(character.Name);
    }

    public async Task<ChatActivity> Play(Character character, IProgress<(string, float)>? progress = null)
    {
        ChatActivity chatActivity = await ChatActivity.Create(
        character, configurationSystem, pluginSystem, progress,
        appendObjects.ToArray()
        );

        activities.Add(character.Name, chatActivity);
        Created?.Invoke(chatActivity);
        await chatActivity.Launch(progress);

        return chatActivity;
    }

    public async Task Stop(Character character)
    {
        ChatActivity chatActivity = activities[character.Name];
        await chatActivity.DisposeAsync();
        activities.Remove(character.Name);
        Destroyed?.Invoke(chatActivity);
    }

    public ChatActivitySystem(
        CharacterSystem characterSystem,
        ConfigurationSystem configurationSystem,
        PluginSystem pluginSystem,
        StorageSystem storageSystem)
    {
        appendObjects.Add(characterSystem);
        appendObjects.Add(configurationSystem);
        appendObjects.Add(pluginSystem);
        appendObjects.Add(storageSystem);
        appendObjects.Add(this);
        this.pluginSystem = pluginSystem;
        this.configurationSystem = configurationSystem;
    }

    readonly PluginSystem pluginSystem;
    readonly ConfigurationSystem configurationSystem;
    readonly List<object> appendObjects = new();
    readonly Dictionary<string, ChatActivity> activities = new();
}
