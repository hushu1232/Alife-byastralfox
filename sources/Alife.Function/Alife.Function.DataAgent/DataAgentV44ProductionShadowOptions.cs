using System.Globalization;

namespace Alife.Function.DataAgent;

public sealed record DataAgentV44ProductionShadowOptions(
    bool Enabled,
    bool KillSwitchActive,
    int ValueScore,
    string ValueStatus,
    int MaxConcurrency,
    int FailureThreshold,
    TimeSpan CircuitOpenDuration)
{
    public const string EnabledEnvironmentVariable = "ALIFE_DATAAGENT_V44_PRODUCTION_SHADOW_ENABLED";
    public const string KillSwitchEnvironmentVariable = "ALIFE_DATAAGENT_V44_KILL_SWITCH";
    public const string ValueScoreEnvironmentVariable = "ALIFE_DATAAGENT_V44_VALUE_SCORE";
    public const string ValueStatusEnvironmentVariable = "ALIFE_DATAAGENT_V44_VALUE_STATUS";
    public const string MaxConcurrencyEnvironmentVariable = "ALIFE_DATAAGENT_V44_MAX_CONCURRENCY";
    public const string FailureThresholdEnvironmentVariable = "ALIFE_DATAAGENT_V44_FAILURE_THRESHOLD";
    public const string CircuitOpenMsEnvironmentVariable = "ALIFE_DATAAGENT_V44_CIRCUIT_OPEN_MS";

    public const int DefaultMaxConcurrency = 2;
    public const int DefaultFailureThreshold = 3;
    public const int DefaultCircuitOpenMs = 30_000;

    public bool ValueGatePassed =>
        ValueScore >= DataAgentV43CrossModuleValueEvaluator.ProductionShadowEligibilityScore &&
        string.Equals(ValueStatus, "proven_useful", StringComparison.Ordinal);

    public bool Ready => Enabled && KillSwitchActive == false && ValueGatePassed;

    public static DataAgentV44ProductionShadowOptions FromEnvironment() =>
        FromValues(
            Environment.GetEnvironmentVariable(EnabledEnvironmentVariable),
            Environment.GetEnvironmentVariable(KillSwitchEnvironmentVariable),
            Environment.GetEnvironmentVariable(ValueScoreEnvironmentVariable),
            Environment.GetEnvironmentVariable(ValueStatusEnvironmentVariable),
            Environment.GetEnvironmentVariable(MaxConcurrencyEnvironmentVariable),
            Environment.GetEnvironmentVariable(FailureThresholdEnvironmentVariable),
            Environment.GetEnvironmentVariable(CircuitOpenMsEnvironmentVariable));

    public static DataAgentV44ProductionShadowOptions FromValues(
        string? enabled,
        string? killSwitch,
        string? valueScore,
        string? valueStatus,
        string? maxConcurrency,
        string? failureThreshold,
        string? circuitOpenMs)
    {
        bool parsedEnabled = ParseBool(enabled, fallback: false);
        bool parsedKillSwitch = ParseBool(killSwitch, fallback: true);
        int parsedScore = ParseInt(valueScore, 0, 100, 0);
        int parsedConcurrency = ParseInt(maxConcurrency, 1, 8, DefaultMaxConcurrency);
        int parsedThreshold = ParseInt(failureThreshold, 1, 10, DefaultFailureThreshold);
        int parsedCircuitMs = ParseInt(circuitOpenMs, 1_000, 300_000, DefaultCircuitOpenMs);

        return new DataAgentV44ProductionShadowOptions(
            parsedEnabled,
            parsedKillSwitch,
            parsedScore,
            NormalizeStatus(valueStatus),
            parsedConcurrency,
            parsedThreshold,
            TimeSpan.FromMilliseconds(parsedCircuitMs));
    }

    static bool ParseBool(string? value, bool fallback) => value?.Trim().ToLowerInvariant() switch
    {
        "true" or "1" or "yes" => true,
        "false" or "0" or "no" => false,
        _ => fallback
    };

    static int ParseInt(string? value, int min, int max, int fallback) =>
        int.TryParse(value?.Trim(), NumberStyles.None, CultureInfo.InvariantCulture, out int parsed) &&
        parsed >= min && parsed <= max
            ? parsed
            : fallback;

    static string NormalizeStatus(string? value) => value?.Trim().ToLowerInvariant() switch
    {
        "proven_useful" => "proven_useful",
        "promising" => "promising",
        "unproven" => "unproven",
        "rejected" => "rejected",
        _ => "unproven"
    };
}
