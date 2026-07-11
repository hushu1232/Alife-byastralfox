using Alife.Function.DataAgent;

namespace Alife.Test.DataAgent;

[TestFixture]
public sealed class DataAgentV45ProductionClosureTests
{
    static readonly DateTimeOffset BaseTime = new(2026, 7, 12, 8, 0, 0, TimeSpan.Zero);

    [Test]
    public void RecorderClassifiesEveryProductionOutcomeWithoutPayloadData()
    {
        DataAgentV45ProductionObservationRecorder recorder = new();
        (DataAgentGraphHandshakeOutcome Outcome, int Latency)[] observations =
        [
            (Outcome(DataAgentGraphHandshakeStatus.Accepted, "handshake_accepted", false, true), 100),
            (Outcome(DataAgentGraphHandshakeStatus.Rejected, "sql_authority_requested", true, true), 200),
            (Outcome(DataAgentGraphHandshakeStatus.Invalid, "invalid_request_schema", true, false), 0),
            (Outcome(DataAgentGraphHandshakeStatus.Timeout, "production_shadow_timeout", true, true), 300),
            (Outcome(DataAgentGraphHandshakeStatus.Unavailable, "production_shadow_unavailable", true, true), 400),
            (Outcome(DataAgentGraphHandshakeStatus.Unavailable, "production_shadow_busy", true, false), 0),
            (Outcome(DataAgentGraphHandshakeStatus.Unavailable, "production_shadow_circuit_open", true, false), 0)
        ];

        foreach ((DataAgentGraphHandshakeOutcome outcome, int latency) in observations)
            recorder.Record(outcome, TimeSpan.FromMilliseconds(latency), BaseTime);

        DataAgentV45ProductionObservationSnapshot snapshot = recorder.GetSnapshot(BaseTime);

        Assert.Multiple(() =>
        {
            Assert.That(snapshot.ObservationCount, Is.EqualTo(7));
            Assert.That(snapshot.AcceptedCount, Is.EqualTo(1));
            Assert.That(snapshot.RejectedCount, Is.EqualTo(1));
            Assert.That(snapshot.FallbackCount, Is.EqualTo(1));
            Assert.That(snapshot.TimeoutCount, Is.EqualTo(1));
            Assert.That(snapshot.UnavailableCount, Is.EqualTo(1));
            Assert.That(snapshot.BusyCount, Is.EqualTo(1));
            Assert.That(snapshot.CircuitOpenCount, Is.EqualTo(1));
            Assert.That(snapshot.NetworkAttemptCount, Is.EqualTo(4));
            Assert.That(snapshot.StoresSensitiveData, Is.False);
        });
    }

    [Test]
    public void RecorderEvictsOldestAndExpiredObservationsWithinFixedBounds()
    {
        DataAgentV45ProductionObservationRecorder recorder = new(
            new DataAgentV45ProductionObservationOptions(2, TimeSpan.FromMinutes(15), 60));

        recorder.Record(Outcome(DataAgentGraphHandshakeStatus.Accepted), TimeSpan.Zero, BaseTime);
        recorder.Record(Outcome(DataAgentGraphHandshakeStatus.Rejected), TimeSpan.Zero, BaseTime.AddMinutes(1));
        recorder.Record(Outcome(DataAgentGraphHandshakeStatus.Timeout), TimeSpan.Zero, BaseTime.AddMinutes(2));

        DataAgentV45ProductionObservationSnapshot bounded = recorder.GetSnapshot(BaseTime.AddMinutes(2));
        DataAgentV45ProductionObservationSnapshot expired = recorder.GetSnapshot(BaseTime.AddMinutes(18));

        Assert.Multiple(() =>
        {
            Assert.That(bounded.Capacity, Is.EqualTo(2));
            Assert.That(bounded.ObservationCount, Is.EqualTo(2));
            Assert.That(bounded.AcceptedCount, Is.Zero);
            Assert.That(bounded.RejectedCount, Is.EqualTo(1));
            Assert.That(bounded.TimeoutCount, Is.EqualTo(1));
            Assert.That(expired.ObservationCount, Is.Zero);
        });
    }

