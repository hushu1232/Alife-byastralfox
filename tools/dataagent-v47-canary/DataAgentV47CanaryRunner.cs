using System.Text.Json;
using Alife.Function.DataAgent;

namespace Alife.Tools.DataAgentV47Canary;

public sealed record DataAgentV47CanaryRunResult(
    bool Accepted,
    string ReasonCode,
    int AcceptedCount,
    int NetworkAttemptCount,
    DataAgentV45ProductionObservationSnapshot? ObservationSnapshot,
    DataAgentV47RuntimeIdentityEvidence? RuntimeIdentity);

public sealed class DataAgentV47CanaryRunner
{
    public async Task<DataAgentV47CanaryRunResult> RunAsync(
        DataAgentV47CanaryArguments arguments,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(arguments);
        using HttpClient http = new() { Timeout = TimeSpan.FromMilliseconds(arguments.TimeoutMs) };
        HealthEvidence? before = await ReadHealthAsync(http, arguments.Endpoint, cancellationToken);
        if (before is null)
            return Rejected("v4_7_health_invalid");

        Uri handshakeEndpoint = new(arguments.Endpoint, "/handshake");
        DataAgentGraphHandshakeHttpOptions httpOptions = new(
            handshakeEndpoint, TimeSpan.FromMilliseconds(arguments.TimeoutMs), true, false);
        DataAgentV44ProductionShadowOptions shadowOptions =
            DataAgentV44ProductionShadowOptions.FromValues(
                "true", "false", "100", "proven_useful", "2", "3", "30000");
        DataAgentV45ProductionObservationRecorder recorder = new();
        using DataAgentV44ProductionShadowClient shadow = new(
            new DataAgentGraphHandshakeHttpClient(http, httpOptions), shadowOptions);
        DataAgentGraphHandshakeCoordinator coordinator = new(
            new DataAgentGraphHandshakeOptions(true), shadow,
            observationRecorder: recorder);

        int accepted = 0;
        for (int sequence = 1; sequence <= arguments.RequestCount; sequence++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            DataAgentGraphHandshakeOutcome outcome = coordinator.TryHandshake(
                "v47-canary", "bounded production shadow advisory",
                DataAgentV47CanaryRequestFactory.Create(sequence));
            if (outcome.Status == DataAgentGraphHandshakeStatus.Accepted &&
                outcome.FallbackRequired == false)
                accepted++;
        }

        DataAgentV45ProductionObservationSnapshot snapshot = recorder.GetSnapshot(DateTimeOffset.UtcNow);
        HealthEvidence? after = await ReadHealthAsync(http, arguments.Endpoint, cancellationToken);
        bool stable = before == after;
        DataAgentV47RuntimeIdentityEvidence identity = new(
            before.RuntimeInstanceId, before.ConfigurationFingerprint,
            before.StartedAtUnixSeconds, stable);
        bool complete = accepted == arguments.RequestCount &&
            snapshot.ObservationCount == arguments.RequestCount &&
            snapshot.NetworkAttemptCount == arguments.RequestCount &&
            snapshot.FallbackCount == 0 && stable;
        return new(complete,
            complete ? "v4_7_canary_window_accepted" : "v4_7_canary_window_rejected",
            accepted, snapshot.NetworkAttemptCount, snapshot, identity);
    }

    static async Task<HealthEvidence?> ReadHealthAsync(
        HttpClient http, Uri endpoint, CancellationToken cancellationToken)
    {
        try
        {
            using HttpResponseMessage response = await http.GetAsync(new Uri(endpoint, "/health"), cancellationToken);
            if (response.IsSuccessStatusCode == false)
                return null;
            using JsonDocument document = JsonDocument.Parse(
                await response.Content.ReadAsStreamAsync(cancellationToken));
            JsonElement root = document.RootElement;
            string[] fields = ["ok", "ready", "runtimeMode", "langGraphLoaded", "langGraphVersion",
                "graphCompiled", "contractVersion", "graphVersion", "runtimeInstanceId",
                "configurationFingerprint", "startedAtUnixSeconds"];
            if (root.ValueKind != JsonValueKind.Object ||
                root.EnumerateObject().Select(item => item.Name).Order().SequenceEqual(fields.Order()) == false ||
                root.GetProperty("ok").GetBoolean() == false || root.GetProperty("ready").GetBoolean() == false ||
                root.GetProperty("runtimeMode").GetString() != "langgraph" ||
                root.GetProperty("langGraphLoaded").GetBoolean() == false ||
                root.GetProperty("langGraphVersion").GetString() != "0.3.34" ||
                root.GetProperty("graphCompiled").GetBoolean() == false ||
                root.GetProperty("contractVersion").GetString() != "v4.7" ||
                root.GetProperty("graphVersion").GetString() != "dataagent-advisory-v1")
                return null;
            string instance = root.GetProperty("runtimeInstanceId").GetString() ?? string.Empty;
            string fingerprint = root.GetProperty("configurationFingerprint").GetString() ?? string.Empty;
            long started = root.GetProperty("startedAtUnixSeconds").GetInt64();
            if (Guid.TryParseExact(instance, "D", out Guid parsed) == false || parsed.ToString("D") != instance ||
                fingerprint.Length != 64 || fingerprint.Any(ch => ch is not (>= 'a' and <= 'f' or >= '0' and <= '9')) ||
                started <= 0)
                return null;
            return new(instance, fingerprint, started);
        }
        catch (Exception) when (cancellationToken.IsCancellationRequested == false)
        {
            return null;
        }
    }

    static DataAgentV47CanaryRunResult Rejected(string reasonCode) =>
        new(false, reasonCode, 0, 0, null, null);

    sealed record HealthEvidence(
        string RuntimeInstanceId,
        string ConfigurationFingerprint,
        long StartedAtUnixSeconds);
}
