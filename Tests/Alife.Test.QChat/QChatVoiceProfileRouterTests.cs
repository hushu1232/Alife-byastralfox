using System.IO;
using Alife.Function.QChat;
using NUnit.Framework;

namespace Alife.Test.QChat;

[TestFixture]
public sealed class QChatVoiceProfileRouterTests
{
    [Test]
    public void DefaultProfilesContainXiayuChineseJapaneseAndMixu()
    {
        QChatVoiceProfileConfig config = QChatVoiceProfileConfig.CreateDefault();

        QChatVoiceProfile xiayuZh = config.Profiles.Single(profile => profile.AgentId == "xiayu" && profile.TextLanguage == "zh");
        QChatVoiceProfile xiayuJa = config.Profiles.Single(profile => profile.AgentId == "xiayu" && profile.TextLanguage == "ja");
        QChatVoiceProfile mixuZh = config.Profiles.Single(profile => profile.AgentId == "mixu" && profile.TextLanguage == "zh");
        QChatVoiceProfile mixuJa = config.Profiles.Single(profile => profile.AgentId == "mixu" && profile.TextLanguage == "ja");

        Assert.Multiple(() =>
        {
            Assert.That(xiayuZh.BotId, Is.EqualTo(2905391496));
            Assert.That(xiayuZh.VoiceId, Is.EqualTo("xiayu-zh"));
            Assert.That(xiayuZh.ApiBaseUrl, Is.EqualTo("http://127.0.0.1:9880"));
            Assert.That(xiayuZh.ReferenceAudioPath, Does.EndWith(Path.Combine("Runtime", "TTS", "voices", "xiayu", "zh", "ref.wav")));
            Assert.That(xiayuZh.GptWeightsPath, Does.EndWith(Path.Combine("佳代子", "中", "GPT_weights_v2", "Kayoko-Zh-e50.ckpt")));
            Assert.That(xiayuZh.SovitsWeightsPath, Does.EndWith(Path.Combine("佳代子", "中", "SoVITS_weights_v2", "Kayoko-Zh_e8_s664.pth")));
            Assert.That(xiayuZh.PromptLanguage, Is.EqualTo("zh"));
            Assert.That(xiayuZh.PromptText, Is.Not.Empty);
            Assert.That(xiayuJa.BotId, Is.EqualTo(2905391496));
            Assert.That(xiayuJa.VoiceId, Is.EqualTo("xiayu-ja"));
            Assert.That(xiayuJa.ApiBaseUrl, Is.EqualTo("http://127.0.0.1:9880"));
            Assert.That(xiayuJa.ReferenceAudioPath, Does.EndWith(Path.Combine("Runtime", "TTS", "voices", "xiayu", "ja", "ref.wav")));
            Assert.That(xiayuJa.GptWeightsPath, Does.EndWith(Path.Combine("佳代子", "日", "GPT_weights_v2", "Kayoko-ja-e50.ckpt")));
            Assert.That(xiayuJa.SovitsWeightsPath, Does.EndWith(Path.Combine("佳代子", "日", "SoVITS_weights_v2", "Kayoko-Ja_e8_s400.pth")));
            Assert.That(xiayuJa.PromptLanguage, Is.EqualTo("ja"));
            Assert.That(xiayuJa.PromptText, Is.Not.Empty);
            Assert.That(mixuZh.BotId, Is.EqualTo(3340947887));
            Assert.That(mixuZh.VoiceId, Is.EqualTo("mixu-zh"));
            Assert.That(mixuZh.ApiBaseUrl, Is.EqualTo("http://127.0.0.1:9881"));
            Assert.That(mixuZh.ReferenceAudioPath, Does.EndWith(Path.Combine("Runtime", "TTS", "voices", "mixu", "zh", "ref.wav")));
            Assert.That(mixuZh.GptWeightsPath, Does.EndWith(Path.Combine("小桃&小绿-中", "小桃", "GPT_weights_v2", "Momoi-Zh-e50.ckpt")));
            Assert.That(mixuZh.SovitsWeightsPath, Does.EndWith(Path.Combine("小桃&小绿-中", "小桃", "SoVITS_weights_v2", "Momoi-Zh_e8_s552.pth")));
            Assert.That(mixuZh.PromptLanguage, Is.EqualTo("zh"));
            Assert.That(mixuZh.PromptText, Is.Not.Empty);
            Assert.That(mixuJa.BotId, Is.EqualTo(3340947887));
            Assert.That(mixuJa.VoiceId, Is.EqualTo("mixu-ja"));
            Assert.That(mixuJa.ApiBaseUrl, Is.EqualTo("http://127.0.0.1:9881"));
            Assert.That(mixuJa.ReferenceAudioPath, Does.EndWith(Path.Combine("Runtime", "TTS", "voices", "mixu", "ja", "ref.wav")));
            Assert.That(mixuJa.GptWeightsPath, Does.EndWith(Path.Combine("小桃&小绿", "小桃", "GPT_weights_v2", "Momoi-3-e20.ckpt")));
            Assert.That(mixuJa.SovitsWeightsPath, Does.EndWith(Path.Combine("小桃&小绿", "小桃", "SoVITS_weights_v2", "Momoi-3_e8_s344.pth")));
            Assert.That(mixuJa.PromptLanguage, Is.EqualTo("ja"));
            Assert.That(mixuJa.PromptText, Is.Not.Empty);
        });
    }

