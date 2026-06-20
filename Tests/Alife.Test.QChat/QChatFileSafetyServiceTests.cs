using System;
using System.IO;
using Alife.Function.QChat;
using NUnit.Framework;

namespace Alife.Test.QChat;

[TestFixture]
public class QChatFileSafetyServiceTests
{
    [TestCase("a.docx")]
    [TestCase("a.txt")]
    [TestCase("a.md")]
    [TestCase("a.pdf")]
    [TestCase("a.csv")]
    [TestCase("a.json")]
    public void SupportsCommonTextDocumentTypes(string fileName)
    {
        QChatFileSafetyService service = new(CreateTempRoot());

        Assert.That(service.IsSupportedTextLikeFile(fileName), Is.True);
    }

    [Test]
    public void RejectsPathOutsideManagedRootAndAvoidsPrefixBug()
    {
        string root = CreateTempRoot();
        QChatFileSafetyService service = new(root);

        Assert.Multiple(() =>
        {
            Assert.That(service.IsInsideRoot(Path.Combine(root, "xiayu", "file.txt")), Is.True);
            Assert.That(service.IsInsideRoot(Path.Combine(Path.GetDirectoryName(root)!, "outside.txt")), Is.False);
            Assert.That(service.IsInsideRoot(root + "-sibling"), Is.False);
        });
    }

    [Test]
    public void IsInsideRootAcceptsEquivalentTrailingSeparatorRootAndChildPaths()
    {
        string root = CreateTempRoot();
        QChatFileSafetyService service = new(root + Path.DirectorySeparatorChar);

        Assert.Multiple(() =>
        {
            Assert.That(service.IsInsideRoot(root), Is.True);
            Assert.That(service.IsInsideRoot(Path.Combine(root, "child", "file.txt")), Is.True);
            Assert.That(service.IsInsideRoot(root + "-sibling"), Is.False);
        });
    }

    [TestCase(null)]
    [TestCase("")]
    [TestCase(" ")]
    public void IsInsideRootReturnsFalseForEmptyPath(string? path)
    {
        QChatFileSafetyService service = new(CreateTempRoot());

        Assert.That(service.IsInsideRoot(path), Is.False);
    }

    [TestCase(@"..\outside")]
    [TestCase("foo/bar")]
    [TestCase(@"foo\bar")]
    [TestCase("xiayu:bot")]
    public void BuildDownloadFolderSanitizesUnsafeAgentIdSegments(string agentId)
    {
        string root = CreateTempRoot();
        QChatFileSafetyService service = new(root);
        QChatAgentRoute route = new(agentId, 2905391496, QChatConversationKind.Group, 12345, 3045846738, true, "qq:test:2905391496:group:12345");

        string folder = service.BuildDownloadFolder(route, "txt");

        AssertSafeFolder(service, root, folder);
    }

    [Test]
    public void BuildDownloadFolderSanitizesRootedAgentId()
    {
        string root = CreateTempRoot();
        QChatFileSafetyService service = new(root);
        string rootedAgentId = Path.Combine(Path.GetPathRoot(root)!, "outside");
        QChatAgentRoute route = new(rootedAgentId, 2905391496, QChatConversationKind.Group, 12345, 3045846738, true, "qq:test:2905391496:group:12345");

        string folder = service.BuildDownloadFolder(route, "txt");

        AssertSafeFolder(service, root, folder);
        Assert.That(folder, Is.EqualTo(Path.Combine(root, "agent", "group-12345", "txt")));
    }

    [TestCase("..", "file")]
    [TestCase(".", "file")]
    [TestCase("", "file")]
    [TestCase(" ", "file")]
    [TestCase(@"folder\name", "folder_name")]
    [TestCase("folder/name", "folder_name")]
    [TestCase("bad:name", "bad_name")]
    public void BuildDownloadFolderSanitizesUnsafeCategories(string category, string expectedCategory)
    {
        string root = CreateTempRoot();
        QChatFileSafetyService service = new(root);
        QChatAgentRoute route = new("xiayu", 2905391496, QChatConversationKind.Private, 3045846738, 3045846738, true, "qq:xiayu:2905391496:private:3045846738");

        string folder = service.BuildDownloadFolder(route, category);

        AssertSafeFolder(service, root, folder);
        Assert.That(folder, Is.EqualTo(Path.Combine(root, "xiayu", "private-3045846738", expectedCategory)));
    }

    [Test]
    public void BuildDownloadFolderSanitizesRootedCategory()
    {
        string root = CreateTempRoot();
        QChatFileSafetyService service = new(root);
        string rootedCategory = Path.Combine(Path.GetPathRoot(root)!, "outside");
        QChatAgentRoute route = new("xiayu", 2905391496, QChatConversationKind.Private, 3045846738, 3045846738, true, "qq:xiayu:2905391496:private:3045846738");

        string folder = service.BuildDownloadFolder(route, rootedCategory);

        AssertSafeFolder(service, root, folder);
        Assert.That(folder, Is.EqualTo(Path.Combine(root, "xiayu", "private-3045846738", "file")));
    }

    [Test]
    public void BuildDownloadFolderForGroupRouteUsesAgentPeerAndCategory()
    {
        string root = CreateTempRoot();
        QChatFileSafetyService service = new(root);
        QChatAgentRoute route = new("xiayu", 2905391496, QChatConversationKind.Group, 12345, 3045846738, true, "qq:xiayu:2905391496:group:12345");

        string folder = service.BuildDownloadFolder(route, ".docx");

        Assert.That(folder, Is.EqualTo(Path.Combine(root, "xiayu", "group-12345", "docx")));
    }

    [Test]
    public void BuildDownloadFolderForPrivateRouteUsesAgentPeerAndCategory()
    {
        string root = CreateTempRoot();
        QChatFileSafetyService service = new(root);
        QChatAgentRoute route = new("xiayu", 2905391496, QChatConversationKind.Private, 3045846738, 3045846738, true, "qq:xiayu:2905391496:private:3045846738");

        string folder = service.BuildDownloadFolder(route, "txt");

        Assert.That(folder, Is.EqualTo(Path.Combine(root, "xiayu", "private-3045846738", "txt")));
    }

    static string CreateTempRoot()
    {
        return Path.Combine(Path.GetTempPath(), "alife-qchat-file-safety-tests", Guid.NewGuid().ToString("N"));
    }

    static void AssertSafeFolder(QChatFileSafetyService service, string root, string folder)
    {
        Assert.That(service.IsInsideRoot(folder), Is.True);

        string relative = Path.GetRelativePath(root, folder);
        Assert.That(Path.IsPathRooted(relative), Is.False);
        foreach (string segment in relative.Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar))
        {
            Assert.That(segment, Is.Not.EqualTo("."));
            Assert.That(segment, Is.Not.EqualTo(".."));
            Assert.That(segment, Does.Not.Contain(Path.DirectorySeparatorChar.ToString()));
            Assert.That(segment, Does.Not.Contain(Path.AltDirectorySeparatorChar.ToString()));
        }
    }
}