    [Test]
    public void RecorderComputesFallbackLatencyAndRetryStormAggregatesDeterministically()
    {
        DataAgentV45ProductionObservationRecorder recorder = new(
            new DataAgentV45ProductionObservationOptions(10, TimeSpan.FromMinutes(15), 3));
        int[] latencies = [100, 200, 300, 400, 500];
        for (int index = 0; index < latencies.Length; index++)
        {
            bool fallback = index == latencies.Length - 1;
            recorder.Record(
                Outcome(
                    fallback ? DataAgentGraphHandshakeStatus.Timeout : DataAgentGraphHandshakeStatus.Accepted,
                    fallback ? "production_shadow_timeout" : "handshake_accepted",
                    fallback,
                    true),
                TimeSpan.FromMilliseconds(latencies[index]),
                BaseTime.AddSeconds(index));
        }

        DataAgentV45ProductionObservationSnapshot snapshot = recorder.GetSnapshot(BaseTime.AddMinutes(1));

        Assert.Multiple(() =>
        {
            Assert.That(snapshot.FallbackRatioBasisPoints, Is.EqualTo(2000));
            Assert.That(snapshot.AverageLatencyMs, Is.EqualTo(300));
            Assert.That(snapshot.P95LatencyMs, Is.EqualTo(500));
            Assert.That(snapshot.MaxObservationsPerMinute, Is.EqualTo(5));
            Assert.That(snapshot.RetryStormDetected, Is.True);
        });
    }

    [Test]
    public void DefaultObservationWindowIsExplicitlyFifteenMinutes()
    {
        DataAgentV45ProductionObservationSnapshot snapshot =
            new DataAgentV45ProductionObservationRecorder().GetSnapshot(BaseTime);

        Assert.That(snapshot.WindowMinutes, Is.EqualTo(15));
    }

    [Test]
    public void RecorderRejectsNullOutcomeUnsafeReasonAndInvalidOptions()
    {
        Assert.Multiple(() =>
        {
            Assert.Throws<ArgumentOutOfRangeException>(() => new DataAgentV45ProductionObservationRecorder(
                new DataAgentV45ProductionObservationOptions(0, TimeSpan.FromMinutes(15), 60)));
            Assert.Throws<ArgumentNullException>(() => new DataAgentV45ProductionObservationRecorder().Record(
                null!, TimeSpan.Zero, BaseTime));
            Assert.Throws<ArgumentException>(() => new DataAgentV45ProductionObservationRecorder().Record(
                Outcome(DataAgentGraphHandshakeStatus.Unavailable, "Bearer secret\nSELECT * FROM x", true, true),
                TimeSpan.Zero,
                BaseTime));
        });
    }