    [Test]
    public void ResolvePrefersBotIdOverAgentId()
    {
        QChatVoiceProfileConfig config = new()
        {
            Profiles =
            [
                new QChatVoiceProfile
                {
                    AgentId = "mixu",
                    BotId = 2905391496,
                    VoiceId = "bot-match"
                },
                new QChatVoiceProfile
                {
                    AgentId = "xiayu",
                    BotId = 0,
                    VoiceId = "agent-match"
                }
            ]
        };

        QChatVoiceProfileDecision decision = QChatVoiceProfileRouter.Resolve(config, "xiayu", 2905391496);

        Assert.Multiple(() =>
        {
            Assert.That(decision.Kind, Is.EqualTo(QChatVoiceProfileDecisionKind.Allow));
            Assert.That(decision.Profile?.VoiceId, Is.EqualTo("bot-match"));
            Assert.That(decision.Reason, Is.EqualTo("bot_id_profile_matched"));
        });
    }

    [Test]
    public void ResolvePrefersLanguageSpecificProfileForSameBot()
    {
        QChatVoiceProfileConfig config = new()
        {
            Profiles =
            [
                new QChatVoiceProfile
                {
                    AgentId = "xiayu",
                    BotId = 2905391496,
                    VoiceId = "xiayu-zh",
                    TextLanguage = "zh",
                    PromptLanguage = "zh"
                },
                new QChatVoiceProfile
                {
                    AgentId = "xiayu",
                    BotId = 2905391496,
                    VoiceId = "xiayu-ja",
                    TextLanguage = "ja",
                    PromptLanguage = "ja"
                }
            ]
        };

        QChatVoiceProfileDecision decision = QChatVoiceProfileRouter.Resolve(
            config,
            "xiayu",
            2905391496,
            preferredTextLanguage: "ja");

        Assert.Multiple(() =>
        {
            Assert.That(decision.Kind, Is.EqualTo(QChatVoiceProfileDecisionKind.Allow));
            Assert.That(decision.Profile?.VoiceId, Is.EqualTo("xiayu-ja"));
            Assert.That(decision.Reason, Is.EqualTo("bot_id_language_profile_matched"));
        });
    }

