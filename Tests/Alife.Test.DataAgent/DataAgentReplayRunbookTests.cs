using System.Diagnostics;
using System.Text.Json;
using Alife.Tools.DataAgentReplay;

namespace Alife.Test.DataAgent;

[TestFixture]
[NonParallelizable]
public sealed class DataAgentReplayRunbookTests
{
    [Test]
    public void DefaultFixtureContainsV39OwnerReadinessReplayShape()
    {
        string fixturePath = DefaultFixturePath();
        string fixtureText = File.ReadAllText(fixturePath);

        using JsonDocument document = JsonDocument.Parse(fixtureText);
        JsonElement root = document.RootElement;

        Assert.Multiple(() =>
        {
            Assert.That(root.GetProperty("version").GetString(), Is.EqualTo("v3.9"));
            Assert.That(root.GetProperty("name").GetString(), Is.EqualTo("owner-readiness-analysis"));
            Assert.That(root.GetProperty("callerId").GetString(), Is.EqualTo("owner"));
            Assert.That(root.GetProperty("utterance").GetString(), Is.EqualTo("DataAgent analyze project readiness"));
            Assert.That(root.GetProperty("routeState").GetProperty("isOwner").GetBoolean(), Is.True);
            Assert.That(root.GetProperty("routeState").GetProperty("isPrivate").GetBoolean(), Is.True);
            Assert.That(root.GetProperty("routeState").GetProperty("trustedRuntime").GetBoolean(), Is.True);
            Assert.That(root.GetProperty("planner").GetProperty("dataset").GetString(), Is.EqualTo("document_index"));
            Assert.That(root.GetProperty("planner").GetProperty("intent").GetString(), Is.EqualTo("find_dataagent_documents"));
            Assert.That(root.GetProperty("expectedMarkers").EnumerateArray().Select(item => item.GetString()), Does.Contain("sidecar_authority=false"));
            Assert.That(root.GetProperty("expectedMarkers").EnumerateArray().Select(item => item.GetString()), Does.Contain("default_tests_live_runtime=false"));
        });
    }

    [Test]
    public void RunnerExecutesDefaultFixtureThroughRealOfflineChain()
    {
        DataAgentReplayFixture fixture = DataAgentReplayFixture.Load(DefaultFixturePath());
        DataAgentReplayResult result = DataAgentReplayRunner.Run(fixture);

        Assert.Multiple(() =>
        {
            Assert.That(result.Passed, Is.True);
            Assert.That(result.Route.ReasonCode, Is.EqualTo("route_allowed"));
            Assert.That(result.Route.AllowedTools, Does.Contain("dataagent_analysis_start"));
            Assert.That(result.XmlPolicy.Allowed, Is.True);
            Assert.That(result.RouteContext.Present, Is.True);
            Assert.That(result.RouteContext.ToolName, Is.EqualTo("dataagent_analysis_start"));
            Assert.That(result.RouteContext.AllowsQuery, Is.True);
            Assert.That(result.Orchestration.Trace, Does.Contain("RouteGate:Succeeded"));
            Assert.That(result.Orchestration.Trace, Does.Contain("Execute:Succeeded"));
            Assert.That(result.Session.SessionId, Is.Not.Empty);
            Assert.That(result.Diagnostics.Evidence, Does.Contain("DataAgent evidence diagnostics"));
            Assert.That(result.Diagnostics.Trace, Does.Contain("DataAgent trace diagnostics"));
            Assert.That(result.Diagnostics.Progress, Does.Contain("DataAgent progress diagnostics"));
            Assert.That(result.Diagnostics.Graph, Does.Contain("graph_sidecar"));
            Assert.That(result.Diagnostics.Graph, Does.Contain("sidecar_disabled"));
            Assert.That(result.Diagnostics.Graph, Does.Not.Contain("127.0.0.1"));
            Assert.That(result.Diagnostics.Graph, Does.Not.Contain("localhost"));
            Assert.That(result.OfflineBoundary.SidecarAuthority, Is.False);
            Assert.That(result.OfflineBoundary.DefaultTestsLiveRuntime, Is.False);
            Assert.That(result.ExpectedMarkers, Has.Count.EqualTo(fixture.ExpectedMarkers.Count));
            Assert.That(result.ExpectedMarkers.Select(marker => marker.Marker), Does.Contain("RouteGate:Succeeded"));
            Assert.That(result.ExpectedMarkers.Select(marker => marker.Marker), Does.Contain("sidecar_authority=false"));
            Assert.That(result.ExpectedMarkers.Select(marker => marker.Marker), Does.Contain("default_tests_live_runtime=false"));
            Assert.That(result.ExpectedMarkers.All(marker => marker.Passed), Is.True);
        });
    }

