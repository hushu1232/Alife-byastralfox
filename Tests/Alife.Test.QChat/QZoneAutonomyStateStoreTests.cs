using System.IO;
using System.Security.Cryptography;
using System.Text;
using Alife.Function.QChat;
using NUnit.Framework;

namespace Alife.Test.QChat;

public sealed class QZoneAutonomyStateStoreTests
{
    readonly List<string> directoriesToDelete = [];

    [TearDown]
    public void TearDown()
    {
        foreach (string directory in directoriesToDelete)
        {
            if (Directory.Exists(directory))
                Directory.Delete(directory, recursive: true);
        }
    }

    [Test]
    public void AtomicRoundTripPersistsOnlySafeStateAndKeepsEightNewestContentHashes()
    {
        string directory = CreateTemporaryDirectory();
        QZoneAutonomyStateStore store = new(directory);
        QZoneAutonomyAgentKey agentKey = QZoneAutonomyAgentKey.Create("xiayu", 10001);
        DateTimeOffset now = new(2026, 7, 17, 12, 0, 0, TimeSpan.Zero);
        IReadOnlyList<string> hashes = Enumerable.Range(0, 9)
            .Select(index => Hash($"content-{index}"))
            .ToArray();
        QZoneAutonomyState state = QZoneAutonomyState.Create(agentKey) with {
            LastSuccessfulPostAt = now,
            LastSuccessfulCommentAt = now.AddHours(-1),
            NextPostCandidateAt = now.AddHours(33),
            DailyCountDate = DateOnly.FromDateTime(now.DateTime),
            PostsToday = 1,
            CommentsToday = 2,
            CooldownUntil = now.AddMinutes(15),
            LastFailureKind = "transport-unavailable",
            LastAuditId = "audit-123",
            ContentHashes = hashes
        };

        store.Save(state);

        QZoneAutonomyState loaded = store.Load(agentKey);
        string[] persistedFiles = Directory.GetFiles(directory, "*.json");
        string persistedJson = File.ReadAllText(persistedFiles.Single());

        Assert.Multiple(() =>
        {
            Assert.That(loaded.AgentKey, Is.EqualTo(agentKey));
            Assert.That(loaded.LastSuccessfulPostAt, Is.EqualTo(now));
            Assert.That(loaded.LastSuccessfulCommentAt, Is.EqualTo(now.AddHours(-1)));
            Assert.That(loaded.NextPostCandidateAt, Is.EqualTo(now.AddHours(33)));
            Assert.That(loaded.PostsToday, Is.EqualTo(1));
            Assert.That(loaded.CommentsToday, Is.EqualTo(2));
            Assert.That(loaded.CooldownUntil, Is.EqualTo(now.AddMinutes(15)));
            Assert.That(loaded.LastFailureKind, Is.EqualTo("transport-unavailable"));
            Assert.That(loaded.LastAuditId, Is.EqualTo("audit-123"));
            Assert.That(loaded.ContentHashes, Is.EqualTo(hashes.Skip(1)));
            Assert.That(loaded.ContentHashes, Has.Count.EqualTo(8));
            Assert.That(persistedJson, Does.Not.Contain("cookie").IgnoreCase);
            Assert.That(persistedJson, Does.Not.Contain("prompt").IgnoreCase);
            Assert.That(persistedJson, Does.Not.Contain("draft").IgnoreCase);
            Assert.That(Directory.GetFiles(directory, "*.tmp"), Is.Empty);
        });
    }

    [Test]
    public void DefaultStorePathEndsUnderStorageQZoneAutonomy()
    {
        QZoneAutonomyStateStore store = new();

        Assert.That(store.DirectoryPath, Does.EndWith(Path.Combine("Storage", "QZoneAutonomy")));
    }

    string CreateTemporaryDirectory()
    {
        string directory = Path.Combine(Path.GetTempPath(), "Alife.QZoneAutonomy.Tests", Guid.NewGuid().ToString("N"));
        directoriesToDelete.Add(directory);
        return directory;
    }

    static string Hash(string content) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(content))).ToLowerInvariant();
}
