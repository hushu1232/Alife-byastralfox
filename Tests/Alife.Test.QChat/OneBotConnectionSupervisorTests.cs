using Alife.Function.QChat;
using NUnit.Framework;

namespace Alife.Test.QChat;

public sealed class OneBotConnectionSupervisorTests
{
    [Test]
    public async Task Supervisor_reaches_restart_threshold_after_bounded_backoff()
    {
        FakeOneBotRuntime runtime = new(connectFailuresBeforeSuccess: 3);
        OneBotReconnectPolicy policy = new(TimeSpan.Zero, TimeSpan.Zero, restartThreshold: 3);

        OneBotConnectionOutcome outcome = await new OneBotConnectionSupervisor(runtime, policy).EnsureConnectedAsync(CancellationToken.None);

        Assert.Multiple(() =>
        {
            Assert.That(outcome, Is.EqualTo(OneBotConnectionOutcome.RestartThresholdReached));
            Assert.That(runtime.ConnectCalls, Is.EqualTo(3));
        });
    }

    private sealed class FakeOneBotRuntime(int connectFailuresBeforeSuccess) : IOneBotConnectionRuntime
    {
        public int ConnectCalls { get; private set; }
        public Task<bool> ConnectAsync(CancellationToken cancellationToken) => Task.FromResult(++ConnectCalls > connectFailuresBeforeSuccess);
    }
}
