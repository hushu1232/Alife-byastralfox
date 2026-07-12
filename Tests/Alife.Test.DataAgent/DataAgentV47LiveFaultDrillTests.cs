using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using Alife.Function.DataAgent;
using Alife.Tools.DataAgentV47Canary;

namespace Alife.Test.DataAgent;

public sealed class DataAgentV47LiveFaultDrillTests
{
    [Test]
    public async Task RunnerExecutesExactSevenLiveBoundaries()
    {
        DataAgentV45ProductionFaultDrillResult result =
            await new DataAgentV47LiveFaultDrillRunner().RunAsync(TimeSpan.FromMilliseconds(150));

        Dictionary<DataAgentV45FaultDrillKind, DataAgentV45FaultDrillObservation> drills =
            result.Drills.ToDictionary(drill => drill.Kind);
        Assert.Multiple(() =>
        {
            Assert.That(result.Accepted, Is.True);
            Assert.That(drills, Has.Count.EqualTo(7));
            Assert.That(drills[DataAgentV45FaultDrillKind.RuntimeUnavailable],
                Is.EqualTo(new DataAgentV45FaultDrillObservation(
                    DataAgentV45FaultDrillKind.RuntimeUnavailable, true,
                    "production_shadow_unavailable", true)));
            Assert.That(drills[DataAgentV45FaultDrillKind.Timeout].ReasonCode,
                Is.EqualTo("production_shadow_timeout"));
            Assert.That(drills[DataAgentV45FaultDrillKind.Timeout].NetworkAttempted, Is.True);
            Assert.That(drills[DataAgentV45FaultDrillKind.InvalidSchema].ReasonCode,
                Is.EqualTo("production_shadow_invalid_response"));
            Assert.That(drills[DataAgentV45FaultDrillKind.InvalidSchema].NetworkAttempted, Is.True);
            Assert.That(drills[DataAgentV45FaultDrillKind.UnsafeAuthority].ReasonCode,
                Is.EqualTo("sql_authority_requested"));
            Assert.That(drills[DataAgentV45FaultDrillKind.UnsafeAuthority].NetworkAttempted, Is.True);
            Assert.That(drills[DataAgentV45FaultDrillKind.ConcurrencySaturation].ReasonCode,
                Is.EqualTo("production_shadow_busy"));
            Assert.That(drills[DataAgentV45FaultDrillKind.ConcurrencySaturation].NetworkAttempted, Is.False);
            Assert.That(drills[DataAgentV45FaultDrillKind.CircuitOpenRecovery].ReasonCode,
                Is.EqualTo("production_shadow_circuit_open"));
            Assert.That(drills[DataAgentV45FaultDrillKind.CircuitOpenRecovery].NetworkAttempted, Is.False);
            Assert.That(drills[DataAgentV45FaultDrillKind.LiveKillSwitch].ReasonCode,
                Is.EqualTo("production_shadow_kill_switch_active"));
            Assert.That(drills[DataAgentV45FaultDrillKind.LiveKillSwitch].NetworkAttempted, Is.False);
        });
    }

    [Test]
    public void ProductionInvalidResponseIsAnAllowedInvalidSchemaDrill()
    {
        DataAgentV45FaultDrillObservation[] drills =
        [
            new(DataAgentV45FaultDrillKind.RuntimeUnavailable, true, "production_shadow_unavailable", true),
            new(DataAgentV45FaultDrillKind.Timeout, true, "production_shadow_timeout", true),
            new(DataAgentV45FaultDrillKind.InvalidSchema, true, "production_shadow_invalid_response", true),
            new(DataAgentV45FaultDrillKind.UnsafeAuthority, true, "sql_authority_requested", true),
            new(DataAgentV45FaultDrillKind.ConcurrencySaturation, true, "production_shadow_busy", false),
            new(DataAgentV45FaultDrillKind.CircuitOpenRecovery, true, "production_shadow_circuit_open", false),
            new(DataAgentV45FaultDrillKind.LiveKillSwitch, true, "production_shadow_kill_switch_active", false)
        ];

        Assert.That(DataAgentV45ProductionFaultDrillEvaluator.Evaluate(drills).Accepted, Is.True);
    }

