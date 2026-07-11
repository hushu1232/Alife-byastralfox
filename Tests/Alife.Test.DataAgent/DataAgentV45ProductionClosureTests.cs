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
}
