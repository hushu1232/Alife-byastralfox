using System;
using System.Collections.Generic;
using System.Linq;

namespace Alife.Function.QChat;

public sealed class QChatVisionProfileConfig
{
    public bool EnablePerAgentVisionProfiles { get; set; } = true;
    public List<QChatVisionProfile> Profiles { get; set; } = [];

    public static QChatVisionProfileConfig CreateDefault()
    {
        return new QChatVisionProfileConfig
        {
            Profiles =
            [
                new QChatVisionProfile
                {
                    AgentId = "xiayu",
                    BotId = 2905391496,
                    Provider = "agnes",
                    PrimaryProvider = "agnes",
                    FallbackProvider = "grok",
                    ComplexRequestProvider = "grok",
                    Model = "agnes-2.0-flash",
                    ApiEndpoint = "https://apihub.agnes-ai.com/v1/chat/completions",
                    MaxImagesPerMessage = 2,
                    RequiresPublicUrl = true
                },
                new QChatVisionProfile
                {
                    AgentId = "mixu",
                    BotId = 3340947887,
                    Provider = "agnes",
                    PrimaryProvider = "agnes",
                    FallbackProvider = "grok",
                    ComplexRequestProvider = "grok",
                    Model = "agnes-2.0-flash",
                    ApiEndpoint = "https://apihub.agnes-ai.com/v1/chat/completions",
                    MaxImagesPerMessage = 2,
                    RequiresPublicUrl = true
                }
            ]
        };
    }
}

public sealed class QChatVisionProfile
{
    public string AgentId { get; set; } = "";
    public long BotId { get; set; }
    public string Provider { get; set; } = "agnes";
    public string PrimaryProvider { get; set; } = "";
    public string FallbackProvider { get; set; } = "";
    public string ComplexRequestProvider { get; set; } = "";
    public string Model { get; set; } = "agnes-2.0-flash";
    public string ApiEndpoint { get; set; } = "https://apihub.agnes-ai.com/v1/chat/completions";
    public int MaxImagesPerMessage { get; set; } = 2;
    public bool RequiresPublicUrl { get; set; } = true;
    public bool Enabled { get; set; } = true;
}

public enum QChatVisionProfileDecisionKind
{
    Deny,
    Allow
}

public sealed record QChatVisionProfileDecision(
    QChatVisionProfileDecisionKind Kind,
    QChatVisionProfile? Profile,
    string Reason);

public static class QChatVisionProfileRouter
{
    public static QChatVisionProfileDecision Resolve(QChatVisionProfileConfig? config, string? agentId, long botId)
    {
        config ??= QChatVisionProfileConfig.CreateDefault();
        if (config.EnablePerAgentVisionProfiles == false)
            return Deny("per_agent_vision_profiles_disabled");

        IEnumerable<QChatVisionProfile?> profiles = config.Profiles?.Cast<QChatVisionProfile?>() ?? [];
        QChatVisionProfile? profile = profiles.FirstOrDefault(candidate =>
            candidate != null && candidate.BotId > 0 && candidate.BotId == botId);
        if (profile != null)
            return profile.Enabled ? Allow(profile, "bot_id_profile_matched") : Deny("vision_profile_disabled");

        string normalizedAgentId = (agentId ?? "").Trim();
        profile = profiles.FirstOrDefault(candidate =>
            candidate != null &&
            string.Equals(candidate.AgentId?.Trim(), normalizedAgentId, StringComparison.OrdinalIgnoreCase));
        if (profile != null)
            return profile.Enabled ? Allow(profile, "agent_id_profile_matched") : Deny("vision_profile_disabled");

        return Deny("vision_profile_not_found");
    }

    static QChatVisionProfileDecision Allow(QChatVisionProfile profile, string reason) =>
        new(QChatVisionProfileDecisionKind.Allow, profile, reason);

    static QChatVisionProfileDecision Deny(string reason) =>
        new(QChatVisionProfileDecisionKind.Deny, null, reason);
}
