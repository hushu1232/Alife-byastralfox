using System.Diagnostics;

namespace Alife.Test.DataAgent;

[TestFixture]
public sealed class DataAgentV318SmokeResultArtifactTests
{
    [Test]
    public void SmokeResultFormatterIsManualOnlyAndDoesNotStartRuntime()
    {
        string repoRoot = FindRepoRoot(TestContext.CurrentContext.TestDirectory);
        string script = File.ReadAllText(Path.Combine(repoRoot, "tools", "format-dataagent-langgraph-smoke-result.ps1"));

        Assert.Multiple(() =>
        {
            Assert.That(script, Does.Contain("artifact_formatter=true"));
            Assert.That(script, Does.Contain("manual_only=true"));
            Assert.That(script, Does.Contain("stores_secrets=false"));
            Assert.That(script, Does.Contain("stores_sql=false"));
            Assert.That(script, Does.Contain("stores_hidden_context=false"));
            Assert.That(script, Does.Contain("sanitizes_unsafe_text=true"));
            Assert.That(script, Does.Not.Contain("Start-Process"));
            Assert.That(script, Does.Not.Contain("pip install"));
            Assert.That(script, Does.Not.Contain("python -m venv"));
            Assert.That(script, Does.Not.Contain("uvicorn"));
        });
    }

    [Test]
    public void SmokeResultFormatterWritesSanitizedArtifact()
    {
        string repoRoot = FindRepoRoot(TestContext.CurrentContext.TestDirectory);
        string scriptPath = Path.Combine(repoRoot, "tools", "format-dataagent-langgraph-smoke-result.ps1");
        string directory = Path.Combine(TestContext.CurrentContext.WorkDirectory, "v318-smoke-artifact");
        Directory.CreateDirectory(directory);
        string inputPath = Path.Combine(directory, "unsafe-response.json");
        string outputPath = Path.Combine(directory, "artifact.json");
        File.WriteAllText(inputPath, """
        {
          "RequestId": "manual-smoke-valid",
          "Accepted": true,
          "ReasonCode": "langgraph_skeleton_advisory",
          "TraceSummary": "SELECT * FROM hidden_context WHERE bearer_token = secret",
          "ContextContribution": "qchat visible text should not be stored",
          "FallbackRequired": true,
          "NoSqlAuthority": true,
          "ReadOnly": true,
          "RequestsCheckpointMutation": false,
          "RequestsVisibleText": false
        }
        """);

        ScriptResult result = RunPowerShell(scriptPath, inputPath, outputPath);
        string artifact = File.ReadAllText(outputPath);

        Assert.Multiple(() =>
        {
            Assert.That(result.ExitCode, Is.EqualTo(0), result.StandardError);
            Assert.That(artifact, Does.Contain("\"artifact_formatter\": true"));
            Assert.That(artifact, Does.Contain("\"manual_only\": true"));
            Assert.That(artifact, Does.Contain("\"unsafe_text_redacted\": true"));
            Assert.That(artifact, Does.Contain("\"trace_summary\": \"redacted\""));
            Assert.That(artifact, Does.Contain("\"context_contribution\": \"redacted\""));
            Assert.That(artifact, Does.Not.Contain("SELECT"));
            Assert.That(artifact, Does.Not.Contain("hidden_context"));
            Assert.That(artifact, Does.Not.Contain("bearer"));
            Assert.That(artifact, Does.Not.Contain("secret"));
            Assert.That(artifact, Does.Not.Contain("qchat"));
        });
    }

    [Test]
    public void V318DocumentDeclaresSmokeResultArtifactBoundary()
    {
        string repoRoot = FindRepoRoot(TestContext.CurrentContext.TestDirectory);
        string doc = File.ReadAllText(Path.Combine(repoRoot, "docs", "dataagent", "dataagent-v3.18-smoke-result-artifact.md"));

        Assert.Multiple(() =>
        {
            Assert.That(doc, Does.Contain("smoke_result_artifact=true"));
            Assert.That(doc, Does.Contain("artifact_formatter=true"));
            Assert.That(doc, Does.Contain("manual_only=true"));
            Assert.That(doc, Does.Contain("stores_secrets=false"));
            Assert.That(doc, Does.Contain("stores_sql=false"));
            Assert.That(doc, Does.Contain("stores_hidden_context=false"));
            Assert.That(doc, Does.Contain("sanitizes_unsafe_text=true"));
            Assert.That(doc, Does.Contain("default_result_changed=false"));
        });
    }

    static ScriptResult RunPowerShell(string scriptPath, string inputPath, string outputPath)
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
        startInfo.ArgumentList.Add("-InputPath");
        startInfo.ArgumentList.Add(inputPath);
        startInfo.ArgumentList.Add("-OutputPath");
        startInfo.ArgumentList.Add(outputPath);

        using Process process = Process.Start(startInfo)
            ?? throw new InvalidOperationException("Failed to start PowerShell.");
        string stdout = process.StandardOutput.ReadToEnd();
        string stderr = process.StandardError.ReadToEnd();
        if (process.WaitForExit(15000) == false)
        {
            process.Kill(entireProcessTree: true);
            throw new TimeoutException("PowerShell formatter did not exit within 15 seconds.");
        }

        return new ScriptResult(process.ExitCode, stdout, stderr);
    }

    static string FindRepoRoot(string startDirectory)
    {
        DirectoryInfo? directory = new(startDirectory);
        while (directory != null)
        {
            if (File.Exists(Path.Combine(directory.FullName, "Alife.slnx")) &&
                Directory.Exists(Path.Combine(directory.FullName, "docs")) &&
                Directory.Exists(Path.Combine(directory.FullName, "tools")))
            {
                return directory.FullName;
            }

            directory = directory.Parent;
        }

        throw new DirectoryNotFoundException("Could not locate repository root.");
    }

    readonly record struct ScriptResult(int ExitCode, string StandardOutput, string StandardError);
}
