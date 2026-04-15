using Alife.Function.QChat;
using NUnit.Framework;
using System.IO;
using System.Text.Json;
using System.Windows;

namespace Alife.Test.QChat;

[TestFixture]
public class QChatFunctionTests
{
    [OneTimeSetUp]
    public async Task Setup()
    {
        client = new OneBotClient(TestUrl);
        await client.ConnectAsync();
    }

    #region 私聊测试 (Private Chat)

    [Test, Order(1)]
    public async Task TestPrivate_TextSendRecv()
    {
        var tcs = new TaskCompletionSource<OneBotMessageEvent>();
        Action<OneBotBaseEvent> handler = e => { if (e is OneBotMessageEvent m && m.MessageType == OneBotMessageType.Private) tcs.TrySetResult(m); };
        client.OnEventReceived += handler;

        MessageBox.Show("请给 Bot 发送一条【私聊】消息以锚定身份...", "私聊测试 - 1/3");
        var received = await tcs.Task.WaitAsync(TimeSpan.FromSeconds(30));
        client.OnEventReceived -= handler;

        lastPrivateUserId = received.UserId;
        await client.SendPrivateMessage(lastPrivateUserId, $"[Echo] 你好，收到你的私聊: {received.RawMessage}");
        
        var result = MessageBox.Show($"已原路回复。收到你的消息了吗？\n内容: {received.RawMessage}", "人工验证", MessageBoxButton.YesNo);
        Assert.That(result, Is.EqualTo(MessageBoxResult.Yes));
    }

    [Test, Order(2)]
    public async Task TestPrivate_ImageSend()
    {
        if (lastPrivateUserId == 0) Assert.Ignore("需完成私聊锚定。");
        
        // 使用一个网图进行测试
        const string testImageUrl = "https://www.google.com/images/branding/googlelogo/2x/googlelogo_color_272x92dp.png";
        await client.SendPrivateImage(lastPrivateUserId, testImageUrl);
        
        var result = MessageBox.Show("Bot 是否发送了一张图片（Google Logo）给你？", "图片发送验证", MessageBoxButton.YesNo);
        Assert.That(result, Is.EqualTo(MessageBoxResult.Yes));
    }

    [Test, Order(3)]
    public async Task TestPrivate_FileUpload()
    {
        if (lastPrivateUserId == 0) Assert.Ignore("需完成私聊锚定。");

        string tempFile = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "private_test.txt");
        await File.WriteAllTextAsync(tempFile, $"Private File Test - {DateTime.Now}");

        await client.UploadPrivateFile(lastPrivateUserId, tempFile, "私聊测试文件.txt");

