using System;

namespace Alife.Function.QChat;

public sealed class OneBotReconnectPolicy(TimeSpan initial, TimeSpan maximum, int restartThreshold)
{
    public int RestartThreshold { get; } = restartThreshold > 0 ? restartThreshold : throw new ArgumentOutOfRangeException(nameof(restartThreshold));
    public TimeSpan NextDelay(int failures) => failures <= 0 ? TimeSpan.Zero : TimeSpan.FromTicks(Math.Min(initial.Ticks * (1L << Math.Min(failures - 1, 30)), maximum.Ticks));
}
