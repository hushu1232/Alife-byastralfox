using Alife.Function.DataAgent;
using Alife.Function.FunctionCaller;
using Microsoft.Extensions.Logging.Abstractions;
using NUnit.Framework;

namespace Alife.Test.DataAgent;

[TestFixture]
public sealed class DataAgentProgressDiagnosticsPublisherTests
{
    [Test]
    public void PublishRecordsProgressAndEmitsFormattedDiagnostics()
    {
        DateTimeOffset now = Now();
        List<string> published = [];
        DataAgentProgressRecorder recorder = new();
        DataAgentProgressDiagnosticsPublisher publisher = new(
            recorder,
            text => published.Add(text),
            () => now);

        publisher.Publish(Event("session-a", DataAgentProgressEventKind.RouteGate, now));

        IReadOnlyList<DataAgentProgressEvent> recent = recorder.GetRecent("session-a", now);

        Assert.Multiple(() =>
        {
            Assert.That(recent, Has.Count.EqualTo(1));
            Assert.That(published, Has.Count.EqualTo(1));
            Assert.That(published[0], Does.Contain("DataAgent progress diagnostics"));
            Assert.That(published[0], Does.Contain("RouteGate"));
            Assert.That(published[0], Does.Contain("session=session-a"));
        });
    }

    [Test]
    public void XmlFunctionCallerStoresRecentProgressDiagnostics()
    {
        XmlFunctionCaller caller = new(NullLogger<XmlFunctionCaller>.Instance);

        caller.RecordRecentDataAgentProgressDiagnostics("DataAgent progress diagnostics\r\nstate=ok  ");

        Assert.That(
            caller.RecentDataAgentProgressDiagnostics,
            Is.EqualTo("DataAgent progress diagnostics\nstate=ok"));
    }

    [Test]
    public void XmlFunctionCallerStoresRecentGraphDiagnostics()
    {
        XmlFunctionCaller caller = new(new NullLogger<XmlFunctionCaller>());

        caller.RecordRecentDataAgentGraphDiagnostics("DataQueryGraph dry-run\r\nenabled=false  ");

        Assert.That(
            caller.RecentDataAgentGraphDiagnostics,
            Is.EqualTo("DataQueryGraph dry-run\nenabled=false"));
    }

    [Test]
    public void DataAgentModuleWiresProgressPublisherIntoRuntime()
    {
        string source = ReadSource(
            "Sources",
            "Alife.Function",
            "Alife.Function.DataAgent",
            "DataAgentModuleService.cs");

        Assert.Multiple(() =>
        {
            Assert.That(source, Does.Contain("DataAgentProgressRecorder progressRecorder = new();"));
            Assert.That(source, Does.Contain("new DataAgentProgressDiagnosticsPublisher("));
            Assert.That(source, Does.Contain("functionService.RecordRecentDataAgentProgressDiagnostics"));
            Assert.That(source, Does.Contain("progressSink: progressSink"));
        });
    }

    [Test]
    public void DataAgentProjectDoesNotReferenceQChat()
    {
        string project = ReadSource(
            "Sources",
            "Alife.Function",
            "Alife.Function.DataAgent",
            "Alife.Function.DataAgent.csproj");

        Assert.That(project, Does.Not.Contain("Alife.Function.QChat"));
    }

    static DataAgentProgressEvent Event(
        string sessionId,
        DataAgentProgressEventKind kind,
        DateTimeOffset at)
    {
        return new DataAgentProgressEvent(
            sessionId,
            kind,
            DataAgentProgressEventPhase.Completed,
            DataAgentProgressEventStatus.Succeeded,
            "ok",
            TurnCount: 1,
            at,
            ExecutedSql: false,
            QueryAllowed: true,
            Terminal: false,
            new Dictionary<string, string>());
    }

    static DateTimeOffset Now()
    {
        return new DateTimeOffset(2026, 7, 3, 10, 0, 0, TimeSpan.Zero);
    }

    static string ReadSource(params string[] parts)
    {
        string root = FindRepositoryRoot();
        return File.ReadAllText(Path.Combine([root, .. parts]));
    }

    static string FindRepositoryRoot()
    {
        DirectoryInfo? directory = new(TestContext.CurrentContext.TestDirectory);
        while (directory != null)
        {
            if (Directory.Exists(Path.Combine(directory.FullName, "Sources")) &&
                File.Exists(Path.Combine(directory.FullName, "Alife.slnx")))
            {
                return directory.FullName;
            }

            directory = directory.Parent;
        }

        throw new DirectoryNotFoundException("Repository root not found.");
    }
}
