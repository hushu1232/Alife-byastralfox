using System;
using System.IO;
using Alife.Function.QChat;
using NUnit.Framework;

namespace Alife.Test.QChat;

public sealed class QChatPersonaFactProviderTests
{
    string storageRoot = null!;

    [SetUp]
    public void SetUp()
    {
        storageRoot = Path.Combine(Path.GetTempPath(), "alife-qchat-facts-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(storageRoot);
    }

    [TearDown]
    public void TearDown()
    {
        if (Directory.Exists(storageRoot))
            Directory.Delete(storageRoot, recursive: true);
    }

    [Test]
    public void ReadReturnsOnlyRequestedBoundedPersonaSection()
    {
        WriteXiayuProfile("""
            # 关系
            主人是她最信任的亲人
            # 说话风格
            对主人温柔亲密，对他人礼貌克制
            # 行为边界
            不泄露隐私，不越权执行
            """);
        QChatPersonaFactProvider provider = new(new QChatPersonaMemoryContextProvider(storageRoot));
        QChatAgentIdentity xiayu = QChatAgentIdentityRegistry.CreateDefault().ResolveByAgentId("xiayu")!;

        QChatCapabilityFeedback feedback = provider.Read(
            xiayu,
            QChatPersonaFactCategory.SpeechStyle,
            DateTimeOffset.UtcNow);

        Assert.Multiple(() =>
        {
            Assert.That(feedback.Status, Is.EqualTo(QChatCapabilityFeedbackStatus.Succeeded));
            Assert.That(feedback.Data, Does.Contain("说话风格"));
            Assert.That(feedback.Data, Does.Contain("礼貌克制"));
            Assert.That(feedback.Data, Does.Not.Contain("主人是她最信任"));
            Assert.That(feedback.Data, Does.Not.Contain(storageRoot));
        });
    }

    [Test]
    public void ReadFailsClosedForUnknownIdentityOrMissingRequestedSection()
    {
        QChatPersonaFactProvider provider = new(new QChatPersonaMemoryContextProvider(storageRoot));

        QChatCapabilityFeedback unknown = provider.Read(null, QChatPersonaFactCategory.Relationship, DateTimeOffset.UtcNow);
        QChatAgentIdentity xiayu = QChatAgentIdentityRegistry.CreateDefault().ResolveByAgentId("xiayu")!;
        WriteXiayuProfile("# 关系\n主人是她最信任的亲人");
        QChatCapabilityFeedback absent = provider.Read(xiayu, QChatPersonaFactCategory.SpeechStyle, DateTimeOffset.UtcNow);

        Assert.Multiple(() =>
        {
            Assert.That(unknown.Status, Is.EqualTo(QChatCapabilityFeedbackStatus.Denied));
            Assert.That(unknown.Data, Is.Empty);
            Assert.That(absent.Status, Is.EqualTo(QChatCapabilityFeedbackStatus.NoRelevantData));
            Assert.That(absent.Data, Is.Empty);
        });
    }

    void WriteXiayuProfile(string content)
    {
        string path = Path.Combine(storageRoot, "Character", "夏羽", "Memory", "Persona", "夏羽-角色背景.md");
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, content);
    }
}