    [Test]
    public void MarkdownFormatterEmitsStableRunbookSections()
    {
        DataAgentReplayResult result = DataAgentReplayRunner.Run(DataAgentReplayFixture.Load(DefaultFixturePath()));

        string markdown = DataAgentReplayReportFormatter.FormatMarkdown(result);

        Assert.Multiple(() =>
        {
            Assert.That(markdown, Does.Contain("# DataAgent Replay: owner-readiness-analysis"));
            Assert.That(markdown, Does.Contain("## Fixture"));
            Assert.That(markdown, Does.Contain("## Route Decision"));
            Assert.That(markdown, Does.Contain("## XML Policy"));
            Assert.That(markdown, Does.Contain("## Route Context"));
            Assert.That(markdown, Does.Contain("## Orchestration"));
            Assert.That(markdown, Does.Contain("## Session"));
            Assert.That(markdown, Does.Contain("## Diagnostics"));
            Assert.That(markdown, Does.Contain("## Expected Markers"));
            Assert.That(markdown, Does.Contain("## Offline Boundary"));
            Assert.That(markdown, Does.Contain("PASS"));
            Assert.That(markdown, Does.Contain("sidecar_authority=false"));
            Assert.That(markdown, Does.Contain("default_tests_live_runtime=false"));
            Assert.That(markdown, Does.Not.Contain("RequestsVisibleText=True"));
        });
    }

    [Test]
    public void JsonFormatterEmitsParseableStableTopLevelShape()
    {
        DataAgentReplayResult result = DataAgentReplayRunner.Run(DataAgentReplayFixture.Load(DefaultFixturePath()));

        string json = DataAgentReplayReportFormatter.FormatJson(result);

        using JsonDocument document = JsonDocument.Parse(json);
        JsonElement root = document.RootElement;

        Assert.Multiple(() =>
        {
            Assert.That(root.GetProperty("passed").GetBoolean(), Is.True);
            Assert.That(root.TryGetProperty("fixture", out _), Is.True);
            Assert.That(root.TryGetProperty("route", out _), Is.True);
            Assert.That(root.TryGetProperty("xmlPolicy", out _), Is.True);
            Assert.That(root.TryGetProperty("routeContext", out _), Is.True);
            Assert.That(root.TryGetProperty("orchestration", out _), Is.True);
            Assert.That(root.TryGetProperty("session", out _), Is.True);
            Assert.That(root.TryGetProperty("diagnostics", out _), Is.True);
            Assert.That(root.TryGetProperty("expectedMarkers", out _), Is.True);
            Assert.That(root.TryGetProperty("offlineBoundary", out _), Is.True);
        });
    }

    [Test]
    public void RunnerReportsMissingExpectedMarkerWithoutThrowing()
    {
        DataAgentReplayFixture fixture = DataAgentReplayFixture.Load(DefaultFixturePath()) with
        {
            ExpectedMarkers = ["marker_that_is_not_in_the_replay_report"]
        };

        DataAgentReplayResult result = DataAgentReplayRunner.Run(fixture);

        Assert.Multiple(() =>
        {
            Assert.That(result.Passed, Is.False);
            Assert.That(result.ExpectedMarkers, Has.Count.EqualTo(1));
            Assert.That(result.ExpectedMarkers[0].Marker, Is.EqualTo("marker_that_is_not_in_the_replay_report"));
            Assert.That(result.ExpectedMarkers[0].Passed, Is.False);
        });
    }

