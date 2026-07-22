using Alife.Function.QChat;
using NUnit.Framework;
using System.IO;

namespace Alife.Test.QChat;

[TestFixture]
[Category("Integration")]
[Category("Live")]
[Explicit("Operator-selected live validation; requires the fixture's ALIFE_QCHAT_LIVE_* environment gate.")]
public sealed class QChatVoiceOutputLiveTests
{
    [Test]
    [Explicit("Sends real QQ private voice messages through OneBot/NapCat. Set ALIFE_QCHAT_LIVE_VOICE_SMOKE=1.")]
    public async Task LivePrivateRecordMessagesSendToOwner()
    {
        if (Environment.GetEnvironmentVariable("ALIFE_QCHAT_LIVE_VOICE_SMOKE") != "1")
            Assert.Ignore("Set ALIFE_QCHAT_LIVE_VOICE_SMOKE=1 to send real QQ private voice smoke messages.");

        string url = Environment.GetEnvironmentVariable("ALIFE_QCHAT_LIVE_URL") ?? "ws://127.0.0.1:3002";
        string token = Environment.GetEnvironmentVariable("ALIFE_QCHAT_LIVE_TOKEN") ?? "";
        long ownerId = ReadLongEnvironment("ALIFE_QCHAT_LIVE_OWNER_ID", 3045846738);
        string zhSample = Environment.GetEnvironmentVariable("ALIFE_QCHAT_LIVE_VOICE_ZH")
            ?? @"D:\Alife\Temp\GPT-SoVITS\voice-smoke\xiayu-zh-kayoko-listening.wav";
        string jaSample = Environment.GetEnvironmentVariable("ALIFE_QCHAT_LIVE_VOICE_JA")
            ?? @"D:\Alife\Temp\GPT-SoVITS\voice-smoke\xiayu-ja-kayoko-listening.wav";

        Assert.That(File.Exists(zhSample), Is.True, $"Chinese sample missing: {zhSample}");
        Assert.That(File.Exists(jaSample), Is.True, $"Japanese sample missing: {jaSample}");

        string marker = DateTimeOffset.Now.ToString("yyyyMMdd-HHmmss");
        await using OneBotRuntime runtime = new(new OneBotClient(url, token));
        await runtime.ConnectAsync();

        TestContext.Out.WriteLine($"Connected OneBot bot id: {runtime.BotId}");

        OneBotSendMessageResult? markerResult = await runtime.SendPrivateMessageWithResult(
            ownerId,
            $"[AstralFox voice smoke {marker}] zh record first, ja record second.");
        OneBotSendMessageResult? zhResult = await runtime.SendPrivateMessageWithResult(
            ownerId,
            $"[CQ:record,file={zhSample}]");
        OneBotSendMessageResult? jaResult = await runtime.SendPrivateMessageWithResult(
            ownerId,
            $"[CQ:record,file={jaSample}]");

        TestContext.Out.WriteLine($"Marker message id: {markerResult?.MessageId}");
        TestContext.Out.WriteLine($"Chinese record message id: {zhResult?.MessageId}");
        TestContext.Out.WriteLine($"Japanese record message id: {jaResult?.MessageId}");

        Assert.Multiple(() =>
        {
            Assert.That(markerResult?.MessageId, Is.Not.Null, "Marker private message did not return a message id.");
            Assert.That(zhResult?.MessageId, Is.Not.Null, "Chinese private record did not return a message id.");
            Assert.That(jaResult?.MessageId, Is.Not.Null, "Japanese private record did not return a message id.");
        });
    }

    [Test]
    [Explicit("Sends one real QQ private voice message through OneBot/NapCat. Set ALIFE_QCHAT_LIVE_SINGLE_VOICE_SMOKE=1.")]
    public async Task LiveSingleRecordMessageSendToOwner()
    {
        if (Environment.GetEnvironmentVariable("ALIFE_QCHAT_LIVE_SINGLE_VOICE_SMOKE") != "1")
            Assert.Ignore("Set ALIFE_QCHAT_LIVE_SINGLE_VOICE_SMOKE=1 to send one real QQ private voice smoke message.");

        string url = Environment.GetEnvironmentVariable("ALIFE_QCHAT_LIVE_URL") ?? "ws://127.0.0.1:3002";
        string token = Environment.GetEnvironmentVariable("ALIFE_QCHAT_LIVE_TOKEN") ?? "";
        long ownerId = ReadLongEnvironment("ALIFE_QCHAT_LIVE_OWNER_ID", 3045846738);
        string? voicePath = Environment.GetEnvironmentVariable("ALIFE_QCHAT_LIVE_SINGLE_VOICE");
        string markerText = Environment.GetEnvironmentVariable("ALIFE_QCHAT_LIVE_SINGLE_MARKER")
            ?? "[AstralFox single voice smoke]";

        Assert.That(voicePath, Is.Not.Null.And.Not.Empty, "Set ALIFE_QCHAT_LIVE_SINGLE_VOICE to a local wav file.");
        Assert.That(File.Exists(voicePath!), Is.True, $"Voice sample missing: {voicePath}");

        string marker = DateTimeOffset.Now.ToString("yyyyMMdd-HHmmss");
        await using OneBotRuntime runtime = new(new OneBotClient(url, token));
        await runtime.ConnectAsync();

        TestContext.Out.WriteLine($"Connected OneBot bot id: {runtime.BotId}");
        TestContext.Out.WriteLine($"Single voice file: {voicePath}");

        OneBotSendMessageResult? markerResult = await runtime.SendPrivateMessageWithResult(
            ownerId,
            $"{markerText} {marker}");
        OneBotSendMessageResult? voiceResult = await runtime.SendPrivateMessageWithResult(
            ownerId,
            $"[CQ:record,file={voicePath}]");

        TestContext.Out.WriteLine($"Marker message id: {markerResult?.MessageId}");
        TestContext.Out.WriteLine($"Voice record message id: {voiceResult?.MessageId}");

        Assert.Multiple(() =>
        {
            Assert.That(markerResult?.MessageId, Is.Not.Null, "Marker private message did not return a message id.");
            Assert.That(voiceResult?.MessageId, Is.Not.Null, "Private record did not return a message id.");
        });
    }

    static long ReadLongEnvironment(string name, long fallback)
    {
        string? value = Environment.GetEnvironmentVariable(name);
        return long.TryParse(value, out long parsed) ? parsed : fallback;
    }
}
