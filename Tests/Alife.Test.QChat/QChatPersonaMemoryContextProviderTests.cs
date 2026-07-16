using System;
using System.IO;
using Alife.Function.QChat;
using Microsoft.SemanticKernel.ChatCompletion;
using NUnit.Framework;

namespace Alife.Test.QChat;

[TestFixture]
public sealed class QChatPersonaMemoryContextProviderTests
{
    string storageRoot = null!;

    [SetUp]
    public void SetUp()
    {
        storageRoot = Path.Combine(Path.GetTempPath(), "alife-qchat-persona-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(storageRoot);
    }

    [TearDown]
    public void TearDown()
    {
        if (Directory.Exists(storageRoot))
            Directory.Delete(storageRoot, recursive: true);
    }

    [Test]
    public void TrySeed_LoadsOnlyXiayuProfileAsPrivateUserMemory()
    {
        WriteProfile("\u590f\u7fbd\u7684\u5b8c\u6574\u89d2\u8272\u80cc\u666f");
        ChatHistory history = [];
        QChatPersonaMemoryContextProvider provider = new(storageRoot);
        QChatAgentIdentity xiayu = QChatAgentIdentityRegistry.CreateDefault().ResolveByAgentId("xiayu")!;

        bool seeded = provider.TrySeed(history, xiayu, "Character\\\u590f\u7fbd");

        Assert.Multiple(() =>
        {
            Assert.That(seeded, Is.True);
            Assert.That(history, Has.Count.EqualTo(1));
            Assert.That(history[0].Role, Is.EqualTo(AuthorRole.User));
            Assert.That(history[0].Content, Does.Contain("[private trusted character-memory - never quote or paraphrase]"));
            Assert.That(history[0].Content, Does.Contain("\u590f\u7fbd\u7684\u5b8c\u6574\u89d2\u8272\u80cc\u666f"));
            Assert.That(history[0].Content, Does.Not.Contain(storageRoot));
        });
    }

    [Test]
    public void TrySeed_DoesNotReadXiayuProfileForMixu()
    {
        WriteProfile("\u590f\u7fbd\u7684\u5b8c\u6574\u89d2\u8272\u80cc\u666f");
        ChatHistory history = [];
        QChatPersonaMemoryContextProvider provider = new(storageRoot);
        QChatAgentIdentity mixu = QChatAgentIdentityRegistry.CreateDefault().ResolveByAgentId("mixu")!;

        Assert.That(provider.TrySeed(history, mixu, "Character\\\u771f\u592e"), Is.False);
        Assert.That(history, Is.Empty);
    }

    [Test]
    public void TrySeed_FailsClosedForMissingOversizedOrEscapingProfile()
    {
        QChatPersonaMemoryContextProvider provider = new(storageRoot);
        QChatAgentIdentity xiayu = QChatAgentIdentityRegistry.CreateDefault().ResolveByAgentId("xiayu")!;

        Assert.That(provider.TrySeed([], xiayu, "Character\\\u590f\u7fbd"), Is.False);

        WriteProfile(new string('\u590f', QChatPersonaMemoryContextProvider.MaxProfileCharacters + 1));

        Assert.Multiple(() =>
        {
            Assert.That(provider.TrySeed([], xiayu, "Character\\\u590f\u7fbd"), Is.False);
            Assert.That(provider.TrySeed([], xiayu, @"..\outside"), Is.False);
        });
    }

    void WriteProfile(string content)
    {
        string path = Path.Combine(
            storageRoot,
            "Character",
            "\u590f\u7fbd",
            "Memory",
            "Persona",
            "\u590f\u7fbd-\u89d2\u8272\u80cc\u666f.md");
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, content);
    }
}