    [Test]
    public void RunnerReportsEmptyExpectedMarkersWithoutPassing()
    {
        DataAgentReplayFixture fixture = DataAgentReplayFixture.Load(DefaultFixturePath()) with
        {
            ExpectedMarkers = []
        };

        DataAgentReplayResult result = DataAgentReplayRunner.Run(fixture);

        Assert.Multiple(() =>
        {
            Assert.That(result.Passed, Is.False);
            Assert.That(result.ExpectedMarkers, Is.Empty);
        });
    }

    [Test]
    public void FixtureLoadReportsMissingPlannerWithTargetedError()
    {
        string fixturePath = Path.Combine(
            TestContext.CurrentContext.WorkDirectory,
            $"missing-planner-{Guid.NewGuid():N}.json");
        File.WriteAllText(
            fixturePath,
            """
            {
              "version": "v3.9",
              "name": "missing-planner",
              "callerId": "owner",
              "utterance": "DataAgent analyze project readiness",
              "routeState": {
                "isOwner": true,
                "isPrivate": true,
                "trustedRuntime": true,
                "activeDataAgentSessionId": "",
                "activeDataAgentSessionStatus": ""
              },
              "expectedMarkers": []
            }
            """);

        try
        {
            InvalidOperationException exception = Assert.Throws<InvalidOperationException>(
                () => DataAgentReplayFixture.Load(fixturePath))!;
            Assert.That(exception.Message, Does.Contain("planner"));
        }
        finally
        {
            File.Delete(fixturePath);
        }
    }

    [Test]
    public void FixtureLoadReportsBlankCallerIdWithTargetedError()
    {
        string fixturePath = Path.Combine(
            TestContext.CurrentContext.WorkDirectory,
            $"blank-caller-{Guid.NewGuid():N}.json");
        File.WriteAllText(
            fixturePath,
            """
            {
              "version": "v3.9",
              "name": "blank-caller",
              "callerId": "   ",
              "utterance": "DataAgent analyze project readiness",
              "routeState": {
                "isOwner": true,
                "isPrivate": true,
                "trustedRuntime": true,
                "activeDataAgentSessionId": "",
                "activeDataAgentSessionStatus": ""
              },
              "planner": {
                "dataset": "document_index",
                "intent": "find_dataagent_documents",
                "select": ["path", "title"],
                "filters": [],
                "limit": 20
              },
              "expectedMarkers": ["route_allowed"]
            }
            """);

        try
        {
            InvalidOperationException exception = Assert.Throws<InvalidOperationException>(
                () => DataAgentReplayFixture.Load(fixturePath))!;
            Assert.That(exception.Message, Does.Contain("callerId"));
        }
        finally
        {
            File.Delete(fixturePath);
        }
    }

    [Test]
    public void FixtureLoadReportsBlankPlannerDatasetWithTargetedError()
    {
        string fixturePath = Path.Combine(
            TestContext.CurrentContext.WorkDirectory,
            $"blank-planner-dataset-{Guid.NewGuid():N}.json");
        File.WriteAllText(
            fixturePath,
            """
            {
              "version": "v3.9",
              "name": "blank-planner-dataset",
              "callerId": "owner",
              "utterance": "DataAgent analyze project readiness",
              "routeState": {
                "isOwner": true,
                "isPrivate": true,
                "trustedRuntime": true,
                "activeDataAgentSessionId": "",
                "activeDataAgentSessionStatus": ""
              },
              "planner": {
                "dataset": "   ",
                "intent": "find_dataagent_documents",
                "select": ["path", "title"],
                "filters": [],
                "limit": 20
              },
              "expectedMarkers": ["route_allowed"]
            }
            """);

        try
        {
            InvalidOperationException exception = Assert.Throws<InvalidOperationException>(
                () => DataAgentReplayFixture.Load(fixturePath))!;
            Assert.That(exception.Message, Does.Contain("planner.dataset"));
        }
        finally
        {
            File.Delete(fixturePath);
        }
    }

