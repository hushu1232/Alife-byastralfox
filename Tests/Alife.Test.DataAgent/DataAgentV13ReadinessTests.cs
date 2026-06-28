using Alife.Function.DataAgent;

namespace Alife.Test.DataAgent;

[TestFixture]
public sealed class DataAgentV13ReadinessTests
{
    static readonly string[] RequiredChecks =
    [
        "LlmPlannerInterfacePresent",
        "LlmPlannerPromptUsesSchemaSnapshot",
        "LlmPlannerStrictJsonParser",
        "LlmPlannerRejectsInvalidOutput",
        "LlmPlannerFallbackPreservesSafety",
        "ClarificationRequestSupported",
        "NaturalLanguageResultExplanationPresent"
    ];

    [Test]
    public void CoreReadinessIncludesAllV13LlmHarnessChecks()
    {
        string databasePath = NewDatabasePath();

        IReadOnlyDictionary<string, DataAgentReadinessCheck> checks = DataAgentReadiness
            .CheckCore(databasePath)
            .ToDictionary(check => check.Name, StringComparer.Ordinal);

        Assert.Multiple(() =>
        {
            foreach (string checkName in RequiredChecks)
            {
                Assert.That(checks, Does.ContainKey(checkName), checkName);
                Assert.That(checks[checkName].Passed, Is.True, $"{checkName}:{checks[checkName].Detail}");
            }
        });
    }

    [Test]
    public void StaticReadinessScriptContainsAllV13LlmHarnessMarkers()
    {
        string repoRoot = FindRepoRoot(TestContext.CurrentContext.TestDirectory);
        string script = File.ReadAllText(Path.Combine(repoRoot, "tools", "check-dataagent-readiness.ps1"));

        Assert.Multiple(() =>
        {
            foreach (string checkName in RequiredChecks)
                Assert.That(script, Does.Contain(checkName), checkName);
        });
    }

    static string NewDatabasePath()
    {
        string directory = Path.Combine(TestContext.CurrentContext.WorkDirectory, "dataagent-v13-readiness-tests");
        Directory.CreateDirectory(directory);
        return Path.Combine(directory, $"{Guid.NewGuid():N}.sqlite");
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