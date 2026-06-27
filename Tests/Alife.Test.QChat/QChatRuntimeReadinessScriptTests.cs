using NUnit.Framework;
using System.Diagnostics;
using System.IO;

namespace Alife.Test.QChat;

[TestFixture]
public sealed class QChatRuntimeReadinessScriptTests
{
    [Test]
    public void EngineeringMapDeclaresRuntimeReadinessAsRequired()
    {
        string repoRoot = FindRepoRoot(TestContext.CurrentContext.TestDirectory);
        string engineeringMapPath = Path.Combine(repoRoot, "tools", "check-qchat-engineering-map.ps1");
        string script = File.ReadAllText(engineeringMapPath);

        string declaration = FindAddCheckDeclaration(script, "Runtime readiness script");

        Assert.Multiple(() =>
        {
            Assert.That(declaration, Is.Not.Empty);
            Assert.That(declaration, Does.Not.Contain("-Required $false"));
            Assert.That(declaration, Does.Contain("QChat Runtime Readiness"));
            Assert.That(declaration, Does.Contain("-Live"));
            Assert.That(declaration, Does.Contain("-Strict"));
            Assert.That(declaration, Does.Contain("exit 1"));
        });
    }

    [Test]
    public void RuntimeReadinessScriptKeepsStableContractMarkers()
    {
        string repoRoot = FindRepoRoot(TestContext.CurrentContext.TestDirectory);
        string scriptPath = Path.Combine(repoRoot, "tools", "check-qchat-runtime-readiness.ps1");
        string script = File.ReadAllText(scriptPath);

        string[] markers =
        [
            "QChat Runtime Readiness",
            "AgnesVisionKeyConfigured",
            "XiayuTts9880Reachable",
            "MixuTts9881Reachable",
            "XiayuZhRef",
            "XiayuJaRef",
            "MixuZhRef",
            "MixuJaRef",
            "-Live",
            "-Strict",
            "-Json",
            "exit 1"
        ];

        Assert.Multiple(() =>
        {
            foreach (string marker in markers)
                Assert.That(script, Does.Contain(marker), $"Missing marker '{marker}'.");
        });
    }

    [Test]
    public void RuntimeReadinessScriptDefaultModeExitsZeroAndPrintsSummary()
    {
        string repoRoot = FindRepoRoot(TestContext.CurrentContext.TestDirectory);
        string scriptPath = Path.Combine(repoRoot, "tools", "check-qchat-runtime-readiness.ps1");

        ScriptResult result = RunPowerShellScript(scriptPath);

        Assert.Multiple(() =>
        {
            Assert.That(result.ExitCode, Is.EqualTo(0), result.StandardError);
            Assert.That(result.StandardOutput, Does.Contain("QChat Runtime Readiness"));
            Assert.That(result.StandardOutput, Does.Contain("[Vision]"));
            Assert.That(result.StandardOutput, Does.Contain("[Voice]"));
            Assert.That(result.StandardOutput, Does.Contain("Summary:"));
        });
    }

    [Test]
    public void RuntimeReadinessScriptLiveStrictFailsWhenReferenceAudioRootIsMissing()
    {
        string repoRoot = FindRepoRoot(TestContext.CurrentContext.TestDirectory);
        string scriptPath = Path.Combine(repoRoot, "tools", "check-qchat-runtime-readiness.ps1");
        string missingVoiceRoot = Path.Combine(TestContext.CurrentContext.WorkDirectory, "missing-qchat-voice-root");

        if (Directory.Exists(missingVoiceRoot))
            Directory.Delete(missingVoiceRoot, recursive: true);

        ScriptResult result = RunPowerShellScript(
            scriptPath,
            "-Live",
            "-Strict",
            "-VoiceRootPath",
            missingVoiceRoot);

        Assert.Multiple(() =>
        {
            Assert.That(result.ExitCode, Is.Not.EqualTo(0));
            Assert.That(result.StandardOutput, Does.Contain("QChat Runtime Readiness"));
            Assert.That(result.StandardOutput, Does.Contain("MISSING"));
            Assert.That(result.StandardOutput, Does.Contain("reference audio"));
        });
    }

    static ScriptResult RunPowerShellScript(string scriptPath, params string[] arguments)
    {
        string powerShell = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.System),
            "WindowsPowerShell",
            "v1.0",
            "powershell.exe");

        ProcessStartInfo startInfo = new()
        {
            FileName = powerShell,
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true
        };

        startInfo.ArgumentList.Add("-NoLogo");
        startInfo.ArgumentList.Add("-NoProfile");
        startInfo.ArgumentList.Add("-ExecutionPolicy");
        startInfo.ArgumentList.Add("Bypass");
        startInfo.ArgumentList.Add("-File");
        startInfo.ArgumentList.Add(scriptPath);

        foreach (string argument in arguments)
            startInfo.ArgumentList.Add(argument);

        using Process process = Process.Start(startInfo)
            ?? throw new InvalidOperationException("Failed to start PowerShell.");

        string stdout = process.StandardOutput.ReadToEnd();
        string stderr = process.StandardError.ReadToEnd();

        if (process.WaitForExit(15000) == false)
        {
            process.Kill(entireProcessTree: true);
            throw new TimeoutException("Runtime readiness script did not exit within 15 seconds.");
        }

        return new ScriptResult(process.ExitCode, stdout, stderr);
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

    readonly record struct ScriptResult(int ExitCode, string StandardOutput, string StandardError);
}