    [Test]
    public void ReplayScriptDefaultRunEmitsMarkdown()
    {
        ProcessResult result = RunScript();

        Assert.Multiple(() =>
        {
            Assert.That(result.ExitCode, Is.EqualTo(0), result.CombinedOutput);
            Assert.That(result.StandardOutput, Does.Contain("# DataAgent Replay: owner-readiness-analysis"));
            Assert.That(result.StandardOutput, Does.Contain("## Expected Markers"));
            Assert.That(result.StandardOutput, Does.Contain("sidecar_authority=false"));
        });
    }

    [Test]
    public void ReplayScriptJsonRunEmitsParseableJson()
    {
        ProcessResult result = RunScript("-Format", "json");

        Assert.That(result.ExitCode, Is.EqualTo(0), result.CombinedOutput);
        using JsonDocument document = JsonDocument.Parse(result.StandardOutput);
        Assert.That(document.RootElement.GetProperty("passed").GetBoolean(), Is.True);
    }

    [Test]
    public void ReplayScriptMissingFixtureFailsWithClearError()
    {
        string missingPath = Path.Combine(FindRepoRoot(TestContext.CurrentContext.TestDirectory), "missing-dataagent-replay-fixture.json");

        ProcessResult result = RunScript("-Fixture", missingPath);

        Assert.Multiple(() =>
        {
            Assert.That(result.ExitCode, Is.Not.EqualTo(0));
            Assert.That(result.StandardError + result.StandardOutput, Does.Contain("Fixture not found"));
            Assert.That(result.StandardError + result.StandardOutput, Does.Contain(missingPath));
        });
    }

    [Test]
    public void ReplayScriptUnsupportedFormatFailsWithClearError()
    {
        ProcessResult result = RunScript("-Format", "xml");

        Assert.Multiple(() =>
        {
            Assert.That(result.ExitCode, Is.Not.EqualTo(0));
            Assert.That(result.StandardError + result.StandardOutput, Does.Contain("Unsupported format"));
            Assert.That(result.StandardError + result.StandardOutput, Does.Contain("markdown"));
            Assert.That(result.StandardError + result.StandardOutput, Does.Contain("json"));
        });
    }

    [Test]
    public void ReplayScriptFallbackValidatesDotnetNineSdk()
    {
        string repoRoot = FindRepoRoot(TestContext.CurrentContext.TestDirectory);
        string script = File.ReadAllText(Path.Combine(repoRoot, "tools", "replay-dataagent-chain.ps1"));

        Assert.Multiple(() =>
        {
            Assert.That(script, Does.Contain("dotnet --version"));
            Assert.That(script, Does.Contain("StartsWith(\"9.\""));
            Assert.That(script, Does.Contain(".NET 9 SDK required for DataAgent replay; found:"));
        });
    }

    [Test]
    public void ReplayScriptDisablesImplicitDotnetRestore()
    {
        string repoRoot = FindRepoRoot(TestContext.CurrentContext.TestDirectory);
        string script = File.ReadAllText(Path.Combine(repoRoot, "tools", "replay-dataagent-chain.ps1"));

        Assert.That(script, Does.Contain("run --no-restore --project"));
    }

    [Test]
    public void ReplayTestProcessRunnerDisablesMsBuildNodeReuse()
    {
        string repoRoot = FindRepoRoot(TestContext.CurrentContext.TestDirectory);
        string source = File.ReadAllText(Path.Combine(repoRoot, "Tests", "Alife.Test.DataAgent", "DataAgentReplayRunbookTests.cs"));
        string declaration = FindSourceBlock(source, "static async Task<ProcessResult> RunProcessAsync", "process.Start();");

        Assert.That(declaration, Does.Contain("StartInfo.Environment[\"MSBUILDDISABLENODEREUSE\"]"));
    }

