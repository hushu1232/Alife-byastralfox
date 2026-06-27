using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Alife.Platform;

namespace Alife.Function.QChat;

public sealed class QChatVoiceProfileConfig
{
    public bool EnablePerAgentVoiceProfiles { get; set; } = true;
    public List<QChatVoiceProfile> Profiles { get; set; } = [];

    public static QChatVoiceProfileConfig CreateDefault()
    {
        return new QChatVoiceProfileConfig
        {
            Profiles =
            [
                new QChatVoiceProfile
                {
                    AgentId = "xiayu",
                    BotId = 2905391496,
                    VoiceId = "xiayu-zh",
                    ApiBaseUrl = "http://127.0.0.1:9880",
                    ReferenceAudioPath = Path.Combine(AlifePath.RuntimeFolderPath, "TTS", "voices", "xiayu", "zh", "ref.wav"),
                    GptWeightsPath = Path.Combine("D:\\lobotomy", "GPT模型", "ブルーアーカイブ", "佳代子", "中", "GPT_weights_v2", "Kayoko-Zh-e50.ckpt"),
                    SovitsWeightsPath = Path.Combine("D:\\lobotomy", "GPT模型", "ブルーアーカイブ", "佳代子", "中", "SoVITS_weights_v2", "Kayoko-Zh_e8_s664.pth"),
                    PromptText = "圣诞快乐，这是情侣们的节日啊。",
                    TextLanguage = "zh",
                    PromptLanguage = "zh"
                },
                new QChatVoiceProfile
                {
                    AgentId = "xiayu",
                    BotId = 2905391496,
                    VoiceId = "xiayu-ja",
                    ApiBaseUrl = "http://127.0.0.1:9880",
                    ReferenceAudioPath = Path.Combine(AlifePath.RuntimeFolderPath, "TTS", "voices", "xiayu", "ja", "ref.wav"),
                    GptWeightsPath = Path.Combine("D:\\lobotomy", "GPT模型", "ブルーアーカイブ", "佳代子", "日", "GPT_weights_v2", "Kayoko-ja-e50.ckpt"),
                    SovitsWeightsPath = Path.Combine("D:\\lobotomy", "GPT模型", "ブルーアーカイブ", "佳代子", "日", "SoVITS_weights_v2", "Kayoko-Ja_e8_s400.pth"),
                    PromptText = "ありがとう、先生。これからも",
                    TextLanguage = "ja",
                    PromptLanguage = "ja"
                },
                new QChatVoiceProfile
                {
                    AgentId = "mixu",
                    BotId = 3340947887,
                    VoiceId = "mixu-zh",
                    ApiBaseUrl = "http://127.0.0.1:9881",
                    ReferenceAudioPath = Path.Combine(AlifePath.RuntimeFolderPath, "TTS", "voices", "mixu", "zh", "ref.wav"),
                    GptWeightsPath = Path.Combine("D:\\lobotomy", "GPT模型", "ブルーアーカイブ", "小桃&小绿-中", "小桃", "GPT_weights_v2", "Momoi-Zh-e50.ckpt"),
                    SovitsWeightsPath = Path.Combine("D:\\lobotomy", "GPT模型", "ブルーアーカイブ", "小桃&小绿-中", "小桃", "SoVITS_weights_v2", "Momoi-Zh_e8_s552.pth"),
                    PromptText = "想提高我的好感度，那就多买点新游戏吧。",
                    TextLanguage = "zh",
                    PromptLanguage = "zh"
                },
                new QChatVoiceProfile
                {
                    AgentId = "mixu",
                    BotId = 3340947887,
                    VoiceId = "mixu-ja",
                    ApiBaseUrl = "http://127.0.0.1:9881",
                    ReferenceAudioPath = Path.Combine(AlifePath.RuntimeFolderPath, "TTS", "voices", "mixu", "ja", "ref.wav"),
                    GptWeightsPath = Path.Combine("D:\\lobotomy", "GPT模型", "ブルーアーカイブ", "小桃&小绿", "小桃", "GPT_weights_v2", "Momoi-3-e20.ckpt"),
                    SovitsWeightsPath = Path.Combine("D:\\lobotomy", "GPT模型", "ブルーアーカイブ", "小桃&小绿", "小桃", "SoVITS_weights_v2", "Momoi-3_e8_s344.pth"),
                    PromptText = "先生！今日は私と緑の誕生日なの。",
                    TextLanguage = "ja",
                    PromptLanguage = "ja"
                }
            ]
        };
    }
}

