using System.Text.RegularExpressions;

namespace Alife.Function.DataAgent;

public enum DataAgentV45ProductionObservationStatus
{
    Accepted,
    Rejected,
    Fallback,
    Timeout,
    Unavailable,
    Busy,
    CircuitOpen
}

public sealed record DataAgentV45ProductionObservationOptions(
    int Capacity,
    TimeSpan Window,
    int RetryStormThresholdPerMinute)
{
    public static DataAgentV45ProductionObservationOptions Default { get; } =
        new(256, TimeSpan.FromMinutes(15), 60);
}

public sealed record DataAgentV45ProductionObservationSnapshot(
    int Capacity,
    int WindowMinutes,
    int ObservationCount,
    int AcceptedCount,
    int RejectedCount,
    int FallbackCount,
    int TimeoutCount,
    int UnavailableCount,
    int BusyCount,
    int CircuitOpenCount,
    int NetworkAttemptCount,
    int AverageLatencyMs,
    int P95LatencyMs,
    int FallbackRatioBasisPoints,
    int MaxObservationsPerMinute,
    bool RetryStormDetected,
    bool StoresSensitiveData);

public interface IDataAgentV45ProductionObservationSink
{
    void Record(DataAgentGraphHandshakeOutcome outcome, TimeSpan elapsed, DateTimeOffset recordedAt);
}

public sealed class DataAgentV45ProductionObservationRecorder : IDataAgentV45ProductionObservationSink
{
    const int MaxCapacity = 4096;
    const int MaxLatencyMs = 300_000;
    const int MaxRetryStormThreshold = 10_000;

    static readonly Regex SafeReasonCodePattern = new(
        "^[a-z][a-z0-9_]{0,127}$",
        RegexOptions.CultureInvariant | RegexOptions.Compiled);

    readonly DataAgentV45ProductionObservationOptions options;
    readonly Queue<Observation> observations = new();
    readonly object sync = new();

    public DataAgentV45ProductionObservationRecorder(
        DataAgentV45ProductionObservationOptions? options = null)
    {
        this.options = options ?? DataAgentV45ProductionObservationOptions.Default;
        if (this.options.Capacity is < 1 or > MaxCapacity)
            throw new ArgumentOutOfRangeException(nameof(options));
        if (this.options.Window < TimeSpan.FromSeconds(1) || this.options.Window > TimeSpan.FromHours(24))
            throw new ArgumentOutOfRangeException(nameof(options));
        if (this.options.RetryStormThresholdPerMinute is < 1 or > MaxRetryStormThreshold)
            throw new ArgumentOutOfRangeException(nameof(options));
    }

    public void Record(
        DataAgentGraphHandshakeOutcome outcome,
        TimeSpan elapsed,
        DateTimeOffset recordedAt)
    {
        ArgumentNullException.ThrowIfNull(outcome);
        if (SafeReasonCode(outcome.ReasonCode) == false)
            throw new ArgumentException("unsafe_reason_code", nameof(outcome));

        bool networkAttempted = outcome.Observability?.NetworkAttempted == true;
        int elapsedMs = networkAttempted
            ? Math.Clamp((int)Math.Round(elapsed.TotalMilliseconds), 0, MaxLatencyMs)
            : 0;
        Observation observation = new(
            MapStatus(outcome),
            outcome.FallbackRequired,
            networkAttempted,
            elapsedMs,
            recordedAt);

        lock (sync)
        {
            EvictExpired(recordedAt);
            while (observations.Count >= options.Capacity)
                observations.Dequeue();
            observations.Enqueue(observation);
        }
    }

    public DataAgentV45ProductionObservationSnapshot GetSnapshot(DateTimeOffset now)
    {
        lock (sync)
        {
            EvictExpired(now);
            Observation[] values = observations.ToArray();
            int[] networkLatencies = values
                .Where(value => value.NetworkAttempted)
                .Select(value => value.ElapsedMs)
                .Order()
                .ToArray();
            int averageLatency = networkLatencies.Length == 0
                ? 0
                : (int)Math.Round(networkLatencies.Average());
            int p95Latency = networkLatencies.Length == 0
                ? 0
                : networkLatencies[Math.Max(0, (int)Math.Ceiling(networkLatencies.Length * 0.95) - 1)];
            int fallbackRatio = values.Length == 0
                ? 0
                : values.Count(value => value.FallbackRequired) * 10_000 / values.Length;
            int maxPerMinute = values.Length == 0
                ? 0
                : values.GroupBy(value => value.RecordedAt.ToUnixTimeSeconds() / 60)
                    .Max(group => group.Count());

            return new DataAgentV45ProductionObservationSnapshot(
                options.Capacity,
                (int)options.Window.TotalMinutes,
                values.Length,
                Count(values, DataAgentV45ProductionObservationStatus.Accepted),
                Count(values, DataAgentV45ProductionObservationStatus.Rejected),
                Count(values, DataAgentV45ProductionObservationStatus.Fallback),
                Count(values, DataAgentV45ProductionObservationStatus.Timeout),
                Count(values, DataAgentV45ProductionObservationStatus.Unavailable),
                Count(values, DataAgentV45ProductionObservationStatus.Busy),
                Count(values, DataAgentV45ProductionObservationStatus.CircuitOpen),
                networkLatencies.Length,
                averageLatency,
                p95Latency,
                fallbackRatio,
                maxPerMinute,
                maxPerMinute > options.RetryStormThresholdPerMinute,
                StoresSensitiveData: false);
        }
    }

    void EvictExpired(DateTimeOffset now)
    {
        DateTimeOffset cutoff = now.Subtract(options.Window);
        while (observations.TryPeek(out Observation? observation) && observation.RecordedAt < cutoff)
            observations.Dequeue();
    }

    static DataAgentV45ProductionObservationStatus MapStatus(DataAgentGraphHandshakeOutcome outcome)
    {
        if (string.Equals(outcome.ReasonCode, "production_shadow_busy", StringComparison.Ordinal))
            return DataAgentV45ProductionObservationStatus.Busy;
        if (string.Equals(outcome.ReasonCode, "production_shadow_circuit_open", StringComparison.Ordinal))
            return DataAgentV45ProductionObservationStatus.CircuitOpen;

        return outcome.Status switch
        {
            DataAgentGraphHandshakeStatus.Accepted => DataAgentV45ProductionObservationStatus.Accepted,
            DataAgentGraphHandshakeStatus.Rejected => DataAgentV45ProductionObservationStatus.Rejected,
            DataAgentGraphHandshakeStatus.Invalid when outcome.Observability?.NetworkAttempted == true =>
                DataAgentV45ProductionObservationStatus.Rejected,
            DataAgentGraphHandshakeStatus.Timeout => DataAgentV45ProductionObservationStatus.Timeout,
            DataAgentGraphHandshakeStatus.Unavailable => DataAgentV45ProductionObservationStatus.Unavailable,
            _ => DataAgentV45ProductionObservationStatus.Fallback
        };
    }

    static bool SafeReasonCode(string? reasonCode) =>
        string.IsNullOrWhiteSpace(reasonCode) == false &&
        SafeReasonCodePattern.IsMatch(reasonCode) &&
        DataAgentGraphHandshakeUnsafeDiagnosticDetector.ContainsUnsafeText(reasonCode) == false;

    static int Count(
        IEnumerable<Observation> values,
        DataAgentV45ProductionObservationStatus status) =>
        values.Count(value => value.Status == status);

    sealed record Observation(
        DataAgentV45ProductionObservationStatus Status,
        bool FallbackRequired,
        bool NetworkAttempted,
        int ElapsedMs,
        DateTimeOffset RecordedAt);
}