    [Test]
    public void ReplayProjectUnsupportedFormatFailsWithClearError()
    {
        ProcessResult result = RunReplayProject("--fixture", DefaultFixturePath(), "--format", "xml");

        Assert.Multiple(() =>
        {
            Assert.That(result.ExitCode, Is.Not.EqualTo(0));
            Assert.That(result.StandardError + result.StandardOutput, Does.Contain("Unsupported format"));
            Assert.That(result.StandardError + result.StandardOutput, Does.Contain("markdown"));
            Assert.That(result.StandardError + result.StandardOutput, Does.Contain("json"));
        });
    }

    [Test]
    public void ReplayProjectMissingFixtureArgumentFailsWithClearError()
    {
        ProcessResult result = RunReplayProject("--format", "markdown");

        Assert.Multiple(() =>
        {
            Assert.That(result.ExitCode, Is.Not.EqualTo(0));
            Assert.That(result.StandardError + result.StandardOutput, Does.Contain("--fixture"));
        });
    }

    [Test]
    public void ReplayProjectUnexpectedFixtureErrorReportsExceptionType()
    {
        string fixturePath = Path.Combine(
            TestContext.CurrentContext.WorkDirectory,
            $"program-missing-planner-{Guid.NewGuid():N}.json");
        File.WriteAllText(
            fixturePath,
            """
            {
              "version": "v3.9",
              "name": "program-missing-planner",
              "callerId": "owner",
              "utterance": "DataAgent analyze project readiness",
              "routeState": {
                "isOwner": true,
                "isPrivate": true,
                "trustedRuntime": true,
                "activeDataAgentSessionId": "",
                "activeDataAgentSessionStatus": ""
              },
              "expectedMarkers": []
            }
            """);

        try
        {
            ProcessResult result = RunReplayProject("--fixture", fixturePath, "--format", "markdown");

            Assert.Multiple(() =>
            {
                Assert.That(result.ExitCode, Is.Not.EqualTo(0));
                Assert.That(result.StandardError + result.StandardOutput, Does.Contain(nameof(InvalidOperationException)));
                Assert.That(result.StandardError + result.StandardOutput, Does.Contain("planner"));
            });
        }
        finally
        {
            File.Delete(fixturePath);
        }
    }

    [Test]
    public void ReplayImplementationUsesRealRoutePolicyAccessorAndDisabledRuntime()
    {
        string repoRoot = FindRepoRoot(TestContext.CurrentContext.TestDirectory);
        string runner = File.ReadAllText(Path.Combine(repoRoot, "tools", "dataagent-replay", "DataAgentReplayRunner.cs"));

        Assert.Multiple(() =>
        {
            Assert.That(runner, Does.Contain("ToolCapabilityRouter.CreateDefault"));
            Assert.That(runner, Does.Contain("XmlFunctionExecutionPolicy"));
            Assert.That(runner, Does.Contain("XmlPolicyDataAgentToolRouteContextAccessor"));
            Assert.That(runner, Does.Contain("DataAgentAnalysisToolHandler"));
            Assert.That(runner, Does.Contain("QChatDiagnosticsService"));
            Assert.That(runner, Does.Contain("DataAgentGraphHandshakeOptions.Disabled"));
            Assert.That(runner, Does.Contain("DisabledDataAgentGraphSidecarClient.Instance"));
            Assert.That(runner, Does.Not.Contain("FixedRouteContextAccessor"));
        });
    }

    static string DefaultFixturePath()
    {
        return Path.Combine(
            FindRepoRoot(TestContext.CurrentContext.TestDirectory),
            "Tests",
            "Alife.Test.DataAgent",
            "Fixtures",
            "DataAgentReplay",
            "v3.9-owner-readiness-analysis.json");
    }

    static ProcessResult RunScript(params string[] arguments)
    {
        return RunScriptAsync(arguments).GetAwaiter().GetResult();
    }

    static ProcessResult RunReplayProject(params string[] arguments)
    {
        return RunReplayProjectAsync(arguments).GetAwaiter().GetResult();
    }

