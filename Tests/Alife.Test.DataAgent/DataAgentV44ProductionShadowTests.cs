using Alife.Function.DataAgent;

namespace Alife.Test.DataAgent;

[TestFixture]
public sealed class DataAgentV44ProductionShadowTests
{
    [Test]
    public void OptionsDefaultDisabledWithKillSwitchActive()
    {
        DataAgentV44ProductionShadowOptions options = DataAgentV44ProductionShadowOptions.FromValues(
            null, null, null, null, null, null, null);

        Assert.Multiple(() =>
        {
            Assert.That(options.Enabled, Is.False);
            Assert.That(options.KillSwitchActive, Is.True);
            Assert.That(options.ValueGatePassed, Is.False);
            Assert.That(options.Ready, Is.False);
            Assert.That(options.MaxConcurrency, Is.EqualTo(2));
            Assert.That(options.FailureThreshold, Is.EqualTo(3));
            Assert.That(options.CircuitOpenDuration, Is.EqualTo(TimeSpan.FromSeconds(30)));
        });
    }

    [Test]
    public void OptionsRequireExplicitEnableKillOffAndProvenValue()
    {
        DataAgentV44ProductionShadowOptions options = DataAgentV44ProductionShadowOptions.FromValues(
            "true", "false", "80", "proven_useful", "4", "5", "45000");

        Assert.Multiple(() =>
        {
            Assert.That(options.Enabled, Is.True);
            Assert.That(options.KillSwitchActive, Is.False);
            Assert.That(options.ValueGatePassed, Is.True);
            Assert.That(options.Ready, Is.True);
            Assert.That(options.MaxConcurrency, Is.EqualTo(4));
            Assert.That(options.FailureThreshold, Is.EqualTo(5));
            Assert.That(options.CircuitOpenDuration, Is.EqualTo(TimeSpan.FromSeconds(45)));
        });
    }

    [TestCase("79", "proven_useful")]
    [TestCase("100", "promising")]
    [TestCase("100", "unproven")]
    public void ValueGateFailsClosedWithoutBothThresholdAndStatus(string score, string status)
    {
        DataAgentV44ProductionShadowOptions options = DataAgentV44ProductionShadowOptions.FromValues(
            "true", "false", score, status, "2", "3", "30000");

        Assert.That(options.ValueGatePassed, Is.False);
        Assert.That(options.Ready, Is.False);
    }

    [Test]
    public void InvalidNumericValuesUseSafeDefaults()
    {
        DataAgentV44ProductionShadowOptions options = DataAgentV44ProductionShadowOptions.FromValues(
            "true", "false", "invalid", "proven_useful", "99", "0", "9999999");

        Assert.Multiple(() =>
        {
            Assert.That(options.ValueScore, Is.Zero);
            Assert.That(options.MaxConcurrency, Is.EqualTo(2));
            Assert.That(options.FailureThreshold, Is.EqualTo(3));
            Assert.That(options.CircuitOpenDuration, Is.EqualTo(TimeSpan.FromSeconds(30)));
            Assert.That(options.Ready, Is.False);
        });
    }

    [Test]
    public void ExistingHttpOptionsStillRejectNonLoopbackEndpoint()
    {
        DataAgentGraphHandshakeHttpOptions options =
            DataAgentGraphHandshakeHttpOptions.FromValues("https://example.com/handshake", "800");

        Assert.That(options.Configured, Is.False);
        Assert.That(options.Endpoint, Is.Null);
    }

    [TestCase(false, false, 100, "proven_useful", "production_shadow_disabled")]
    [TestCase(true, true, 100, "proven_useful", "production_shadow_kill_switch_active")]
    [TestCase(true, false, 79, "proven_useful", "production_shadow_value_gate_failed")]
    public void ClientFailsClosedBeforeNetwork(
        bool enabled,
        bool killSwitch,
        int score,
        string status,
        string expectedReason)
    {
        int calls = 0;
        DataAgentV44ProductionShadowClient client = new(
            new FakeClient(_ => { calls++; return Response(); }),
            ReadyOptions() with { Enabled = enabled, KillSwitchActive = killSwitch, ValueScore = score, ValueStatus = status });

        DataAgentV44ProductionShadowException error = Assert.Throws<DataAgentV44ProductionShadowException>(
            () => client.TryHandshake(Request()))!;

        Assert.That(error.ReasonCode, Is.EqualTo(expectedReason));
        Assert.That(error.NetworkAttempted, Is.False);
        Assert.That(calls, Is.Zero);
    }

    [Test]
    public void SuccessfulCallResetsFailureStateAndReleasesLease()
    {
        DataAgentV44ProductionShadowClient client = new(
            new FakeClient(_ => Response()),
            ReadyOptions());

        DataAgentGraphHandshakeResponse response = client.TryHandshake(Request());
        DataAgentV44ProductionShadowSnapshot snapshot = client.GetSnapshot();

        Assert.That(response.Accepted, Is.True);
        Assert.That(snapshot.ActiveCalls, Is.Zero);
        Assert.That(snapshot.ConsecutiveFailures, Is.Zero);
        Assert.That(snapshot.CircuitOpen, Is.False);
    }

