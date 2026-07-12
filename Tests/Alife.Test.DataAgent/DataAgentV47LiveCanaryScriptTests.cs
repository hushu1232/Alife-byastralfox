namespace Alife.Test.DataAgent;

public sealed class DataAgentV47LiveCanaryScriptTests
{
    [Test]
    public void OperatorScriptHasBoundedOwnedLifecycleAndExactCanaryInputs()
    {
        string root = FindRepoRoot(TestContext.CurrentContext.TestDirectory);
        string path = Path.Combine(root, "tools", "run-dataagent-v47-live-canary.ps1");
        Assert.That(File.Exists(path), Is.True);
        string script = File.ReadAllText(path);

        Assert.Multiple(() =>
        {
            foreach (string parameter in new[] { "$Python", "$Port", "$OutputDirectory", "$RequestCount", "$RuntimeRestartCount" })
                Assert.That(script, Does.Contain(parameter));
            Assert.That(script, Does.Contain("[int]$Port = 8765"));
            Assert.That(script, Does.Contain("[int]$RequestCount = 20"));
            Assert.That(script, Does.Contain("--runtime-mode"));
            Assert.That(script, Does.Contain("langgraph"));
            Assert.That(script, Does.Contain("Start-Process"));
            Assert.That(script, Does.Contain("-WindowStyle Hidden"));
            Assert.That(script, Does.Contain("-PassThru"));
            Assert.That(script, Does.Contain("run-dataagent-langgraph-manual-smoke.ps1"));
            Assert.That(script, Does.Contain("-ExpectedContractVersion"));
            Assert.That(script, Does.Contain("v4.7"));
            Assert.That(script, Does.Contain("dataagent-v47-canary"));
            Assert.That(script, Does.Contain("Stop-Process -Id $ownedProcess.Id"));
            Assert.That(script, Does.Contain("finally"));
            Assert.That(script, Does.Contain("kill_switch_restored=true"));
            Assert.That(script, Does.Contain("production_shadow_restored_disabled=true"));
            Assert.That(script, Does.Not.Contain("pip install"));
            Assert.That(script, Does.Not.Contain("NapCat"));
            Assert.That(script, Does.Not.Contain("QQ"));
            Assert.That(script, Does.Not.Contain("Remove-Item -Recurse"));
        });
    }

    [Test]
    public void FiveItemSmokeAcceptsOnlyBoundedExpectedV4Contract()
    {
        string root = FindRepoRoot(TestContext.CurrentContext.TestDirectory);
        string script = File.ReadAllText(Path.Combine(root, "tools", "run-dataagent-langgraph-manual-smoke.ps1"));
        Assert.Multiple(() =>
        {
            Assert.That(script, Does.Contain("$ExpectedContractVersion = \"v4.7\""));
            Assert.That(script, Does.Contain("^v4\\.[0-9]+$"));
            Assert.That(script, Does.Contain("$health.contractVersion -ne $ExpectedContractVersion"));
            Assert.That(script.Split("Write-Output \"PASS", StringSplitOptions.None).Length - 1,
                Is.EqualTo(5));
        });
    }

    static string FindRepoRoot(string start)
    {
        DirectoryInfo? current = new(start);
        while (current is not null)
        {
            if (Directory.Exists(Path.Combine(current.FullName, "sources")) &&
                Directory.Exists(Path.Combine(current.FullName, "tools")))
                return current.FullName;
            current = current.Parent;
        }
        throw new DirectoryNotFoundException("repo_root_not_found");
    }
}