    static async Task<ProcessResult> RunReplayProjectAsync(params string[] arguments)
    {
        string repoRoot = FindRepoRoot(TestContext.CurrentContext.TestDirectory);
        string projectPath = Path.Combine(repoRoot, "tools", "dataagent-replay", "Alife.Tools.DataAgentReplay.csproj");
        string dotnetPath = @"C:\Users\hu shu\.dotnet\dotnet.exe";

        List<string> command = [
            "run",
            "--no-restore",
            "--project",
            projectPath,
            "--"
        ];
        command.AddRange(arguments);

        return await RunProcessAsync(dotnetPath, repoRoot, command, "Timed out after 60 seconds while running replay project.");
    }

    static async Task<ProcessResult> RunScriptAsync(params string[] arguments)
    {
        string repoRoot = FindRepoRoot(TestContext.CurrentContext.TestDirectory);
        string scriptPath = Path.Combine(repoRoot, "tools", "replay-dataagent-chain.ps1");
        if (File.Exists(scriptPath) == false)
            Assert.Ignore("DataAgent replay CLI wrapper is added in Task 3.");

        List<string> command = [
            "-ExecutionPolicy",
            "Bypass",
            "-File",
            scriptPath
        ];
        command.AddRange(arguments);

        return await RunProcessAsync("powershell", repoRoot, command, "Timed out after 60 seconds while running replay script.");
    }

    static async Task<ProcessResult> RunProcessAsync(
        string fileName,
        string workingDirectory,
        IReadOnlyList<string> arguments,
        string timeoutError)
    {
        using Process process = new();
        process.StartInfo = new ProcessStartInfo
        {
            FileName = fileName,
            WorkingDirectory = workingDirectory,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false
        };
        foreach (string argument in arguments)
            process.StartInfo.ArgumentList.Add(argument);
        process.StartInfo.Environment["MSBUILDDISABLENODEREUSE"] = "1";
        process.StartInfo.Environment["DOTNET_CLI_TELEMETRY_OPTOUT"] = "1";
        process.StartInfo.Environment["DOTNET_SKIP_FIRST_TIME_EXPERIENCE"] = "1";

        process.Start();
        Task<string> standardOutputTask = process.StandardOutput.ReadToEndAsync();
        Task<string> standardErrorTask = process.StandardError.ReadToEndAsync();
        Task waitForExitTask = process.WaitForExitAsync();

        Task completedTask = await Task.WhenAny(waitForExitTask, Task.Delay(TimeSpan.FromSeconds(60)));
        if (completedTask != waitForExitTask)
        {
            try
            {
                process.Kill(entireProcessTree: true);
            }
            catch
            {
            }

            string capturedOutput = standardOutputTask.IsCompletedSuccessfully
                ? standardOutputTask.Result
                : string.Empty;
            string capturedError = standardErrorTask.IsCompletedSuccessfully
                ? standardErrorTask.Result
                : string.Empty;
            string timeoutStandardError = string.IsNullOrEmpty(capturedError)
                ? timeoutError
                : capturedError + Environment.NewLine + timeoutError;

            return new ProcessResult(-1, capturedOutput, timeoutStandardError);
        }

        await waitForExitTask;
        string standardOutput = await standardOutputTask;
        string standardError = await standardErrorTask;
        return new ProcessResult(process.ExitCode, standardOutput, standardError);
    }

    static string FindRepoRoot(string startDirectory)
    {
        DirectoryInfo? directory = new(startDirectory);
        while (directory is not null)
        {
            if (File.Exists(Path.Combine(directory.FullName, "Alife.slnx")))
                return directory.FullName;

            directory = directory.Parent;
        }

        throw new InvalidOperationException($"Could not find repository root containing Alife.slnx from start directory: {startDirectory}");
    }

    static string FindSourceBlock(string source, string startMarker, string endMarker)
    {
        int start = source.LastIndexOf(startMarker, StringComparison.Ordinal);
        if (start < 0)
            return string.Empty;

        int end = source.IndexOf(endMarker, start, StringComparison.Ordinal);
        return end < 0
            ? source[start..]
            : source[start..end];
    }

    sealed record ProcessResult(int ExitCode, string StandardOutput, string StandardError)
    {
        public string CombinedOutput => StandardError + StandardOutput;
    }
}