    [Test]
    public async Task RealProductionBoundariesProduceExactSevenFaultDrills()
    {
        List<DataAgentV45FaultDrillObservation> drills = [];
        DataAgentV44ProductionShadowOptions ready = ReadyOptions();

        DataAgentV44ProductionShadowException unavailable = Assert.Throws<DataAgentV44ProductionShadowException>(
            () => new DataAgentV44ProductionShadowClient(
                new FakeClient(_ => throw new InvalidOperationException("offline")), ready)
                .TryHandshake(Request()))!;
        drills.Add(new(DataAgentV45FaultDrillKind.RuntimeUnavailable, true, unavailable.ReasonCode, unavailable.NetworkAttempted));

        DataAgentV44ProductionShadowException timeout = Assert.Throws<DataAgentV44ProductionShadowException>(
            () => new DataAgentV44ProductionShadowClient(
                new FakeClient(_ => throw new TimeoutException("slow")), ready)
                .TryHandshake(Request()))!;
        drills.Add(new(DataAgentV45FaultDrillKind.Timeout, true, timeout.ReasonCode, timeout.NetworkAttempted));

        DataAgentGraphHandshakeValidationResult invalid = DataAgentGraphHandshakeValidator.Validate(
            Request(), Response() with { RequestId = "wrong-request" });
        drills.Add(new(DataAgentV45FaultDrillKind.InvalidSchema, invalid.Accepted == false, invalid.ReasonCode, true));

        DataAgentGraphHandshakeValidationResult unsafeAuthority = DataAgentGraphHandshakeValidator.Validate(
            Request(), Response() with { NoSqlAuthority = false });
        drills.Add(new(DataAgentV45FaultDrillKind.UnsafeAuthority, unsafeAuthority.Accepted == false, unsafeAuthority.ReasonCode, true));

        using ManualResetEventSlim entered = new(false);
        using ManualResetEventSlim release = new(false);
        DataAgentV44ProductionShadowClient saturated = new(
            new FakeClient(_ =>
            {
                entered.Set();
                release.Wait(TimeSpan.FromSeconds(5));
                return Response();
            }),
            ready with { MaxConcurrency = 1 });
        Task<DataAgentGraphHandshakeResponse> first = Task.Run(() => saturated.TryHandshake(Request()));
        Assert.That(entered.Wait(TimeSpan.FromSeconds(5)), Is.True);
        DataAgentV44ProductionShadowException busy = Assert.Throws<DataAgentV44ProductionShadowException>(
            () => saturated.TryHandshake(Request()))!;
        release.Set();
        await first;
        drills.Add(new(DataAgentV45FaultDrillKind.ConcurrencySaturation, true, busy.ReasonCode, busy.NetworkAttempted));

        DateTimeOffset circuitNow = BaseTime;
        bool circuitFail = true;
        DataAgentV44ProductionShadowClient circuit = new(
            new FakeClient(_ => circuitFail ? throw new InvalidOperationException("offline") : Response()),
            ready with { FailureThreshold = 1, CircuitOpenDuration = TimeSpan.FromSeconds(10) },
            () => circuitNow);
        Assert.Throws<DataAgentV44ProductionShadowException>(() => circuit.TryHandshake(Request()));
        DataAgentV44ProductionShadowException circuitOpen = Assert.Throws<DataAgentV44ProductionShadowException>(
            () => circuit.TryHandshake(Request()))!;
        circuitNow = circuitNow.AddSeconds(11);
        circuitFail = false;
        bool circuitRecovered = circuit.TryHandshake(Request()).Accepted;
        drills.Add(new(DataAgentV45FaultDrillKind.CircuitOpenRecovery, circuitRecovered, circuitOpen.ReasonCode, circuitOpen.NetworkAttempted));

        DataAgentV44ProductionShadowOptions live = ready;
        int liveCalls = 0;
        DataAgentV44ProductionShadowClient killed = new(
            new FakeClient(_ => { liveCalls++; return Response(); }),
            ready,
            optionsProvider: () => live);
        killed.TryHandshake(Request());
        live = live with { KillSwitchActive = true };
        DataAgentV44ProductionShadowException kill = Assert.Throws<DataAgentV44ProductionShadowException>(
            () => killed.TryHandshake(Request()))!;
        drills.Add(new(DataAgentV45FaultDrillKind.LiveKillSwitch, liveCalls == 1, kill.ReasonCode, kill.NetworkAttempted));

        DataAgentV45ProductionFaultDrillResult result =
            DataAgentV45ProductionFaultDrillEvaluator.Evaluate(drills);

        Assert.Multiple(() =>
        {
            Assert.That(result.Accepted, Is.True);
            Assert.That(result.ReasonCode, Is.EqualTo("v4_5_fault_drills_passed"));
            Assert.That(result.Drills, Has.Count.EqualTo(7));
        });
    }

    [Test]
    public void FaultDrillEvaluatorRejectsMissingDuplicateUnsafeAndNetworkedKillEvidence()
    {
        DataAgentV45FaultDrillObservation[] valid = ValidDrills();

        Assert.Multiple(() =>
        {
            Assert.That(DataAgentV45ProductionFaultDrillEvaluator.Evaluate(valid[..6]).Accepted, Is.False);
            Assert.That(DataAgentV45ProductionFaultDrillEvaluator.Evaluate([.. valid, valid[0]]).Accepted, Is.False);
            Assert.That(DataAgentV45ProductionFaultDrillEvaluator.Evaluate(
                valid.Select(item => item.Kind == DataAgentV45FaultDrillKind.Timeout
                    ? item with { ReasonCode = "Bearer secret" }
                    : item)).Accepted, Is.False);
            Assert.That(DataAgentV45ProductionFaultDrillEvaluator.Evaluate(
                valid.Select(item => item.Kind == DataAgentV45FaultDrillKind.LiveKillSwitch
                    ? item with { NetworkAttempted = true }
                    : item)).Accepted, Is.False);
        });
    }