    [Test]
    public void ConsecutiveFailuresOpenCircuitUntilDeadlineThenRecover()
    {
        DateTimeOffset now = new(2026, 7, 12, 0, 0, 0, TimeSpan.Zero);
        bool fail = true;
        int calls = 0;
        DataAgentV44ProductionShadowClient client = new(
            new FakeClient(_ =>
            {
                calls++;
                if (fail) throw new InvalidOperationException("offline");
                return Response();
            }),
            ReadyOptions() with { FailureThreshold = 2, CircuitOpenDuration = TimeSpan.FromSeconds(10) },
            () => now);

        Assert.Throws<DataAgentV44ProductionShadowException>(() => client.TryHandshake(Request()));
        Assert.Throws<DataAgentV44ProductionShadowException>(() => client.TryHandshake(Request()));
        DataAgentV44ProductionShadowException open = Assert.Throws<DataAgentV44ProductionShadowException>(
            () => client.TryHandshake(Request()))!;
        Assert.That(open.ReasonCode, Is.EqualTo("production_shadow_circuit_open"));
        Assert.That(calls, Is.EqualTo(2));

        now = now.AddSeconds(11);
        fail = false;
        Assert.That(client.TryHandshake(Request()).Accepted, Is.True);
        Assert.That(client.GetSnapshot().CircuitOpen, Is.False);
        Assert.That(client.GetSnapshot().ConsecutiveFailures, Is.Zero);
    }

    [TestCase(true, "production_shadow_timeout")]
    [TestCase(false, "production_shadow_unavailable")]
    public void TransportFailuresUseStableSafeReasonCodes(bool timeout, string expectedReason)
    {
        const string sensitiveMessage = "https://127.0.0.1:8765 secret request response";
        Exception transportError = timeout
            ? new TimeoutException(sensitiveMessage)
            : new InvalidOperationException(sensitiveMessage);
        DataAgentV44ProductionShadowClient client = new(
            new FakeClient(_ => throw transportError),
            ReadyOptions());

        DataAgentV44ProductionShadowException error = Assert.Throws<DataAgentV44ProductionShadowException>(
            () => client.TryHandshake(Request()))!;

        Assert.Multiple(() =>
        {
            Assert.That(error.ReasonCode, Is.EqualTo(expectedReason));
            Assert.That(error.NetworkAttempted, Is.True);
            Assert.That(error.Message, Is.EqualTo(expectedReason));
            Assert.That(error.InnerException, Is.Null);
            Assert.That(error.ToString(), Does.Not.Contain(sensitiveMessage));
            Assert.That(client.GetSnapshot().ActiveCalls, Is.Zero);
            Assert.That(client.GetSnapshot().ConsecutiveFailures, Is.EqualTo(1));
        });
    }

    [Test]
    public async Task BusyCallDoesNotQueueAndAllLeasesAreReleased()
    {
        using ManualResetEventSlim entered = new(false);
        using ManualResetEventSlim release = new(false);
        DataAgentV44ProductionShadowClient client = new(
            new FakeClient(_ =>
            {
                entered.Set();
                release.Wait(TimeSpan.FromSeconds(5));
                return Response();
            }),
            ReadyOptions() with { MaxConcurrency = 1 });

        Task<DataAgentGraphHandshakeResponse> first = Task.Run(() => client.TryHandshake(Request()));
        Assert.That(entered.Wait(TimeSpan.FromSeconds(5)), Is.True);

        DataAgentV44ProductionShadowException busy = Assert.Throws<DataAgentV44ProductionShadowException>(
            () => client.TryHandshake(Request()))!;
        Assert.Multiple(() =>
        {
            Assert.That(busy.ReasonCode, Is.EqualTo("production_shadow_busy"));
            Assert.That(busy.NetworkAttempted, Is.False);
            Assert.That(client.GetSnapshot().ActiveCalls, Is.EqualTo(1));
        });

        release.Set();
        Assert.That((await first).Accepted, Is.True);
        Assert.That(client.GetSnapshot().ActiveCalls, Is.Zero);
    }

    static DataAgentV44ProductionShadowOptions ReadyOptions()
    {
        return DataAgentV44ProductionShadowOptions.FromValues(
            "true", "false", "80", "proven_useful", "2", "3", "30000");
    }

    static DataAgentGraphHandshakeRequest Request()
    {
        return new DataAgentGraphHandshakeRequest(
            "request-1",
            "session-1",
            "turn-1",
            "owner",
            "review deterministic result",
            "scenario_context=deterministic_csharp",
            "route_present=true",
            "status=Active;executed_sql=true;terminal=false",
            DataAgentGraphHandshakeManifestFactory.CreateDefault(),
            NoSqlAuthority: true,
            ReadOnly: true,
            FallbackAvailable: true,
            TraceBudgetChars: DataAgentGraphHandshakeLimits.MaxTraceSummaryChars,
            ProgressBudget: DataAgentGraphHandshakeLimits.MaxProgressEvents);
    }

    static DataAgentGraphHandshakeResponse Response()
    {
        return new DataAgentGraphHandshakeResponse(
            "request-1",
            Accepted: true,
            ReasonCode: "handshake_accepted",
            SelectedNodes: [DataAgentWorkflowNodeNames.QueryPlanner],
            NodeProgress: [],
            TraceSummary: "QueryPlanner:Completed",
            ContextContribution: "graph_handshake=accepted",
            FallbackRequired: false,
            NoSqlAuthority: true,
            ReadOnly: true,
            RequestedToolNames: [DataAgentGraphHandshakeToolNames.ProposeQueryPlan],
            RequestsCheckpointMutation: false,
            RequestsVisibleText: false);
    }

    sealed class FakeClient(Func<DataAgentGraphHandshakeRequest, DataAgentGraphHandshakeResponse> handler)
        : IDataAgentGraphSidecarClient
    {
        public DataAgentGraphHandshakeResponse TryHandshake(DataAgentGraphHandshakeRequest request)
        {
            return handler(request);
        }
    }
}
