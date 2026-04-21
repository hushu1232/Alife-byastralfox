using Alife.Framework;
using System.Collections.Concurrent;

namespace Alife.Components.Services;

public class ChatMessage
{
    public string? Content { get; set; }
    public bool IsUser { get; set; }
    public bool IsInputting { get; set; }
}
/// <summary>
/// UI层的聊天消息状态管理。在角色激活后立即挂接事件，确保后台对话也能被记录。
/// 采用名称索引以确保在活动重启（Character对象被Clone）时记录依然能够持久。
/// </summary>
public class ChatMessageService
{
    public event Action<string>? OnUIMessageChanged;
    public event Action<string>? OnUIMessageSent;

    public List<ChatMessage> GetMessagesByName(string name)
    {
        return BotMessages.GetOrAdd(name, _ => new List<ChatMessage>());
    }

    public void ClearMessages(ChatActivity activity)
    {
        if (BotMessages.TryGetValue(activity.Character.Name, out List<ChatMessage>? list))
        {
            list.Clear();
        }
    }

    public ChatMessageService(ChatActivitySystem system)
    {
        system.Created += OnActivityCreated;
    }

    static readonly ConcurrentDictionary<string, List<ChatMessage>> BotMessages = new();
    static readonly HashSet<string> HookedActivityNames = new();

    /// <summary>
    /// 确保指定Activity的ChatBot事件已挂接到UI消息列表。
    /// 幂等操作，重复调用安全。
    /// </summary>
    void OnActivityCreated(ChatActivity activity)
    {
        string name = activity.Character.Name;
        List<ChatMessage> list = BotMessages.GetOrAdd(name, _ => new List<ChatMessage>());

        if (!HookedActivityNames.Add(name))
            return;

        activity.ChatBot.ChatSent += (obj) => {
            list.Add(new ChatMessage { Content = obj, IsUser = true });
            list.Add(new ChatMessage { IsUser = false, IsInputting = true });
            OnUIMessageSent?.Invoke(name);
            OnUIMessageChanged?.Invoke(name);
        };
        activity.ChatBot.ChatReceived += (obj) => {
            ChatMessage? aiMessage = list.LastOrDefault(m => m is { IsUser: false, IsInputting: true });
            if (aiMessage != null)
            {
                aiMessage.Content += obj;
                OnUIMessageChanged?.Invoke(name);
            }
        };
        activity.ChatBot.ChatOver += () => {
            ChatMessage? aiMessage = list.LastOrDefault(m => m is { IsUser: false, IsInputting: true });
            if (aiMessage != null)
            {
                aiMessage.IsInputting = false;
                OnUIMessageChanged?.Invoke(name);
            }
        };
    }
}
