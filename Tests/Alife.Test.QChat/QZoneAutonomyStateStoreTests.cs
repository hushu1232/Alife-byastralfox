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
    public void ExplicitStorePathDoesNotCreateTheTemporaryDirectoryUntilStateIsSaved()
    {
        string directory = CreateTemporaryDirectory();
        QZoneAutonomyStateStore store = new(directory);

        Assert.Multiple(() =>
        {
            Assert.That(store.DirectoryPath, Is.EqualTo(directory));
            Assert.That(Directory.Exists(directory), Is.False);
        });
    }

    [Test]
    public void OverwritingAnExistingStateAtomicallyReturnsTheSecondSafeStateWithoutTemporaryFiles()
    {
        string directory = CreateTemporaryDirectory();
        QZoneAutonomyStateStore store = new(directory);
        QZoneAutonomyAgentKey agentKey = QZoneAutonomyAgentKey.Create("mixu", 10002);
        DateTimeOffset now = new(2026, 7, 17, 12, 0, 0, TimeSpan.Zero);
        QZoneAutonomyState firstState = QZoneAutonomyState.Create(agentKey) with {
            LastAuditId = "audit-first",
            NextPostCandidateAt = now.AddHours(24),
            ContentHashes = [Hash("content-first")]
        };
        QZoneAutonomyState secondState = firstState with {
            LastSuccessfulPostAt = now,
            NextPostCandidateAt = now.AddHours(42),
            PostsToday = 1,
            LastAuditId = "audit-second",
            LastFailureKind = "transport-unavailable",
            ContentHashes = [Hash("content-second")]
        };

        store.Save(firstState);
        store.Save(secondState);

        QZoneAutonomyState loaded = store.Load(agentKey);
        string[] persistedFiles = Directory.GetFiles(directory, "*.json");
        string persistedJson = File.ReadAllText(persistedFiles.Single());

        Assert.Multiple(() =>
        {
            Assert.That(loaded.LastSuccessfulPostAt, Is.EqualTo(secondState.LastSuccessfulPostAt));
            Assert.That(loaded.NextPostCandidateAt, Is.EqualTo(secondState.NextPostCandidateAt));
            Assert.That(loaded.PostsToday, Is.EqualTo(secondState.PostsToday));
            Assert.That(loaded.LastAuditId, Is.EqualTo("audit-second"));
            Assert.That(loaded.LastFailureKind, Is.EqualTo("transport-unavailable"));
            Assert.That(loaded.ContentHashes, Is.EqualTo(secondState.ContentHashes));
            Assert.That(Directory.GetFiles(directory, "*.tmp"), Is.Empty);
            Assert.That(persistedJson, Does.Not.Contain("cookie").IgnoreCase);
            Assert.That(persistedJson, Does.Not.Contain("prompt").IgnoreCase);
            Assert.That(persistedJson, Does.Not.Contain("draft").IgnoreCase);
        });
    }

    [Test]
    public void ValidNonsecretAuditMetadataRoundTripsExactly()
    {
        string directory = CreateTemporaryDirectory();
        QZoneAutonomyStateStore store = new(directory);
        QZoneAutonomyAgentKey agentKey = QZoneAutonomyAgentKey.Create("xiayu", 10001);
        QZoneAutonomyState state = QZoneAutonomyState.Create(agentKey) with {
            LastAuditId = "audit:xiayu-10001.20260717",
            LastFailureKind = "transport.timeout-v2"
        };

        store.Save(state);

        QZoneAutonomyState loaded = store.Load(agentKey);

        Assert.Multiple(() =>
        {
            Assert.That(loaded.LastAuditId, Is.EqualTo("audit:xiayu-10001.20260717"));
            Assert.That(loaded.LastFailureKind, Is.EqualTo("transport.timeout-v2"));
        });
    }

    [Test]
    public void UnsafeAuditMetadataIsRejectedAndOpaqueCookiePromptAndChatTextNeverPersist()
    {
        const string fakeCookie = "pt_key=opaque-cookie-value";
        const string fakePrompt = "SYSTEM PROMPT: do-not-persist";
        const string fakeChat = "raw chat message: hello";
        string directory = CreateTemporaryDirectory();
        QZoneAutonomyStateStore store = new(directory);
        QZoneAutonomyAgentKey agentKey = QZoneAutonomyAgentKey.Create("xiayu", 10001);
        QZoneAutonomyState state = QZoneAutonomyState.Create(agentKey) with {
            LastAuditId = $"audit:{fakeCookie}:{fakePrompt}",
            LastFailureKind = $"transport-{fakeChat}-{fakeCookie}"
        };

        store.Save(state);

        QZoneAutonomyState loaded = store.Load(agentKey);
        string persistedJson = File.ReadAllText(Directory.GetFiles(directory, "*.json").Single());

        Assert.Multiple(() =>
        {
            Assert.That(loaded.LastAuditId, Is.Null);
            Assert.That(loaded.LastFailureKind, Is.Null);
            Assert.That(persistedJson, Does.Not.Contain(fakeCookie));
            Assert.That(persistedJson, Does.Not.Contain(fakePrompt));
            Assert.That(persistedJson, Does.Not.Contain(fakeChat));
        });
    }

    [Test]
    public void OverlongOtherwiseSafeAuditMetadataIsRejectedRatherThanTruncated()
    {
        string directory = CreateTemporaryDirectory();
        QZoneAutonomyStateStore store = new(directory);
        QZoneAutonomyAgentKey agentKey = QZoneAutonomyAgentKey.Create("xiayu", 10001);
        string overlongValue = new('a', 257);
        QZoneAutonomyState state = QZoneAutonomyState.Create(agentKey) with {
            LastAuditId = overlongValue,
            LastFailureKind = overlongValue
        };

        store.Save(state);

        QZoneAutonomyState loaded = store.Load(agentKey);
        string persistedJson = File.ReadAllText(Directory.GetFiles(directory, "*.json").Single());

        Assert.Multiple(() =>
        {
            Assert.That(loaded.LastAuditId, Is.Null);
            Assert.That(loaded.LastFailureKind, Is.Null);
            Assert.That(persistedJson, Does.Not.Contain(overlongValue));
        });
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