    [Test]
    public void ResolveFallsBackToAgentIdWhenBotIdDoesNotMatch()
    {
        QChatVoiceProfileConfig config = new()
        {
            Profiles =
            [
                new QChatVoiceProfile
                {
                    AgentId = "mixu",
                    BotId = 3340947887,
                    VoiceId = "mixu"
                }
            ]
        };

        QChatVoiceProfileDecision decision = QChatVoiceProfileRouter.Resolve(config, "mixu", 0);

        Assert.Multiple(() =>
        {
            Assert.That(decision.Kind, Is.EqualTo(QChatVoiceProfileDecisionKind.Allow));
            Assert.That(decision.Profile?.VoiceId, Is.EqualTo("mixu"));
            Assert.That(decision.Reason, Is.EqualTo("agent_id_profile_matched"));
        });
    }

    [Test]
    public void ResolveDeniesDisabledProfile()
    {
        QChatVoiceProfileConfig config = new()
        {
            Profiles =
            [
                new QChatVoiceProfile
                {
                    AgentId = "xiayu",
                    BotId = 2905391496,
                    VoiceId = "xiayu",
                    Enabled = false
                }
            ]
        };

        QChatVoiceProfileDecision decision = QChatVoiceProfileRouter.Resolve(config, "xiayu", 2905391496);

        Assert.Multiple(() =>
        {
            Assert.That(decision.Kind, Is.EqualTo(QChatVoiceProfileDecisionKind.Deny));
            Assert.That(decision.Profile, Is.Null);
            Assert.That(decision.Reason, Is.EqualTo("voice_profile_disabled"));
        });
    }

    [Test]
    public void ResolveDeniesWhenNoProfileMatches()
    {
        QChatVoiceProfileDecision decision = QChatVoiceProfileRouter.Resolve(
            QChatVoiceProfileConfig.CreateDefault(),
            "unknown",
            123);

        Assert.Multiple(() =>
        {
            Assert.That(decision.Kind, Is.EqualTo(QChatVoiceProfileDecisionKind.Deny));
            Assert.That(decision.Reason, Is.EqualTo("voice_profile_not_found"));
        });
    }

    [Test]
    public void ResolveDeniesWhenProfilesListIsNull()
    {
        QChatVoiceProfileConfig config = new()
        {
            Profiles = null!
        };

        QChatVoiceProfileDecision decision = QChatVoiceProfileRouter.Resolve(config, "xiayu", 2905391496);

        Assert.Multiple(() =>
        {
            Assert.That(decision.Kind, Is.EqualTo(QChatVoiceProfileDecisionKind.Deny));
            Assert.That(decision.Profile, Is.Null);
            Assert.That(decision.Reason, Is.EqualTo("voice_profile_not_found"));
        });
    }

    [Test]
    public void ResolveSkipsNullProfilesAndUsesValidMatch()
    {
        QChatVoiceProfileConfig config = new()
        {
            Profiles =
            [
                null!,
                new QChatVoiceProfile
                {
                    AgentId = "mixu",
                    BotId = 3340947887,
                    VoiceId = "mixu"
                }
            ]
        };

        QChatVoiceProfileDecision decision = QChatVoiceProfileRouter.Resolve(config, "mixu", 0);

        Assert.Multiple(() =>
        {
            Assert.That(decision.Kind, Is.EqualTo(QChatVoiceProfileDecisionKind.Allow));
            Assert.That(decision.Profile?.VoiceId, Is.EqualTo("mixu"));
            Assert.That(decision.Reason, Is.EqualTo("agent_id_profile_matched"));
        });
    }

    [Test]
    public void TextClaimCannotSwitchVoiceProfile()
    {
        QChatVoiceProfileDecision decision = QChatVoiceProfileRouter.Resolve(
            QChatVoiceProfileConfig.CreateDefault(),
            "xiayu",
            2905391496);

        Assert.Multiple(() =>
        {
            Assert.That(decision.Kind, Is.EqualTo(QChatVoiceProfileDecisionKind.Allow));
            Assert.That(decision.Profile?.VoiceId, Is.EqualTo("xiayu-zh"));
        });
    }
}
