using Alife.Function.DesktopControl;

namespace Alife.Test.DesktopControl;

[TestFixture]
public sealed class DesktopFileAccessPolicyTests
{
    [Test]
    public void CreateDefault_DeniesReadingSensitiveCredentialFolders()
    {
        DesktopFileAccessPolicy policy = DesktopFileAccessPolicy.CreateDefault();

        DesktopFileAccessDecision decision = policy.CanRead(Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
            ".ssh",
            "id_rsa"));

        Assert.Multiple(() =>
        {
            Assert.That(decision.Allowed, Is.False);
            Assert.That(decision.Reason, Is.EqualTo("read_blacklisted_path"));
        });
    }

    [Test]
    public void CreateDefault_DeniesReadingCodexConfiguration()
    {
        DesktopFileAccessPolicy policy = DesktopFileAccessPolicy.CreateDefault();

        DesktopFileAccessDecision decision = policy.CanRead(Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
            ".codex",
            "config.toml"));

        Assert.Multiple(() =>
        {
            Assert.That(decision.Allowed, Is.False);
            Assert.That(decision.Reason, Is.EqualTo("read_blacklisted_path"));
        });
    }

    [Test]
    public void CreateDefault_DeniesWritingProtectedSystemPaths()
    {
        DesktopFileAccessPolicy policy = DesktopFileAccessPolicy.CreateDefault();

        DesktopFileAccessDecision decision = policy.CanWrite(@"C:\Windows\System32\blocked.txt");

        Assert.Multiple(() =>
        {
            Assert.That(decision.Allowed, Is.False);
            Assert.That(decision.Reason, Is.EqualTo("write_denied_path"));
        });
    }

    [Test]
    public void CreateDefault_DeniesWritingRepositoryMetadata()
    {
        DesktopFileAccessPolicy policy = DesktopFileAccessPolicy.CreateDefault();

        DesktopFileAccessDecision decision = policy.CanWrite(@"D:\Alife\.git\config");

        Assert.Multiple(() =>
        {
            Assert.That(decision.Allowed, Is.False);
            Assert.That(decision.Reason, Is.EqualTo("write_denied_path"));
        });
    }

    [Test]
    public void CreateDefault_DeniesNeutralWritesBecauseFileMutationIsDisabledByDefault()
    {
        DesktopFileAccessPolicy policy = DesktopFileAccessPolicy.CreateDefault();

        DesktopFileAccessDecision decision = policy.CanWrite(@"D:\tmp\alife-neutral\notes.txt");

        Assert.Multiple(() =>
        {
            Assert.That(decision.Allowed, Is.False);
            Assert.That(decision.Reason, Is.EqualTo("file_mutation_disabled"));
        });
    }

    [Test]
    public void CustomPolicy_NormalizesCaseTrailingSeparatorsAndRelativeSegments()
    {
        DesktopFileAccessPolicy policy = new(
            readBlacklistPaths: [@"D:\SecretRoot\"],
            writeDenyPaths: [@"D:\ProtectedRoot\"],
            allowFileMutationByDefault: true);

        DesktopFileAccessDecision readDecision = policy.CanRead(@"d:\secretroot\child\..\token.txt");
        DesktopFileAccessDecision writeDecision = policy.CanWrite(@"d:\protectedroot\child\..\note.txt");

        Assert.Multiple(() =>
        {
            Assert.That(readDecision.Allowed, Is.False);
            Assert.That(readDecision.Reason, Is.EqualTo("read_blacklisted_path"));
            Assert.That(writeDecision.Allowed, Is.False);
            Assert.That(writeDecision.Reason, Is.EqualTo("write_denied_path"));
        });
    }

    [Test]
    public void CreateDefault_DeniesReadingThroughDirectorySymbolicLink()
    {
        string root = Path.Combine(Path.GetTempPath(), "alife-file-policy-link-tests", Guid.NewGuid().ToString("N"));
        string target = Path.Combine(root, "target");
        string link = Path.Combine(root, "link");
        Directory.CreateDirectory(target);
        File.WriteAllText(Path.Combine(target, "secret.txt"), "secret");
        try
        {
            try
            {
                Directory.CreateSymbolicLink(link, target);
            }
            catch (Exception exception) when (exception is UnauthorizedAccessException or IOException or PlatformNotSupportedException)
            {
                Assert.Ignore($"Symbolic links are unavailable in this test environment: {exception.GetType().Name}");
                return;
            }

            DesktopFileAccessDecision decision = DesktopFileAccessPolicy.CreateDefault()
                .CanRead(Path.Combine(link, "secret.txt"));

            Assert.Multiple(() =>
            {
                Assert.That(decision.Allowed, Is.False);
                Assert.That(decision.Reason, Is.EqualTo("read_reparse_point"));
            });
        }
        finally
        {
            if (Directory.Exists(link))
                Directory.Delete(link);
            if (Directory.Exists(root))
                Directory.Delete(root, recursive: true);
        }
    }

    [Test]
    public void FormatForOwner_ReturnsCompactSummaryWithoutPathContents()
    {
        DesktopFileAccessPolicy policy = new(
            readBlacklistPaths: [@"D:\Secret"],
            writeDenyPaths: [@"D:\Protected", @"D:\System"],
            allowFileMutationByDefault: false);

        string summary = policy.FormatForOwner();

        Assert.Multiple(() =>
        {
            Assert.That(summary, Does.Contain("file_policy=enabled"));
            Assert.That(summary, Does.Contain("read_blacklist_entries=1"));
            Assert.That(summary, Does.Contain("write_deny_entries=2"));
            Assert.That(summary, Does.Contain("default_file_mutation=denied"));
            Assert.That(summary, Does.Not.Contain(@"D:\Secret"));
            Assert.That(summary, Does.Not.Contain(@"D:\Protected"));
        });
    }
}
