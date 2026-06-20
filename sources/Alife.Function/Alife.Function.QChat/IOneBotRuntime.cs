using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace Alife.Function.QChat;

public interface IOneBotRuntime : IAsyncDisposable
{
    event Action<OneBotBaseEvent>? EventReceived;
    long BotId { get; }
    bool IsConnected { get; }
    string Url { get; set; }
    string Token { get; set; }
    Task ConnectAsync();
    Task SendGroupMessage(long groupId, string message);
    Task SendPrivateMessage(long userId, string message);
    Task<T?> CallActionAsync<T>(string action, object? parameters = null) =>
        throw new NotSupportedException("This OneBot runtime does not support generic action calls.");
    async Task<OneBotSendMessageResult?> SendGroupMessageWithResult(long groupId, string message)
    {
        await SendGroupMessage(groupId, message);
        return null;
    }
    async Task<OneBotSendMessageResult?> SendPrivateMessageWithResult(long userId, string message)
    {
        await SendPrivateMessage(userId, message);
        return null;
    }
    Task DeleteMessage(long messageId) => Task.CompletedTask;
    Task PokePrivate(long userId) => Task.CompletedTask;
    Task PokeGroup(long groupId, long userId) => Task.CompletedTask;
    Task UploadGroupFile(long groupId, string filePath, string name);
    Task UploadPrivateFile(long userId, string filePath, string name);
    Task<OneBotFile?> GetPrivateFileUrl(string fileId);
    Task<OneBotFile?> GetGroupFileUrl(long groupId, string fileId);
    Task<OneBotMessageEvent?> GetMessage(long messageId);
    Task<List<OneBotForwardMessage>?> GetForwardMessage(string forwardId);
    Task<IReadOnlyList<OneBotGroupInfo>> GetGroupList();
    Task<IReadOnlyList<OneBotGroupMember>> GetGroupMemberList(long groupId);
}

public sealed class OneBotRuntime(OneBotClient client) : IOneBotRuntime
{
    public event Action<OneBotBaseEvent>? EventReceived
    {
        add => client.EventReceived += value;
        remove => client.EventReceived -= value;
    }

    public long BotId => client.BotId;
    public bool IsConnected => client.IsConnected;
    public string Url { get => client.Url; set => client.Url = value; }
    public string Token { get => client.Token; set => client.Token = value; }
    public Task ConnectAsync() => client.ConnectAsync();
    public Task SendGroupMessage(long groupId, string message) => client.SendGroupMessage(groupId, message);
    public Task SendPrivateMessage(long userId, string message) => client.SendPrivateMessage(userId, message);
    public Task<T?> CallActionAsync<T>(string action, object? parameters = null) => client.CallActionAsync<T>(action, parameters);
    public Task<OneBotSendMessageResult?> SendGroupMessageWithResult(long groupId, string message) => client.SendGroupMessageWithResult(groupId, message);
    public Task<OneBotSendMessageResult?> SendPrivateMessageWithResult(long userId, string message) => client.SendPrivateMessageWithResult(userId, message);
    public Task DeleteMessage(long messageId) => client.DeleteMessage(messageId);
    public Task PokePrivate(long userId) => client.PokePrivate(userId);
    public Task PokeGroup(long groupId, long userId) => client.PokeGroup(groupId, userId);
    public Task UploadGroupFile(long groupId, string filePath, string name) => client.UploadGroupFile(groupId, filePath, name);
    public Task UploadPrivateFile(long userId, string filePath, string name) => client.UploadPrivateFile(userId, filePath, name);
    public Task<OneBotFile?> GetPrivateFileUrl(string fileId) => client.GetPrivateFileUrl(fileId);
    public Task<OneBotFile?> GetGroupFileUrl(long groupId, string fileId) => client.GetGroupFileUrl(groupId, fileId);
    public Task<OneBotMessageEvent?> GetMessage(long messageId) => client.GetMessage(messageId);
    public Task<List<OneBotForwardMessage>?> GetForwardMessage(string forwardId) => client.GetForwardMessage(forwardId);
    public Task<IReadOnlyList<OneBotGroupInfo>> GetGroupList() => client.GetGroupList();
    public Task<IReadOnlyList<OneBotGroupMember>> GetGroupMemberList(long groupId) => client.GetGroupMemberList(groupId);
    public ValueTask DisposeAsync() => client.DisposeAsync();
}
