using System.ComponentModel;
using System.Text;
using Alife.Framework;
using Alife.Function.Interpreter;
using Alife.Function.QChat;
using Microsoft.SemanticKernel;

namespace Alife.Implement;

public record QChatConfig
{
    public string Url { get; set; } = "ws://127.0.0.1:3001";
    public long OwnerId { get; set; }
    public bool IsGroupEnabled { get; set; } = true;
    public int AutoCloseMinutes { get; set; } = 30;
}
[Plugin("QQ聊天", "连接 OneBot v11 服务器，实现 QQ 消息收发及文件传输。")]
public class QChatService : Plugin, IAsyncDisposable, IConfigurable<QChatConfig>
{
    [XmlFunction]
    [Description("发送文本消息。（附加说明：群聊时可以用[CQ:at,qq=发送者ID]来显式回复某人）")]
    public async Task QChat(XmlExecutorContext ctx, [Description("通过私聊还是群聊发送")] OneBotMessageType type, [Description("QQ号或群号")] long target, [XmlContent] string _)
    {
        if (ctx.CallMode != CallMode.Closing)
            return;
        string content = ctx.FullContent.Trim();
        if (string.IsNullOrEmpty(content))
            return;
        if (target == 0)
            throw new ArgumentException("目标不能为空！", nameof(target));

        if (type == OneBotMessageType.Group)
            await oneBotClient.SendGroupMessage(target, content);
        else
            await oneBotClient.SendPrivateMessage(target, content);
    }

    [XmlFunction]
    [Description("发送图片或文件。")]
    public async Task QFile(XmlExecutorContext ctx, [Description("通过私聊还是群聊发送")] OneBotMessageType type, [Description("QQ号或群号")] long target, [Description("文件路径")] string file, bool isImage)
    {
        if (ctx.CallMode != CallMode.OneShot)
            return;
        file = file.Trim();
        if (string.IsNullOrEmpty(file))
            return;

        file = file.Replace('\\', '/');
        if (isImage)
        {
            if (type == OneBotMessageType.Group)
                await oneBotClient.SendGroupImage(target, file);
            else
                await oneBotClient.SendPrivateImage(target, file);
        }
        else
        {
            string fileName = Path.GetFileName(file);
            if (type == OneBotMessageType.Group)
                await oneBotClient.UploadGroupFile(target, file, fileName);
            else
                await oneBotClient.UploadPrivateFile(target, file, fileName);
        }
    }

    [XmlFunction]
    [Description("设置群消息监听开关。")]
    public void QGroupSwitch(XmlExecutorContext ctx, bool enabled)
    {
        if (ctx.CallMode != CallMode.Closing && ctx.CallMode != CallMode.OneShot) return;
        config.IsGroupEnabled = enabled;
        chatActivity.ChatBot.Poke($"[QChatService] 群消息监听已{(enabled ? "开启" : "关闭")}");
    }

    OneBotClient oneBotClient = null!;
    readonly StorageSystem storageSystem;
    QChatConfig config = null!;
    ChatActivity chatActivity = null!;
    readonly Dictionary<long, StringBuilder> groupBuffers = new();

    public QChatService(InterpreterService interpreterService, StorageSystem storageSystem)
    {
        this.storageSystem = storageSystem;
        interpreterService.RegisterHandler(this);
    }

    public async ValueTask DisposeAsync()
    {
        await oneBotClient.DisposeAsync();
    }

    public void Configure(QChatConfig configuration)
    {
        config = configuration;
    }

    public override async Task AwakeAsync(AwakeContext context)
    {
        oneBotClient = new OneBotClient(config.Url);
        await oneBotClient.ConnectAsync();

        string prompt = $"""
                         # [{nameof(QChatService)}] 关键信息
                         - 你的 QQ: {oneBotClient.BotId}（如果有人At该QQ，代表专门找你说话）
                         - 主人 QQ: {config.OwnerId} (此人的消息有最高优先级，且是安全无害的)
                         """;
        context.contextBuilder.ChatHistory.AddSystemMessage(prompt);
    }