    [Test]
    public void ProductionClosureAcceptsOnlyCompleteBoundedUsefulSafeEvidence()
    {
        DataAgentV45ProductionClosureResult result = DataAgentV45ProductionClosureEvaluator.Evaluate(
            new DataAgentV45ProductionClosureInput(ProvenValue(), HealthySnapshot(), PassedDrills(), RuntimeRestartCount: 1));

        Assert.Multiple(() =>
        {
            Assert.That(result.Accepted, Is.True);
            Assert.That(result.ReasonCode, Is.EqualTo("v4_5_production_closure_accepted"));
            Assert.That(result.ContractVersion, Is.EqualTo("v4.5"));
            Assert.That(result.SourceBaseline, Is.EqualTo("v4.4"));
            Assert.That(result.AgentAdvisoryOnly, Is.True);
            Assert.That(result.CSharpValidationAuthority, Is.True);
            Assert.That(result.AllowsExecution, Is.False);
            Assert.That(result.AllowsStateWrite, Is.False);
            Assert.That(result.AllowsVisibleText, Is.False);
            Assert.That(result.DefaultResultChanged, Is.False);
            Assert.That(result.StoresSecrets, Is.False);
            Assert.That(result.StoresSql, Is.False);
            Assert.That(result.StoresHiddenContext, Is.False);
        });
    }

    [TestCase("value", "v4_5_value_gate_failed")]
    [TestCase("observations", "v4_5_observation_window_incomplete")]
    [TestCase("capacity", "v4_5_observation_window_incomplete")]
    [TestCase("fallback", "v4_5_fallback_ratio_exceeded")]
    [TestCase("latency", "v4_5_latency_budget_exceeded")]
    [TestCase("retry", "v4_5_retry_storm_detected")]
    [TestCase("restart", "v4_5_restart_budget_exceeded")]
    [TestCase("drill", "v4_5_fault_drill_failed")]
    [TestCase("drill_forged", "v4_5_fault_drill_failed")]
    [TestCase("aggregate", "v4_5_observation_window_incomplete")]
    public void ProductionClosureFailsClosedForEveryHardGate(string scenario, string expectedReason)
    {
        DataAgentV43CrossModuleValueResult value = ProvenValue();
        DataAgentV45ProductionObservationSnapshot snapshot = HealthySnapshot();
        DataAgentV45ProductionFaultDrillResult drills = PassedDrills();
        int restarts = 1;

        switch (scenario)
        {
            case "value": value = value with { TotalScore = 79, ProductionShadowEligible = false }; break;
            case "observations": snapshot = snapshot with { ObservationCount = 19 }; break;
            case "capacity": snapshot = snapshot with { Capacity = 128 }; break;
            case "fallback": snapshot = snapshot with { FallbackRatioBasisPoints = 2501 }; break;
            case "latency": snapshot = snapshot with { P95LatencyMs = 2001 }; break;
            case "retry": snapshot = snapshot with { RetryStormDetected = true }; break;
            case "restart": restarts = 2; break;
            case "drill": drills = drills with { Accepted = false, ReasonCode = "v4_5_fault_drill_failed" }; break;
            case "drill_forged":
                drills = drills with
                {
                    Drills = drills.Drills.Select(item => item.Kind == DataAgentV45FaultDrillKind.LiveKillSwitch
                        ? item with { NetworkAttempted = true }
                        : item).ToArray()
                };
                break;
            case "aggregate": snapshot = snapshot with { AcceptedCount = 20 }; break;
        }

        DataAgentV45ProductionClosureResult result = DataAgentV45ProductionClosureEvaluator.Evaluate(
            new DataAgentV45ProductionClosureInput(value, snapshot, drills, restarts));

        Assert.That(result.Accepted, Is.False);
        Assert.That(result.ReasonCode, Is.EqualTo(expectedReason));
    }

    static DataAgentGraphHandshakeOutcome Outcome(
        DataAgentGraphHandshakeStatus status,
        string reasonCode = "handshake_accepted",
        bool fallback = false,
        bool networkAttempted = false)
    {
        return new DataAgentGraphHandshakeOutcome(
            status,
            reasonCode,
            fallback,
            Request: null,
            Response: null,
            new DataAgentGraphHandshakeValidationResult(status == DataAgentGraphHandshakeStatus.Accepted, reasonCode),
            new DataAgentGraphSidecarObservabilitySnapshot(
                reasonCode,
                DataAgentGraphSidecarObservabilityStatus.Fallback,
                SidecarEnabled: true,
                EndpointConfigured: true,
                RuntimeStartedByAlife: false,
                networkAttempted,
                Accepted: status == DataAgentGraphHandshakeStatus.Accepted,
                FallbackUsed: fallback,
                SafeSummary: reasonCode));
    }

