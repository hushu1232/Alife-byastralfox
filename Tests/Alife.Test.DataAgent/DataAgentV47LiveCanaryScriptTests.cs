using System.Diagnostics;
using System.Text;

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
            Assert.That(script, Does.Contain("Add-Type -AssemblyName System.Net.Http"));
            Assert.That(script.Split("Write-Output \"PASS", StringSplitOptions.None).Length - 1,
                Is.EqualTo(5));
        });
    }

    [Test]
    public void OperatorScriptStagesArtifactAndPromotesOnlyAfterConfirmedCleanup()
    {
        string root = FindRepoRoot(TestContext.CurrentContext.TestDirectory);
        string script = File.ReadAllText(Path.Combine(
            root, "tools", "run-dataagent-v47-live-canary.ps1"));

        int removeCanonical = script.IndexOf(
            "Remove-Item -LiteralPath $canonicalArtifactPath", StringComparison.Ordinal);
        int startOwnedProcess = script.IndexOf("Start-Process", StringComparison.Ordinal);
        int enforcedWait = script.IndexOf(
            "$ownedProcess.WaitForExit(5000) -eq $false", StringComparison.Ordinal);
        int killRestored = script.IndexOf(
            "Write-Output \"kill_switch_restored=true\"", StringComparison.Ordinal);
        int shadowRestored = script.IndexOf(
            "Write-Output \"production_shadow_restored_disabled=true\"", StringComparison.Ordinal);
        int promoteArtifact = script.IndexOf(
            "Move-Item -LiteralPath $pendingArtifactPath -Destination $canonicalArtifactPath",
            StringComparison.Ordinal);

        Assert.Multiple(() =>
        {
            Assert.That(script, Does.Contain(
                "$artifactFileName = \"dataagent-v4.7-live-canary-closure.txt\""));
            Assert.That(script, Does.Contain(
                "$pathRoot = [System.IO.Path]::GetPathRoot($fullPath)"));
            Assert.That(script, Does.Contain(
                "if ($fullPath.Length -le $pathRoot.Length)"));
            Assert.That(script, Does.Contain("$pendingOutputPath"));
            Assert.That(script, Does.Contain("$pendingArtifactPath"));
            Assert.That(script, Does.Contain("$canonicalArtifactPath"));
            Assert.That(script, Does.Contain("--output $pendingOutputPath"));
            Assert.That(script, Does.Not.Contain("--output $outputPath"));
            Assert.That(removeCanonical, Is.GreaterThanOrEqualTo(0));
            Assert.That(startOwnedProcess, Is.GreaterThan(removeCanonical));
            Assert.That(enforcedWait, Is.GreaterThan(startOwnedProcess));
            Assert.That(killRestored, Is.GreaterThan(enforcedWait));
            Assert.That(shadowRestored, Is.GreaterThan(killRestored));
            Assert.That(promoteArtifact, Is.GreaterThan(shadowRestored));
            Assert.That(script, Does.Contain(
                "if ($ownedProcess.HasExited -eq $false)"));
            Assert.That(script, Does.Contain(
                "if ((Test-Path -LiteralPath $pendingArtifactPath -PathType Leaf) -eq $false)"));
            Assert.That(script, Does.Not.Contain("Move-Item -Recurse"));
            Assert.That(script, Does.Not.Contain("Remove-Item -Recurse"));
        });
    }

    [TestCase("sources")]
    [TestCase("sources/nested")]
    [TestCase("Tests")]
    [TestCase("Tests/nested")]
    [TestCase("tools")]
    [TestCase("tools/nested")]
    [TestCase("docs")]
    [TestCase("docs/nested")]
    public void OperatorScriptRejectsExactTrackedRootsAndDescendantsBeforeProcessStart(
        string relativeOutput)
    {
        string root = FindRepoRoot(TestContext.CurrentContext.TestDirectory);
        string output = Path.Combine(
            root, relativeOutput.Replace('/', Path.DirectorySeparatorChar));
        string sentinel = Path.Combine(
            Path.GetTempPath(), $"dataagent-v47-must-not-start-{Guid.NewGuid():N}.exe");

        ScriptResult result = RunOperatorScript(root, output, sentinel);

        Assert.Multiple(() =>
        {
            Assert.That(result.ExitCode, Is.Not.Zero);
            Assert.That(result.CombinedOutput,
                Does.Contain("Output directory must be outside tracked source directories."));
            Assert.That(result.CombinedOutput, Does.Not.Contain(sentinel));
        });
    }

    [Test]
    public void OperatorScriptRejectsExistingFileOutputBeforeProcessStart()
    {
        string root = FindRepoRoot(TestContext.CurrentContext.TestDirectory);
        string occupiedPath = Path.Combine(
            Path.GetTempPath(), $"dataagent-v47-output-file-{Guid.NewGuid():N}.txt");
        string sentinel = Path.Combine(
            Path.GetTempPath(), $"dataagent-v47-must-not-start-{Guid.NewGuid():N}.exe");
        File.WriteAllText(occupiedPath, "occupied-output-root");

        try
        {
            ScriptResult result = RunOperatorScript(root, occupiedPath, sentinel);
            Assert.Multiple(() =>
            {
                Assert.That(result.ExitCode, Is.Not.Zero);
                Assert.That(result.CombinedOutput, Does.Contain("Output directory is invalid."));
                Assert.That(result.CombinedOutput, Does.Not.Contain(sentinel));
                Assert.That(File.ReadAllText(occupiedPath), Is.EqualTo("occupied-output-root"));
            });
        }
        finally
        {
            File.Delete(occupiedPath);
        }
    }

    [Test]
    public void FailedRunRemovesPriorCanonicalArtifactAndPublishesNoRestorationMarkers()
    {
        string root = FindRepoRoot(TestContext.CurrentContext.TestDirectory);
        string output = Path.Combine(
            Path.GetTempPath(), $"dataagent-v47-wrapper-failure-{Guid.NewGuid():N}");
        string canonical = Path.Combine(output, "dataagent-v4.7-live-canary-closure.txt");
        string sentinel = Path.Combine(
            Path.GetTempPath(), $"dataagent-v47-start-failure-{Guid.NewGuid():N}.exe");
        Directory.CreateDirectory(output);
        File.WriteAllText(canonical, "stale-canonical-artifact");

        try
        {
            ScriptResult result = RunOperatorScript(root, output, sentinel);
            Assert.Multiple(() =>
            {
                Assert.That(result.ExitCode, Is.Not.Zero);
                Assert.That(File.Exists(canonical), Is.False);
                Assert.That(result.CombinedOutput, Does.Not.Contain("kill_switch_restored=true"));
                Assert.That(result.CombinedOutput,
                    Does.Not.Contain("production_shadow_restored_disabled=true"));
            });
        }
        finally
        {
            if (Directory.Exists(output))
                Directory.Delete(output, recursive: true);
        }
    }

    static ScriptResult RunOperatorScript(string root, string output, string python)
    {
        string powerShell = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.System),
            "WindowsPowerShell", "v1.0", "powershell.exe");
        ProcessStartInfo startInfo = new()
        {
            FileName = powerShell,
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true,
            StandardOutputEncoding = Encoding.UTF8,
            StandardErrorEncoding = Encoding.UTF8
        };
        foreach (string argument in new[]
        {
            "-NoLogo", "-NoProfile", "-ExecutionPolicy", "Bypass", "-File",
            Path.Combine(root, "tools", "run-dataagent-v47-live-canary.ps1"),
            "-Python", python,
            "-Port", "8765",
            "-OutputDirectory", output,
            "-RequestCount", "20",
            "-RuntimeRestartCount", "0"
        })
        {
            startInfo.ArgumentList.Add(argument);
        }

        using Process process = Process.Start(startInfo)
            ?? throw new InvalidOperationException("operator_script_process_start_failed");
        string standardOutput = process.StandardOutput.ReadToEnd();
        string standardError = process.StandardError.ReadToEnd();
        if (process.WaitForExit(15000) == false)
        {
            process.Kill(entireProcessTree: true);
            process.WaitForExit();
            throw new TimeoutException("operator_script_did_not_exit");
        }
        return new(process.ExitCode, standardOutput, standardError);
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

    readonly record struct ScriptResult(
        int ExitCode,
        string StandardOutput,
        string StandardError)
    {
        public string CombinedOutput => StandardOutput + Environment.NewLine + StandardError;
    }
}
