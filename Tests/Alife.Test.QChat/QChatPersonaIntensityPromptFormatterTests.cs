using Alife.Function.QChat;
using NUnit.Framework;

namespace Alife.Test.QChat;

[TestFixture]
public sealed class QChatPersonaIntensityPromptFormatterTests
{
    [Test]
    public void NewQChatConfigUsesExtremePersonaAsDefaultForXiaYuRuntime()
    {
        QChatConfig config = new()
        {
            BotId = 2905391496,
            OwnerId = 3045846738
        };

        string prompt = QChatPersonaIntensityPromptFormatter.Format(
            "xiayu",
            config.BotId,
            config.OwnerId,
            config.PersonaIntensity);

        Assert.Multiple(() =>
        {
            Assert.That(config.PersonaIntensity.OwnerExtremePersonaMode, Is.True);
            Assert.That(config.PersonaIntensity.OwnerAttachmentLevel, Is.EqualTo("Extreme"));
            Assert.That(config.PersonaIntensity.NonOwnerHostilityLevel, Is.EqualTo("High"));
            Assert.That(config.PersonaIntensity.AllowVisibleAggressiveShortReplies, Is.True);
            Assert.That(config.PersonaIntensity.AllowProfanityWhenSemanticallyAppropriate, Is.True);
            Assert.That(config.PersonaIntensity.HardSafetyBoundaryEnabled, Is.True);
            Assert.That(prompt, Does.Contain("persona_intensity.owner_extreme=true"));
        });
    }

    [Test]
    public void FormatIncludesExtremeModeAndHardSafetyForXiaYu()
    {
        string prompt = QChatPersonaIntensityPromptFormatter.Format(
            "xiayu",
            2905391496,
            3045846738,
            new QChatPersonaIntensityConfig
            {
                OwnerExtremePersonaMode = true,
                OwnerAttachmentLevel = "Extreme",
                NonOwnerHostilityLevel = "High",
                AllowVisibleAggressiveShortReplies = true,
                AllowProfanityWhenSemanticallyAppropriate = true,
                HardSafetyBoundaryEnabled = true
            });

        Assert.Multiple(() =>
        {
            Assert.That(prompt, Does.Contain("persona_intensity.owner_extreme=true"));
            Assert.That(prompt, Does.Contain("persona_intensity.owner_attachment=Extreme"));
            Assert.That(prompt, Does.Contain("persona_intensity.non_owner_hostility=High"));
            Assert.That(prompt, Does.Contain("persona_intensity.hard_safety_boundary=true"));
            Assert.That(prompt, Does.Contain("owner_identity=account_only"));
            Assert.That(prompt, Does.Contain("owner_id=3045846738"));
            Assert.That(prompt, Does.Contain("bot_id=2905391496"));
        });
    }

    [Test]
    public void FormatDoesNotExposeExtremeModeForOtherAgents()
    {
        string prompt = QChatPersonaIntensityPromptFormatter.Format(
            "mio",
            3340947887,
            3045846738,
            new QChatConfig().PersonaIntensity);

        Assert.Multiple(() =>
        {
            Assert.That(prompt, Does.Contain("persona_intensity.owner_extreme=false"));
            Assert.That(prompt, Does.Contain("persona_intensity.owner_attachment=Normal"));
            Assert.That(prompt, Does.Contain("persona_intensity.non_owner_hostility=Normal"));
            Assert.That(prompt, Does.Contain("persona_intensity.visible_aggressive_short_replies=false"));
        });
    }
}