public sealed class QChatVoiceProfile
{
    public string AgentId { get; set; } = "";
    public long BotId { get; set; }
    public string VoiceId { get; set; } = "";
    public string ApiBaseUrl { get; set; } = "http://127.0.0.1:9880";
    public string ReferenceAudioPath { get; set; } = "";
    public string GptWeightsPath { get; set; } = "";
    public string SovitsWeightsPath { get; set; } = "";
    public string PromptText { get; set; } = "";
    public string TextLanguage { get; set; } = "zh";
    public string PromptLanguage { get; set; } = "zh";
    public int MaxTextChars { get; set; } = 120;
    public bool Enabled { get; set; } = true;
}

public enum QChatVoiceProfileDecisionKind
{
    Deny,
    Allow
}

public sealed record QChatVoiceProfileDecision(
    QChatVoiceProfileDecisionKind Kind,
    QChatVoiceProfile? Profile,
    string Reason);

public static class QChatVoiceProfileRouter
{
    public static QChatVoiceProfileDecision Resolve(
        QChatVoiceProfileConfig? config,
        string? agentId,
        long botId,
        string? preferredTextLanguage = null)
    {
        config ??= QChatVoiceProfileConfig.CreateDefault();
        if (config.EnablePerAgentVoiceProfiles == false)
            return Deny("per_agent_voice_profiles_disabled");

        string normalizedAgentId = (agentId ?? string.Empty).Trim();
        string normalizedLanguage = NormalizeLanguage(preferredTextLanguage);
        IEnumerable<QChatVoiceProfile?> profiles = config.Profiles?.Cast<QChatVoiceProfile?>() ?? [];
        QChatVoiceProfile? profile = null;
        if (normalizedLanguage.Length > 0)
        {
            profile = profiles.FirstOrDefault(candidate =>
                candidate != null &&
                candidate.BotId > 0 &&
                candidate.BotId == botId &&
                IsLanguageMatch(candidate, normalizedLanguage));
            if (profile != null)
                return profile.Enabled ? Allow(profile, "bot_id_language_profile_matched") : Deny("voice_profile_disabled");
        }

        profile = profiles.FirstOrDefault(candidate =>
            candidate != null && candidate.BotId > 0 && candidate.BotId == botId);
        if (profile != null)
            return profile.Enabled ? Allow(profile, "bot_id_profile_matched") : Deny("voice_profile_disabled");

        if (normalizedLanguage.Length > 0)
        {
            profile = profiles.FirstOrDefault(candidate =>
                candidate != null &&
                string.Equals(candidate.AgentId?.Trim(), normalizedAgentId, StringComparison.OrdinalIgnoreCase) &&
                IsLanguageMatch(candidate, normalizedLanguage));
            if (profile != null)
                return profile.Enabled ? Allow(profile, "agent_id_language_profile_matched") : Deny("voice_profile_disabled");
        }

        profile = profiles.FirstOrDefault(candidate =>
            candidate != null &&
            string.Equals(candidate.AgentId?.Trim(), normalizedAgentId, StringComparison.OrdinalIgnoreCase));
        if (profile != null)
            return profile.Enabled ? Allow(profile, "agent_id_profile_matched") : Deny("voice_profile_disabled");

        return Deny("voice_profile_not_found");
    }

    static QChatVoiceProfileDecision Allow(QChatVoiceProfile profile, string reason) =>
        new(QChatVoiceProfileDecisionKind.Allow, profile, reason);

    static QChatVoiceProfileDecision Deny(string reason) =>
        new(QChatVoiceProfileDecisionKind.Deny, null, reason);

    static bool IsLanguageMatch(QChatVoiceProfile profile, string normalizedLanguage)
    {
        return string.Equals(NormalizeLanguage(profile.TextLanguage), normalizedLanguage, StringComparison.OrdinalIgnoreCase);
    }

    static string NormalizeLanguage(string? language)
    {
        if (string.IsNullOrWhiteSpace(language))
            return "";

        string normalized = language.Trim().ToLowerInvariant();
        return normalized switch
        {
            "jp" => "ja",
            "jpn" => "ja",
            "japanese" => "ja",
            "cn" => "zh",
            "zho" => "zh",
            "chi" => "zh",
            "chinese" => "zh",
            _ => normalized
        };
    }
}
