using System.Collections.Generic;
using System.Threading.Tasks;

namespace Alife.Function.QChat;

/// <summary>
/// 基础 API 扩展，提供最常用的消息发送与文件处理功能。
/// </summary>
public static class OneBotExtensions
{
    public static async Task SendPrivateMessage(this OneBotClient client, long userId, string message)
    {
        await client.CallActionAsync<object>("send_private_msg", new { user_id = userId, message });
    }
    public static Task<OneBotSendMessageResult?> SendPrivateMessageWithResult(this OneBotClient client, long userId, string message)
    {
        return client.CallActionAsync<OneBotSendMessageResult>("send_private_msg", new { user_id = userId, message });
    }
    public static async Task SendGroupMessage(this OneBotClient client, long groupId, string message)
    {
        await client.CallActionAsync<object>("send_group_msg", new { group_id = groupId, message });
    }
    public static Task<OneBotSendMessageResult?> SendGroupMessageWithResult(this OneBotClient client, long groupId, string message)
    {
        return client.CallActionAsync<OneBotSendMessageResult>("send_group_msg", new { group_id = groupId, message });
    }
    public static Task DeleteMessage(this OneBotClient client, long messageId)
    {
        return client.CallActionAsync<object>("delete_msg", new { message_id = messageId });
    }
    public static Task PokePrivate(this OneBotClient client, long userId)
    {
        return client.CallActionAsync<object>("friend_poke", new { user_id = userId });
    }
    public static Task PokeGroup(this OneBotClient client, long groupId, long userId)
    {
        return client.CallActionAsync<object>("group_poke", new { group_id = groupId, user_id = userId });
    }
    public static async Task UploadPrivateFile(this OneBotClient client, long userId, string filePath, string name)
    {
        await client.CallActionAsync<object>("upload_private_file", new UploadFileParams { UserId = userId, File = filePath, Name = name });
    }
    public static async Task UploadGroupFile(this OneBotClient client, long groupId, string filePath, string name)
    {
        await client.CallActionAsync<object>("upload_group_file", new UploadFileParams { GroupId = groupId, File = filePath, Name = name });
    }

    /// <summary>
    /// 获取私聊文件下载链接。
    /// </summary>
    public static async Task<OneBotFile?> GetPrivateFileUrl(this OneBotClient client, string fileId)
    {
        return await client.CallActionAsync<OneBotFile>("get_private_file_url", new { file_id = fileId });
    }
    /// <summary>
    /// 获取群文件下载链接。
    /// </summary>
    public static async Task<OneBotFile?> GetGroupFileUrl(this OneBotClient client, long groupId, string fileId)
    {
        return await client.CallActionAsync<OneBotFile>("get_group_file_url", new { group_id = groupId, file_id = fileId });
    }
    /// <summary>
    /// 根据消息 ID 获取消息详情。
    /// </summary>
    public static async Task<OneBotMessageEvent?> GetMessage(this OneBotClient client, long messageId)
    {
        return await client.CallActionAsync<OneBotMessageEvent>("get_msg", new { message_id = messageId });
    }
    /// <summary>
    /// 获取合并转发消息详情。
    /// </summary>
    public static async Task<List<OneBotForwardMessage>?> GetForwardMessage(this OneBotClient client, string forwardId)
    {
        OneBotForwardData? data = await client.CallActionAsync<OneBotForwardData>("get_forward_msg", new { id = forwardId });
        return data?.Messages;
    }

    public static async Task<IReadOnlyList<OneBotGroupMember>> GetGroupMemberList(this OneBotClient client, long groupId)
    {
        List<OneBotGroupMember>? members = await client.CallActionAsync<List<OneBotGroupMember>>(
            "get_group_member_list",
            new { group_id = groupId });
        return members ?? [];
    }

    public static async Task<IReadOnlyList<OneBotGroupInfo>> GetGroupList(this OneBotClient client)
    {
        List<OneBotGroupInfo>? groups = await client.CallActionAsync<List<OneBotGroupInfo>>(
            "get_group_list",
            new { });
        return groups ?? [];
    }
}