    [Test]
    public async Task LoopbackResponderStopsItsListenerOnDispose()
    {
        LoopbackFaultResponder responder = await LoopbackFaultResponder.StartAsync(
            LoopbackFaultBehavior.MalformedJson, TimeSpan.FromMilliseconds(100));
        Uri endpoint = responder.Endpoint;

        await responder.DisposeAsync();

        Assert.Multiple(() =>
        {
            Assert.That(endpoint.IsLoopback, Is.True);
            Assert.That(responder.IsStopped, Is.True);
        });
        using TcpClient probe = new();
        Exception? failure = Assert.CatchAsync<Exception>(async () =>
            await probe.ConnectAsync(endpoint.Host, endpoint.Port).WaitAsync(TimeSpan.FromSeconds(2)));
        Assert.That(failure, Is.TypeOf<SocketException>().Or.TypeOf<TimeoutException>());
    }

    [Test]
    public void RuntimeUnavailableDrillUsesStoppedOsAssignedLoopbackPortWithoutRetry()
    {
        using TcpListener unavailableListener = new(IPAddress.Loopback, 0);
        unavailableListener.Server.ExclusiveAddressUse = true;
        unavailableListener.Start();
        Uri unavailableEndpoint = new(
            $"http://127.0.0.1:{((IPEndPoint)unavailableListener.LocalEndpoint).Port}");
        unavailableListener.Stop();
        bool listenerStoppedBeforeRequest = unavailableListener.Server.IsBound == false;

        using SocketsHttpHandler httpHandler = new()
        {
            ConnectTimeout = TimeSpan.FromMilliseconds(100),
            UseProxy = false
        };
        using HttpClient http = new(httpHandler)
        {
            Timeout = TimeSpan.FromMilliseconds(500)
        };
        DataAgentGraphHandshakeHttpOptions httpOptions = new(
            new Uri(unavailableEndpoint, "/handshake"),
            TimeSpan.FromMilliseconds(500), true, false);
        DataAgentV44ProductionShadowOptions shadowOptions =
            DataAgentV44ProductionShadowOptions.FromValues(
                "true", "false", "100", "proven_useful", "2", "3", "30000");
        using DataAgentV44ProductionShadowClient shadow = new(
            new DataAgentGraphHandshakeHttpClient(http, httpOptions), shadowOptions);
        DataAgentGraphHandshakeCoordinator coordinator = new(new(true), shadow);

        Stopwatch stopwatch = Stopwatch.StartNew();
        DataAgentGraphHandshakeOutcome outcome = coordinator.TryHandshake(
            "v47-canary", "closed loopback fault drill",
            DataAgentV47CanaryRequestFactory.Create(1));
        stopwatch.Stop();

        string root = FindRepoRoot(TestContext.CurrentContext.TestDirectory);
        string runnerSource = File.ReadAllText(Path.Combine(
            root, "tools", "dataagent-v47-canary", "DataAgentV47LiveFaultDrillRunner.cs"));
        int stopIndex = runnerSource.IndexOf("unavailableListener.Stop()", StringComparison.Ordinal);
        int governedRequestIndex = runnerSource.IndexOf(
            "Run(CreateShadow(unavailableEndpoint", StringComparison.Ordinal);

        Assert.Multiple(() =>
        {
            Assert.That(listenerStoppedBeforeRequest, Is.True);
            Assert.That(outcome.Status, Is.EqualTo(DataAgentGraphHandshakeStatus.Unavailable));
            Assert.That(outcome.ReasonCode, Is.EqualTo("production_shadow_unavailable"));
            Assert.That(outcome.Observability!.NetworkAttempted, Is.True);
            Assert.That(shadow.GetSnapshot().ConsecutiveFailures, Is.EqualTo(1));
            Assert.That(stopwatch.Elapsed, Is.LessThan(TimeSpan.FromSeconds(2)));
            Assert.That(runnerSource, Does.Contain(
                "TcpListener unavailableListener = new(IPAddress.Loopback, 0)"));
            Assert.That(runnerSource, Does.Contain("SocketsHttpHandler unavailableHandler"));
            Assert.That(runnerSource, Does.Contain("ConnectTimeout"));
            Assert.That(runnerSource, Does.Contain("UseProxy = false"));
            Assert.That(stopIndex, Is.GreaterThanOrEqualTo(0));
            Assert.That(governedRequestIndex, Is.GreaterThan(stopIndex));
            Assert.That(runnerSource.Split(
                "LoopbackFaultBehavior.FailFirstThenAccept", StringSplitOptions.None).Length - 1,
                Is.EqualTo(1));
        });
    }

    static string FindRepoRoot(string start)
    {
        DirectoryInfo? current = new(start);
        while (current is not null)
        {
            if (File.Exists(Path.Combine(current.FullName, "Alife.slnx")))
                return current.FullName;
            current = current.Parent;
        }
        throw new DirectoryNotFoundException("repo_root_not_found");
    }
}
