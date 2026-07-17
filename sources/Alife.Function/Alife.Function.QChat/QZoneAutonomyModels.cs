using System;

namespace Alife.Function.QChat;

public enum QZoneAutonomyAction
{
    Skip,
    Post,
    Comment,
    ReplyOwnComment
}

public readonly record struct QZoneAutonomyAgentKey(string Value)
{
    public static QZoneAutonomyAgentKey Create(string agentId, long botId) =>
        new($"qzone:{agentId.Trim().ToLowerInvariant()}:{botId}");
}

public sealed record QZoneAutonomySettings(
    bool Enabled,
    bool DryRunOnly,
    TimeOnly PostWindowStart,
    TimeOnly PostWindowEnd,
    TimeSpan PostHardMinimumInterval,
    int MaxPostsPerDay,
    int XiayuMaxCommentsPerDay,
    int MixuMaxCommentsPerDay)
{
    static readonly TimeOnly DefaultPostWindowStart = new(9, 30);
    static readonly TimeOnly DefaultPostWindowEnd = new(22, 30);

    public static QZoneAutonomySettings From(QZoneServiceConfig config)
    {
        TimeOnly postWindowStart = ParseWindow(config.AutonomyPostWindowStart, DefaultPostWindowStart);
        TimeOnly postWindowEnd = ParseWindow(config.AutonomyPostWindowEnd, DefaultPostWindowEnd);
        if (postWindowStart >= postWindowEnd)
        {
            postWindowStart = DefaultPostWindowStart;
            postWindowEnd = DefaultPostWindowEnd;
        }

        return new QZoneAutonomySettings(
            config.EnableQZoneAutonomy && config.QZoneAutonomyPaused == false,
            config.QZoneAutonomyDryRunOnly,
            postWindowStart,
            postWindowEnd,
            TimeSpan.FromHours(PositiveOrDefault(config.AutonomyPostMinimumIntervalHours, 12)),
            PositiveOrDefault(config.AutonomyMaxPostsPerDay, 2),
            PositiveOrDefault(config.XiayuAutonomyMaxCommentsPerDay, 2),
            PositiveOrDefault(config.MixuAutonomyMaxCommentsPerDay, 3));
    }

    static TimeOnly ParseWindow(string value, TimeOnly fallback) =>
        TimeOnly.TryParse(value, out TimeOnly parsed) ? parsed : fallback;

    static int PositiveOrDefault(int value, int fallback) => value > 0 ? value : fallback;
}