    static DataAgentV44ProductionShadowOptions ReadyOptions() =>
        DataAgentV44ProductionShadowOptions.FromValues("true", "false", "80", "proven_useful", "2", "3", "30000");

    static DataAgentGraphHandshakeRequest Request() => new(
        "request-1", "session-1", "turn-1", "owner", "review",
        "scenario_context=deterministic_csharp", "route_present=true", "status=Active",
        DataAgentGraphHandshakeManifestFactory.CreateDefault(), true, true, true,
        DataAgentGraphHandshakeLimits.MaxTraceSummaryChars,
        DataAgentGraphHandshakeLimits.MaxProgressEvents);

    static DataAgentGraphHandshakeResponse Response() => new(
        "request-1", true, "handshake_accepted", [DataAgentWorkflowNodeNames.QueryPlanner], [],
        "QueryPlanner:Completed", "graph_handshake=accepted", false, true, true,
        [DataAgentGraphHandshakeToolNames.ProposeQueryPlan], false, false);

    static DataAgentV45FaultDrillObservation[] ValidDrills() =>
    [
        new(DataAgentV45FaultDrillKind.RuntimeUnavailable, true, "production_shadow_unavailable", true),
        new(DataAgentV45FaultDrillKind.Timeout, true, "production_shadow_timeout", true),
        new(DataAgentV45FaultDrillKind.InvalidSchema, true, "request_id_mismatch", true),
        new(DataAgentV45FaultDrillKind.UnsafeAuthority, true, "sql_authority_requested", true),
        new(DataAgentV45FaultDrillKind.ConcurrencySaturation, true, "production_shadow_busy", false),
        new(DataAgentV45FaultDrillKind.CircuitOpenRecovery, true, "production_shadow_circuit_open", false),
        new(DataAgentV45FaultDrillKind.LiveKillSwitch, true, "production_shadow_kill_switch_active", false)
    ];

    static DataAgentV45ProductionFaultDrillResult PassedDrills() =>
        DataAgentV45ProductionFaultDrillEvaluator.Evaluate(ValidDrills());

    static DataAgentV45ProductionObservationSnapshot HealthySnapshot() => new(
        Capacity: 256,
        WindowMinutes: 15,
        ObservationCount: 20,
        AcceptedCount: 18,
        RejectedCount: 1,
        FallbackCount: 0,
        TimeoutCount: 1,
        UnavailableCount: 0,
        BusyCount: 0,
        CircuitOpenCount: 0,
        NetworkAttemptCount: 20,
        AverageLatencyMs: 400,
        P95LatencyMs: 900,
        FallbackRatioBasisPoints: 500,
        MaxObservationsPerMinute: 4,
        RetryStormDetected: false,
        StoresSensitiveData: false);

    static DataAgentV43CrossModuleValueResult ProvenValue() => new(
        Accepted: true,
        ReasonCode: "v4_3_cross_module_value_scored",
        ContractVersion: "v4.3",
        SourceBaseline: "v4.2",
        Status: DataAgentV43ValueStatus.ProvenUseful,
        OperatorDisposition: DataAgentV43OperatorDisposition.Adopted,
        CapabilityNames: ["qchat.intent_hint"],
        PacketScore: 25,
        ReplayAlignmentScore: 25,
        ManifestScore: 20,
        OperatorScore: 20,
        ReviewTimeScore: 10,
        TotalScore: 100,
        ProductionShadowEligible: true,
        AgentAdvisoryOnly: true,
        CSharpValidationAuthority: true,
        AllowsExecution: false,
        AllowsStateWrite: false,
        AllowsVisibleText: false,
        DefaultResultChanged: false,
        StoresSecrets: false,
        StoresSql: false,
        StoresHiddenContext: false,
        ReasonCodes: ["v4_3_cross_module_value_scored", "v4_3_value_proven_useful"]);

    sealed class FakeClient(Func<DataAgentGraphHandshakeRequest, DataAgentGraphHandshakeResponse> handler)
        : IDataAgentGraphSidecarClient
    {
        public DataAgentGraphHandshakeResponse TryHandshake(DataAgentGraphHandshakeRequest request) => handler(request);
    }
}
