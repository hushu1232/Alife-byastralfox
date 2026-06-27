using NUnit.Framework;

namespace Alife.Test.Framework;

public sealed class MojibakeRegressionTests
{
    static readonly string[] UserVisibleFiles =
    [
        Path.Combine("sources", "Alife.Function", "Alife.Function.MessageFilter", "AgentWebResearchService.cs"),
        Path.Combine("sources", "Alife.Function", "Alife.Function.QChat", "QChatVisibleReplyPolicy.cs"),
        Path.Combine("sources", "Alife.Function", "Alife.Function.QChat", "QChatVisibleTextPolicy.cs")
    ];

    static readonly string[] Markers =
    [
        "閿",
        "娑",
        "閺",
        "閹",
        "濞",
        "缂",
        "娴",
        "鐎",
        "閳",
        "锟",
        "鏉ユ簮",
        "缁撹",
        "鎼滅储",
        "娌℃煡"
    ];

    [Test]
    public void UserVisibleRuntimeFilesDoNotContainCommonMojibakeMarkers()
    {
        string root = FindRepositoryRoot();
        List<string> failures = [];

        foreach (string relativePath in UserVisibleFiles)
        {
            string path = Path.Combine(root, relativePath);
            string text = File.ReadAllText(path);
            foreach (string marker in Markers)
            {
                if (text.Contains(marker, StringComparison.Ordinal))
                    failures.Add($"{relativePath} contains {marker}");
            }
        }

        Assert.That(failures, Is.Empty, string.Join(Environment.NewLine, failures));
    }

    static string FindRepositoryRoot()
    {
        DirectoryInfo? directory = new(TestContext.CurrentContext.TestDirectory);
        while (directory != null)
        {
            if (Directory.Exists(Path.Combine(directory.FullName, ".git")))
                return directory.FullName;
            directory = directory.Parent;
        }

        throw new InvalidOperationException("Could not locate repository root.");
    }
}
