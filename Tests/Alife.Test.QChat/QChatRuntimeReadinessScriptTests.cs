using NUnit.Framework;
using System.Diagnostics;
using System.IO;
using System.Text.Json;
using System.Threading.Tasks;

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
            Assert.That(result.StandardOutput, Does.Contain("SKIPPED"));
            Assert.That(result.StandardOutput, Does.Contain("Summary:"));
        });
    }

    [Test]
    public void RuntimeReadinessScriptStrictWithoutLiveFailsWithUsageMessage()
    {
        string repoRoot = FindRepoRoot(TestContext.CurrentContext.TestDirectory);
        string scriptPath = Path.Combine(repoRoot, "tools", "check-qchat-runtime-readiness.ps1");

        ScriptResult result = RunPowerShellScript(scriptPath, "-Strict");
        string combinedOutput = result.StandardOutput + result.StandardError;

        Assert.Multiple(() =>
        {
            Assert.That(result.ExitCode, Is.Not.EqualTo(0));
            Assert.That(combinedOutput, Does.Contain("Strict mode requires -Live"));
        });
    }

    [Test]
    public void RuntimeReadinessScriptJsonIncludesModeAndCheckMetadata()
    {
        string repoRoot = FindRepoRoot(TestContext.CurrentContext.TestDirectory);
        string scriptPath = Path.Combine(repoRoot, "tools", "check-qchat-runtime-readiness.ps1");

        ScriptResult result = RunPowerShellScript(scriptPath, "-Json");

        using JsonDocument document = JsonDocument.Parse(result.StandardOutput);
        JsonElement root = document.RootElement;
        JsonElement checks = root.GetProperty("Checks");

        Assert.Multiple(() =>
        {
            Assert.That(result.ExitCode, Is.EqualTo(0), result.StandardError);
            Assert.That(root.GetProperty("AgnesVisionKeyConfigured").ValueKind, Is.EqualTo(JsonValueKind.False).Or.EqualTo(JsonValueKind.True));
            Assert.That(root.GetProperty("XiayuTts9880Reachable").ValueKind, Is.EqualTo(JsonValueKind.False).Or.EqualTo(JsonValueKind.True));
            Assert.That(root.GetProperty("MixuTts9881Reachable").ValueKind, Is.EqualTo(JsonValueKind.False).Or.EqualTo(JsonValueKind.True));
            Assert.That(root.GetProperty("Mode").GetString(), Is.EqualTo("Default"));
            Assert.That(root.GetProperty("Live").GetBoolean(), Is.False);
            Assert.That(root.GetProperty("Strict").GetBoolean(), Is.False);
            Assert.That(root.GetProperty("RequiredFailures").GetArrayLength(), Is.EqualTo(0));
            Assert.That(checks.ValueKind, Is.EqualTo(JsonValueKind.Array));
            Assert.That(checks.GetArrayLength(), Is.GreaterThanOrEqualTo(7));
        });

        JsonElement xiayuTtsCheck = FindCheck(checks, "XiayuTts9880Reachable");
        Assert.Multiple(() =>
        {
            Assert.That(xiayuTtsCheck.GetProperty("Status").GetString(), Is.EqualTo("SKIPPED"));
            Assert.That(xiayuTtsCheck.GetProperty("Checked").GetBoolean(), Is.False);
            Assert.That(xiayuTtsCheck.GetProperty("Required").GetBoolean(), Is.False);
            Assert.That(xiayuTtsCheck.GetProperty("Reason").GetString(), Does.Contain("-Live"));
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
            missingVoiceRoot,
            "-XiayuTtsPort",
            "1",
            "-MixuTtsPort",
            "2");

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
        return RunPowerShellScriptAsync(scriptPath, arguments).GetAwaiter().GetResult();
    }

    static async Task<ScriptResult> RunPowerShellScriptAsync(string scriptPath, params string[] arguments)
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

        Task<string> stdoutTask = process.StandardOutput.ReadToEndAsync();
        Task<string> stderrTask = process.StandardError.ReadToEndAsync();

        Task<bool> exitedTask = WaitForExitAsync(process, 15000);
        if (await exitedTask.ConfigureAwait(false) == false)
        {
            try
            {
                if (process.HasExited == false)
                    process.Kill(entireProcessTree: true);
            }
            catch (InvalidOperationException)
            {
                // The process can exit between the timeout branch and Kill.
            }

            string timedOutStdout = await CompleteReadAfterKill(stdoutTask).ConfigureAwait(false);
            string timedOutStderr = await CompleteReadAfterKill(stderrTask).ConfigureAwait(false);

            throw new TimeoutException(
                "Runtime readiness script did not exit within 15 seconds." +
                $"{Environment.NewLine}Standard output:{Environment.NewLine}{timedOutStdout}" +
                $"{Environment.NewLine}Standard error:{Environment.NewLine}{timedOutStderr}");
        }

        string stdout = await stdoutTask.ConfigureAwait(false);
        string stderr = await stderrTask.ConfigureAwait(false);

        return new ScriptResult(process.ExitCode, stdout, stderr);
    }

    static async Task<bool> WaitForExitAsync(Process process, int timeoutMilliseconds)
    {
        Task processExitTask = process.WaitForExitAsync();
        Task timeoutTask = Task.Delay(timeoutMilliseconds);

        Task completedTask = await Task.WhenAny(processExitTask, timeoutTask).ConfigureAwait(false);
        if (completedTask == processExitTask)
        {
            await processExitTask.ConfigureAwait(false);
            return true;
        }

        return false;
    }

    static async Task<string> CompleteReadAfterKill(Task<string> readTask)
    {
        Task completedTask = await Task.WhenAny(readTask, Task.Delay(5000)).ConfigureAwait(false);
        return completedTask == readTask
            ? await readTask.ConfigureAwait(false)
            : "<stream read did not complete after process kill>";
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

    static JsonElement FindCheck(JsonElement checks, string field)
    {
        foreach (JsonElement check in checks.EnumerateArray())
        {
            if (check.GetProperty("Field").GetString() == field)
                return check;
        }

        throw new AssertionException($"Could not find check metadata for '{field}'.");
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
