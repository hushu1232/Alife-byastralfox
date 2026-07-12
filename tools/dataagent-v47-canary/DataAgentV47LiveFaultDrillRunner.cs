using Alife.Function.DataAgent;

namespace Alife.Tools.DataAgentV47Canary;

public sealed class DataAgentV47LiveFaultDrillRunner
{
    public async Task<DataAgentV45ProductionFaultDrillResult> RunAsync(TimeSpan timeout)
    {
        if (timeout < TimeSpan.FromMilliseconds(100) || timeout > TimeSpan.FromSeconds(10))
            throw new ArgumentOutOfRangeException(nameof(timeout));
        List<DataAgentV45FaultDrillObservation> drills = [];

        await using (LoopbackFaultResponder unavailable = await LoopbackFaultResponder.StartAsync(
            LoopbackFaultBehavior.FailFirstThenAccept, TimeSpan.Zero))
        {
            drills.Add(Observe(DataAgentV45FaultDrillKind.RuntimeUnavailable,
                Run(CreateShadow(unavailable.Endpoint, timeout), 1),
                "production_shadow_unavailable", true));
        }

        await using (LoopbackFaultResponder delayed = await LoopbackFaultResponder.StartAsync(
            LoopbackFaultBehavior.Delayed, timeout + TimeSpan.FromMilliseconds(100)))
        {
            drills.Add(Observe(DataAgentV45FaultDrillKind.Timeout,
                Run(CreateShadow(delayed.Endpoint, timeout), 2), "production_shadow_timeout", true));
        }
        await using (LoopbackFaultResponder malformed = await LoopbackFaultResponder.StartAsync(
            LoopbackFaultBehavior.MalformedJson, TimeSpan.Zero))
        {
            drills.Add(Observe(DataAgentV45FaultDrillKind.InvalidSchema,
                Run(CreateShadow(malformed.Endpoint, timeout), 3), "production_shadow_invalid_response", true));
        }
        await using (LoopbackFaultResponder unsafeResponse = await LoopbackFaultResponder.StartAsync(
            LoopbackFaultBehavior.UnsafeAuthority, TimeSpan.Zero))
        {
            drills.Add(Observe(DataAgentV45FaultDrillKind.UnsafeAuthority,
                Run(CreateShadow(unsafeResponse.Endpoint, timeout), 4), "sql_authority_requested", true));
        }

        await using (LoopbackFaultResponder blocked = await LoopbackFaultResponder.StartAsync(
            LoopbackFaultBehavior.Delayed, TimeSpan.FromMilliseconds(250)))
        {
            using DataAgentV44ProductionShadowClient shadow = CreateShadow(
                blocked.Endpoint, TimeSpan.FromSeconds(2), ReadyOptions() with { MaxConcurrency = 1 });
            Task<DataAgentGraphHandshakeOutcome> first = Task.Run(() => Run(shadow, 5));
            await blocked.WaitForRequestAsync(TimeSpan.FromSeconds(2));
            drills.Add(Observe(DataAgentV45FaultDrillKind.ConcurrencySaturation,
                Run(shadow, 6), "production_shadow_busy", false));
            await first;
        }

        await using (LoopbackFaultResponder recovering = await LoopbackFaultResponder.StartAsync(
            LoopbackFaultBehavior.FailFirstThenAccept, TimeSpan.Zero))
        {
            DateTimeOffset now = DateTimeOffset.UtcNow;
            using DataAgentV44ProductionShadowClient shadow = CreateShadow(
                recovering.Endpoint, timeout,
                ReadyOptions() with { FailureThreshold = 1, CircuitOpenDuration = TimeSpan.FromSeconds(1) },
                () => now);
            Run(shadow, 7);
            DataAgentGraphHandshakeOutcome open = Run(shadow, 8);
            now = now.AddSeconds(2);
            bool recovered = Run(shadow, 9).Status == DataAgentGraphHandshakeStatus.Accepted;
            drills.Add(new(DataAgentV45FaultDrillKind.CircuitOpenRecovery, recovered,
                open.ReasonCode, open.Observability?.NetworkAttempted == true));
        }

        await using (LoopbackFaultResponder valid = await LoopbackFaultResponder.StartAsync(
            LoopbackFaultBehavior.Valid, TimeSpan.Zero))
        {
            DataAgentV44ProductionShadowOptions live = ReadyOptions();
            using DataAgentV44ProductionShadowClient shadow = CreateShadow(
                valid.Endpoint, timeout, live, optionsProvider: () => live);
            Run(shadow, 10);
            int beforeKill = valid.RequestCount;
            live = live with { KillSwitchActive = true };
            DataAgentGraphHandshakeOutcome killed = Run(shadow, 11);
            drills.Add(new(DataAgentV45FaultDrillKind.LiveKillSwitch,
                valid.RequestCount == beforeKill, killed.ReasonCode,
                killed.Observability?.NetworkAttempted == true));
        }

        return DataAgentV45ProductionFaultDrillEvaluator.Evaluate(drills);
    }

    static DataAgentV45FaultDrillObservation Observe(
        DataAgentV45FaultDrillKind kind, DataAgentGraphHandshakeOutcome outcome,
        string reasonCode, bool network) =>
        new(kind, outcome.ReasonCode == reasonCode && outcome.Observability?.NetworkAttempted == network,
            outcome.ReasonCode, outcome.Observability?.NetworkAttempted == true);

    static DataAgentGraphHandshakeOutcome Run(DataAgentV44ProductionShadowClient shadow, int sequence) =>
        new DataAgentGraphHandshakeCoordinator(new(true), shadow).TryHandshake(
            "v47-canary", "bounded fault drill", DataAgentV47CanaryRequestFactory.Create(sequence));

    static DataAgentV44ProductionShadowClient CreateShadow(
        Uri endpoint, TimeSpan timeout, DataAgentV44ProductionShadowOptions? options = null,
        Func<DateTimeOffset>? clock = null,
        Func<DataAgentV44ProductionShadowOptions>? optionsProvider = null)
    {
        HttpClient http = new() { Timeout = timeout };
        DataAgentGraphHandshakeHttpOptions httpOptions = new(
            new Uri(endpoint, "/handshake"), timeout, true, false);
        return new(new DataAgentGraphHandshakeHttpClient(http, httpOptions),
            options ?? ReadyOptions(), clock, optionsProvider);
    }

    static DataAgentV44ProductionShadowOptions ReadyOptions() =>
        DataAgentV44ProductionShadowOptions.FromValues(
            "true", "false", "100", "proven_useful", "2", "3", "30000");

}
