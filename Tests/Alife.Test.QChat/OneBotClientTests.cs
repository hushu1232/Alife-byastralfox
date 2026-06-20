using System.Collections;
using System.IO;
using System.Reflection;
using Alife.Function.QChat;
using NUnit.Framework;

namespace Alife.Test.QChat;

[TestFixture]
public class OneBotClientTests
{
    [Test]
    public void DefaultActionTimeoutAllowsSlowNapCatFileActions()
    {
        OneBotClient client = new("ws://unused");

        Assert.That(client.ActionTimeout, Is.GreaterThanOrEqualTo(TimeSpan.FromSeconds(30)));
    }

    [Test]
    public void CallActionAsync_RemovesPendingActionWhenResponseTimesOut()
    {
        TimeoutOnlyOneBotClient client = new()
        {
            ActionTimeout = TimeSpan.FromMilliseconds(40)
        };

        TimeoutException? exception = Assert.ThrowsAsync<TimeoutException>(async () =>
            await client.CallActionAsync<object>("never_responds"));

        Assert.That(exception!.Message, Does.Contain("never_responds"));
        Assert.That(exception.Message, Does.Contain("40"));
        Assert.That(GetPendingActionCount(client), Is.Zero);
    }

    [Test]
    public void CallActionAsync_WrapsSendFailuresWithActionNameAndCleansPendingAction()
    {
        SendFailingOneBotClient client = new();

        InvalidOperationException? exception = Assert.ThrowsAsync<InvalidOperationException>(async () =>
            await client.CallActionAsync<object>("send_group_msg"));

        Assert.That(exception!.Message, Does.Contain("send_group_msg"));
        Assert.That(exception.Message, Does.Contain("socket closed"));
        Assert.That(exception.InnerException, Is.TypeOf<IOException>());
        Assert.That(GetPendingActionCount(client), Is.Zero);
    }

    static int GetPendingActionCount(OneBotClient client)
    {
        FieldInfo field = typeof(OneBotClient).GetField(
            "pendingActions",
            BindingFlags.Instance | BindingFlags.NonPublic)!;
        ICollection pendingActions = (ICollection)field.GetValue(client)!;
        return pendingActions.Count;
    }

    sealed class TimeoutOnlyOneBotClient : OneBotClient
    {
        public TimeoutOnlyOneBotClient() : base("ws://unused")
        {
        }

        public override Task SendActionAsync(string action, object? @params = null, string? echo = null)
        {
            return Task.CompletedTask;
        }
    }

    sealed class SendFailingOneBotClient : OneBotClient
    {
        public SendFailingOneBotClient() : base("ws://unused")
        {
        }

        public override Task SendActionAsync(string action, object? @params = null, string? echo = null)
        {
            throw new IOException("socket closed");
        }
    }
}