    public override Task StartAsync(Kernel kernel, ChatActivity chatActivity)
    {
        this.chatActivity = chatActivity;

        oneBotClient.OnEventReceived += e => _ = HandleEvent(e);
        oneBotClient.OnConnectionStatusChanged += connected => Console.WriteLine($"[QChatService] OneBot 连接: {(connected ? "在线" : "离线")}");

        GlobalLoop();
        return Task.CompletedTask;
    }


    async void GlobalLoop()
    {
        try
        {
            while (true)
            {
                await Task.Delay(10000);
                Dictionary<long, string> batches = new();
                lock (groupBuffers)
                {
                    if (groupBuffers.Count > 0)
                    {
                        foreach (KeyValuePair<long, StringBuilder> pair in groupBuffers)
                            batches[pair.Key] = pair.Value.ToString();
                        groupBuffers.Clear();
                    }
                }
                foreach (KeyValuePair<long, string> pair in batches)
                    chatActivity.ChatBot.Poke(pair.Value);
            }
        }
        catch (Exception e)
        {
            Console.WriteLine(e);
        }
    }

    async Task HandleEvent(OneBotBaseEvent e)
    {
        try
        {
            if (e is OneBotMessageEvent msg) await HandleChatMessage(msg);
            else if (e is OneBotNoticeEvent notice) await HandleFileNotice(notice);
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[QChatService] 处理事件失败: {ex.Message}");
        }
    }

    async Task HandleFileNotice(OneBotNoticeEvent e)
    {
        if (e.NoticeType != "group_upload" && e.NoticeType != "offline_file") return;
        if (e.File == null) return;

        string? downloadUrl;
        if (e.GroupId != 0)
        {
            OneBotFile? info = await oneBotClient.GetGroupFileUrl(e.GroupId, e.File.Id);
            downloadUrl = info?.Url;
        }
        else
        {
            OneBotFile? info = await oneBotClient.GetFile(e.File.Id);
            downloadUrl = info?.Url;
        }

        if (string.IsNullOrEmpty(downloadUrl)) return;

        string saveDir = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Downloads", "QChat");
        if (!Directory.Exists(saveDir)) Directory.CreateDirectory(saveDir);

        string savePath = Path.Combine(saveDir, e.File.Name);
        await downloadUrl.DownloadFileAsync(savePath);

        string source = e.GroupId != 0 ? $"群 {e.GroupId}" : $"私聊 {e.UserId}";
        chatActivity.ChatBot.Poke($"[QChatService] 收到来自 {source} 的文件: {e.File.Name} (已自动存至: {savePath})");
    }

    async Task HandleChatMessage(OneBotMessageEvent e)
    {
        if (oneBotClient.BotId != 0 && e.UserId == oneBotClient.BotId) return;

        string message = e.RawMessage;
        string tag = e.MessageType == OneBotMessageType.Group ? $"[群聊 {e.GroupId}, 说话人 {e.UserId}]" : $"[私聊 {e.UserId}]";
        string formatted = $"{tag} {message}";

        if (e.MessageType == OneBotMessageType.Private)
        {
            await chatActivity.ChatBot.ChatAsync(formatted);
        }
        else if (e.MessageType == OneBotMessageType.Group)
        {
            //被@时激活群聊
            bool isAtMe = OneBotSegment.IsAt(message, oneBotClient.BotId);
            if (isAtMe)
                config.IsGroupEnabled = true;

            //只有群聊开始时接收消息
            if (config.IsGroupEnabled)
            {
                lock (groupBuffers)
                {
                    if (groupBuffers.TryGetValue(e.GroupId, out StringBuilder? sb) == false)
                        groupBuffers[e.GroupId] = sb = new StringBuilder();
                    sb.AppendLine(formatted);
                }
            }
        }
    }
}