        var result = MessageBox.Show("QQ 是否收到文件 '私聊测试文件.txt'？", "文件发送验证", MessageBoxButton.YesNo);
        Assert.That(result, Is.EqualTo(MessageBoxResult.Yes));
    }

    #endregion

    #region 群聊测试 (Group Chat)

    [Test, Order(4)]
    public async Task TestGroup_TextSendRecv()
    {
        var tcs = new TaskCompletionSource<OneBotMessageEvent>();
        Action<OneBotBaseEvent> handler = e => { if (e is OneBotMessageEvent m && m.MessageType == OneBotMessageType.Group) tcs.TrySetResult(m); };
        client.OnEventReceived += handler;

        MessageBox.Show("请在群里发送一条【普通群消息】以锚定群聊...", "群聊测试 - 1/4");
        var received = await tcs.Task.WaitAsync(TimeSpan.FromSeconds(30));
        client.OnEventReceived -= handler;

        lastGroupId = received.GroupId;
        lastGroupUserId = received.UserId;
        
        await client.SendGroupMessage(lastGroupId, $"[GroupEcho] 收到来自 {received.UserId} 的群消息: {received.RawMessage}");
        
        var result = MessageBox.Show("Bot 是否在群里原路回复了消息？", "群聊验证", MessageBoxButton.YesNo);
        Assert.That(result, Is.EqualTo(MessageBoxResult.Yes));
    }

    [Test, Order(5)]
    public async Task TestGroup_AtCheck()
    {
        if (lastGroupId == 0) Assert.Ignore("需完成群聊锚定。");

        var tcs = new TaskCompletionSource<OneBotMessageEvent>();
        Action<OneBotBaseEvent> handler = e => {
            if (e is OneBotMessageEvent m && m.GroupId == lastGroupId && m.RawMessage.Contains($"[CQ:at,qq={client.BotId}"))
                tcs.TrySetResult(m);
        };
        client.OnEventReceived += handler;

        MessageBox.Show($"请在群里【@机器人】一下 ({client.BotId})", "群聊 At 测试");
        var received = await tcs.Task.WaitAsync(TimeSpan.FromSeconds(30));
        client.OnEventReceived -= handler;

        // 测试机器人主动 @ 别人 (刚才说话的那个人)
        await client.SendGroupAt(lastGroupId, received.UserId, "收到你的召唤！这是机器人主动 @ 你的测试。");

        var result = MessageBox.Show("Bot 是否成功检测到 @ 并主动回复且 @ 了你？", "At 验证", MessageBoxButton.YesNo);
        Assert.That(result, Is.EqualTo(MessageBoxResult.Yes));
    }

    [Test, Order(6)]
    public async Task TestGroup_ImageRecv()
    {
        if (lastGroupId == 0) Assert.Ignore("需完成群聊锚定。");

        var tcs = new TaskCompletionSource<OneBotMessageEvent>();
        Action<OneBotBaseEvent> handler = e => {
            if (e is OneBotMessageEvent m && m.GroupId == lastGroupId && m.RawMessage.Contains("[CQ:image"))
                tcs.TrySetResult(m);
        };
        client.OnEventReceived += handler;

        MessageBox.Show("请在群里发送【一张图片】...", "群聊接收图片测试");
        var received = await tcs.Task.WaitAsync(TimeSpan.FromSeconds(30));
        client.OnEventReceived -= handler;

        // 简易解析 CQ 码里的 file 参数 (实际建议用正则)
        string raw = received.RawMessage;
        int fileStart = raw.IndexOf("file=") + 5;
        int fileEnd = raw.IndexOfAny(new[] { ',', ']' }, fileStart);
        string fileId = raw.Substring(fileStart, fileEnd - fileStart);

        Console.WriteLine($"成功识别图片 ID: {fileId}");
        
        // 关键：换取 URL
        var info = await client.GetImage(fileId);
        if (info != null && !string.IsNullOrEmpty(info.Url))
        {
            Console.WriteLine($"[验证成功] 拿到图片 URL: {info.Url}");
            string savePath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "recv_image.png");
            await info.Url.DownloadFileAsync(savePath);
            Console.WriteLine($"[验证成功] 已将图片下载至: {savePath}");
        }
        else
        {
            Assert.Fail("无法换取图片 URL。");
        }
    }

    [Test, Order(7)]
    public async Task TestGroup_ImageSend()
    {
        if (lastGroupId == 0) Assert.Ignore("需完成群聊锚定。");
        
        await client.SendGroupImage(lastGroupId, "https://www.baidu.com/img/flexible/logo/pc/result.png");
        
        var result = MessageBox.Show("Bot 是否在群里发送了一张图片（百度 Logo）？", "群图片验证", MessageBoxButton.YesNo);
        Assert.That(result, Is.EqualTo(MessageBoxResult.Yes));
    }

    [Test, Order(8)]
    public async Task TestGroup_FileRecv()
    {
        if (lastGroupId == 0) Assert.Ignore("需完成群聊锚定。");

        var tcs = new TaskCompletionSource<OneBotNoticeEvent>();
        Action<OneBotBaseEvent> handler = e => {
            if (e is OneBotNoticeEvent n && n.GroupId == lastGroupId && n.NoticeType == "group_upload")
                tcs.TrySetResult(n);
        };
        client.OnEventReceived += handler;

        MessageBox.Show("请在群里【上传一个文件】...", "群聊接收文件测试");
        var received = await tcs.Task.WaitAsync(TimeSpan.FromSeconds(30));
        client.OnEventReceived -= handler;

        Console.WriteLine($"收到群文件通知: {received.File?.Name}");
        
        // 关键：换取 URL (群文件需使用专门的 get_group_file_url 接口)
        if (received.File != null)
        {
            var info = await client.GetGroupFileUrl(received.GroupId, received.File.Id);
            if (info != null && !string.IsNullOrEmpty(info.Url))
            {
                Console.WriteLine($"[验证成功] 拿到文件 URL: {info.Url}");
                string savePath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, $"recv_{received.File.Name}");
                await info.Url.DownloadFileAsync(savePath);
                Console.WriteLine($"[验证成功] 已将文件下载至: {savePath}");
            }
            else
            {
                Assert.Fail("无法换取文件 URL。");
            }
        }
    }

    [Test, Order(9)]
    public async Task TestGroup_FileUpload()
    {
        if (lastGroupId == 0) Assert.Ignore("需完成群聊锚定。");

        string tempFile = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "group_test.txt");
        await File.WriteAllTextAsync(tempFile, $"Group File Test - {DateTime.Now}");

        await client.UploadGroupFile(lastGroupId, tempFile, "群测试文件.txt");

        var result = MessageBox.Show("群里是否已经收到文件 '群测试文件.txt'？", "群文件验证", MessageBoxButton.YesNo);
        Assert.That(result, Is.EqualTo(MessageBoxResult.Yes));
    }

    #endregion

    [OneTimeTearDown]
    public async Task Teardown()
    {
        await client.DisposeAsync();
    }

    OneBotClient client = null!;
    long lastPrivateUserId;
    long lastGroupId;
    long lastGroupUserId;
    const string TestUrl = "ws://127.0.0.1:3001";
}
