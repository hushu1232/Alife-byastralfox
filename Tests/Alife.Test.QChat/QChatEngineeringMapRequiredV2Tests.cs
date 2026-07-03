using NUnit.Framework;
using System.IO;

namespace Alife.Test.QChat;

[TestFixture]
public sealed class QChatEngineeringMapRequiredV2Tests
{
    static readonly string[] RequiredV2Checks =
    [
        "Vision readiness tests",
        "Voice warmup coordinator tests",
        "Model reply loop live tests",
        "Prompt leak contract tests",
        "Runtime readiness script",
        "Voice warmup retry coordinator",
        "Semantic settle window contract tests",
        "Voice warmup contract tests",
        "XiaYu self-state machine",
        "Persona intensity prompt formatter",
        "Persona frame prompt",
        "XiaYu private state prompt",
        "Semantic window summary prompt",
        "Tool broker runtime wiring",
        "QChat tool route state wiring",
        "DataAgent dynamic tool route contract",
        "QChat owner Tool Broker diagnostics",
        "QChat semantic diagnostics",
        "DataAgent owner evidence diagnostics",
        "QChat recent diagnostics cache",
        "QChat recent diagnostics command",
        "QChat diagnostics cache redaction",
        "DataAgent trace diagnostics",
        "QChat Kalman semantic state estimator",
        "QChat Kalman settle window integration"
    ];

    [Test]
    public void RequiredV2ChecksAreNotDeclaredOptional()
    {
        string repoRoot = FindRepoRoot(TestContext.CurrentContext.TestDirectory);
        string scriptPath = Path.Combine(repoRoot, "tools", "check-qchat-engineering-map.ps1");
        string script = File.ReadAllText(scriptPath);

        foreach (string checkName in RequiredV2Checks)
        {
            string declaration = FindAddCheckDeclaration(script, checkName);

            Assert.Multiple(() =>
            {
                Assert.That(declaration, Is.Not.Empty, $"Missing Add-Check declaration for '{checkName}'.");
                Assert.That(declaration, Does.Not.Contain("-Required $false"), $"'{checkName}' must be required.");
            });
        }
    }

    [Test]
    public void DiagnosticsCacheRedactionCheckRequiresUnsafeInputDetectors()
    {
        string repoRoot = FindRepoRoot(TestContext.CurrentContext.TestDirectory);
        string scriptPath = Path.Combine(repoRoot, "tools", "check-qchat-engineering-map.ps1");
        string script = File.ReadAllText(scriptPath);

        string declaration = FindAddCheckDeclaration(script, "QChat diagnostics cache redaction");

        Assert.Multiple(() =>
        {
            Assert.That(declaration, Does.Contain("HiddenContextPattern"));
            Assert.That(declaration, Does.Contain("SqlFragmentPattern"));
        });
    }

    static string FindAddCheckDeclaration(string script, string checkName)
    {
        string marker = $"-Name \"{checkName}\"";
        int nameIndex = script.IndexOf(marker, StringComparison.Ordinal);
        if (nameIndex < 0)
            return string.Empty;

        int start = script.LastIndexOf("Add-Check", nameIndex, StringComparison.Ordinal);
        if (start < 0)
            return string.Empty;

        int next = script.IndexOf("Add-Check", nameIndex + marker.Length, StringComparison.Ordinal);
        return next < 0
            ? script[start..]
            : script[start..next];
    }

    static string FindRepoRoot(string startDirectory)
    {
        DirectoryInfo? directory = new(startDirectory);
        while (directory != null)
        {
            if (File.Exists(Path.Combine(directory.FullName, "Alife.slnx")) &&
                Directory.Exists(Path.Combine(directory.FullName, "tools")))
            {
                return directory.FullName;
            }

            directory = directory.Parent;
        }

        throw new DirectoryNotFoundException("Could not locate repository root from test directory.");
    }
}
