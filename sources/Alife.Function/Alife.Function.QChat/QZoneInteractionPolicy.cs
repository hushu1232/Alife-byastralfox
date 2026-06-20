using System;
using System.Linq;

namespace Alife.Function.QChat;

public record QZoneInteractionConfig
{
    public bool EnableQZone { get; set; } = true;
    public string AllowedQZoneTargetIds { get; set; } = "";
    public string PrivateChatContactIds { get; set; } = "";
    public double PrivateContactLikeProbability { get; set; } = 0.15;
    public double CommentReplyProbability { get; set; } = 0.8;
    public double QZoneTargetCooldownMinutes { get; set; }
    public int MaxQZoneInteractionsPerTargetPerDay { get; set; }
}

public static class QZoneInteractionPolicy
{
    public static bool ShouldLikeTarget(QZoneInteractionConfig config, long targetId, Func<double>? random = null)
    {
        if (config.EnableQZone == false)
            return false;
        if (IsAllowedTarget(config.AllowedQZoneTargetIds, targetId) == false)
            return false;
        if (IsListedTarget(config.PrivateChatContactIds, targetId) == false)
            return false;

        return Next(random) < ClampProbability(config.PrivateContactLikeProbability);
    }

    public static bool ShouldReplyComment(QZoneInteractionConfig config, long targetId, Func<double>? random = null)
    {
        if (config.EnableQZone == false)
            return false;
        if (IsAllowedTarget(config.AllowedQZoneTargetIds, targetId) == false)
            return false;

        return Next(random) < ClampProbability(config.CommentReplyProbability);
    }

    static bool IsAllowedTarget(string allowedIds, long targetId)
    {
        string[] ids = SplitIds(allowedIds);
        return ids.Length == 0 || ids.Contains(targetId.ToString());
    }

    static bool IsListedTarget(string ids, long targetId)
    {
        return SplitIds(ids).Contains(targetId.ToString());
    }

    static string[] SplitIds(string ids)
    {
        return ids.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
    }

    static double Next(Func<double>? random)
    {
        return random?.Invoke() ?? Random.Shared.NextDouble();
    }

    static double ClampProbability(double value)
    {
        return Math.Clamp(value, 0.0, 1.0);
    }
}
