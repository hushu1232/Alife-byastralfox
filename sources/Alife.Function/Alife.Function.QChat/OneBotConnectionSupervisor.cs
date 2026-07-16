using System;
using System.Threading;
using System.Threading.Tasks;

namespace Alife.Function.QChat;

public interface IOneBotConnectionRuntime { Task<bool> ConnectAsync(CancellationToken cancellationToken); }
public enum OneBotConnectionOutcome { Connected, RetryScheduled, RestartThresholdReached }
public sealed class OneBotConnectionSupervisor(IOneBotConnectionRuntime runtime, OneBotReconnectPolicy policy)
{
    private readonly SemaphoreSlim gate = new(1, 1);
    public async Task<OneBotConnectionOutcome> EnsureConnectedAsync(CancellationToken cancellationToken)
    {
        await gate.WaitAsync(cancellationToken); try
        {
            for (int failures = 1; failures <= policy.RestartThreshold; failures++)
            {
                if (await runtime.ConnectAsync(cancellationToken)) return OneBotConnectionOutcome.Connected;
                if (failures == policy.RestartThreshold) return OneBotConnectionOutcome.RestartThresholdReached;
                TimeSpan delay = policy.NextDelay(failures); if (delay > TimeSpan.Zero) await Task.Delay(delay, cancellationToken);
            }
            return OneBotConnectionOutcome.RestartThresholdReached;
        }
        finally { gate.Release(); }
    }
}
