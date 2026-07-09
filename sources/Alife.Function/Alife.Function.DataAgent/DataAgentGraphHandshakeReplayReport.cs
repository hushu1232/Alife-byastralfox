namespace Alife.Function.DataAgent;

public sealed record DataAgentGraphHandshakeReplayInput(
    string FixtureId,
    DataAgentGraphHandshakeOutcome Deterministic,
    DataAgentGraphHandshakeOutcome Sidecar);

public sealed record DataAgentGraphHandshakeReplayFixtureResult(
    string FixtureId,
    DataAgentGraphHandshakeShadowComparison Comparison);

public sealed record DataAgentGraphHandshakeReplayReport(
    string ReplayId,
    IReadOnlyList<DataAgentGraphHandshakeReplayFixtureResult> Fixtures,
    IReadOnlyDictionary<string, int> StatusCounts,
    int ComparisonCount,
    bool DefaultResultChanged,
    bool Passed);

public static class DataAgentGraphHandshakeReplayReportConsolidator
{
    public static DataAgentGraphHandshakeReplayReport Create(
        string replayId,
        IEnumerable<DataAgentGraphHandshakeReplayInput> inputs)
    {
        string safeReplayId = string.IsNullOrWhiteSpace(replayId)
            ? "shadow-replay-report"
            : replayId.Trim();
        DataAgentGraphHandshakeReplayFixtureResult[] fixtures = inputs
            .Where(input => input is not null)
            .Select(input => new DataAgentGraphHandshakeReplayFixtureResult(
                input.FixtureId,
                DataAgentGraphHandshakeShadowComparer.Compare(input.Deterministic, input.Sidecar)))
            .ToArray();
        Dictionary<string, int> statusCounts = fixtures
            .GroupBy(fixture => fixture.Comparison.ReasonCode, StringComparer.Ordinal)
            .ToDictionary(group => group.Key, group => group.Count(), StringComparer.Ordinal);
        bool defaultResultChanged = fixtures.Any(fixture => fixture.Comparison.DefaultResultChanged);

        return new DataAgentGraphHandshakeReplayReport(
            safeReplayId,
            fixtures,
            statusCounts,
            fixtures.Length,
            defaultResultChanged,
            defaultResultChanged == false);
    }
}

public static class DataAgentGraphHandshakeReplayReportFormatter
{
    public static string FormatMarkdown(DataAgentGraphHandshakeReplayReport report)
    {
        ArgumentNullException.ThrowIfNull(report);

        List<string> lines =
        [
            $"# DataAgent Graph Shadow Replay Report: {SafeToken(report.ReplayId)}",
            "",
            "## Summary",
            "shadow_replay_report=true",
            "replay_fixture_pack=true",
            "source_fixture_pack=v3.19",
            "shadow_only=true",
            $"default_result_changed={LowerBool(report.DefaultResultChanged)}",
            $"comparison_count={report.ComparisonCount}",
            $"passed={LowerBool(report.Passed)}",
            "",
            "## Safety Boundary",
            "starts_runtime=false",
            "stores_secrets=false",
            "stores_sql=false",
            "stores_hidden_context=false",
            "fallback_required=true",
            "",
            "## Categories"
        ];

        foreach ((string status, int count) in report.StatusCounts.OrderBy(item => item.Key, StringComparer.Ordinal))
            lines.Add($"{SafeToken(status)}={count}");

        lines.Add("");
        lines.Add("## Fixtures");
        foreach (DataAgentGraphHandshakeReplayFixtureResult fixture in report.Fixtures.OrderBy(item => item.FixtureId, StringComparer.Ordinal))
            lines.Add($"fixture_{SafeToken(fixture.FixtureId)}={SafeToken(fixture.Comparison.ReasonCode)}");

        return string.Join(Environment.NewLine, lines);
    }

    static string LowerBool(bool value) => value ? "true" : "false";

    static string SafeToken(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return "redacted";

        string trimmed = value.Trim();
        if (trimmed.Length > DataAgentGraphHandshakeLimits.MaxReasonCodeLength ||
            DataAgentGraphHandshakeUnsafeDiagnosticDetector.ContainsUnsafeText(trimmed))
        {
            return "redacted";
        }

        foreach (char current in trimmed)
        {
            if (current is >= 'A' and <= 'Z'
                or >= 'a' and <= 'z'
                or >= '0' and <= '9'
                or '_'
                or '-'
                or '.')
            {
                continue;
            }

            return "redacted";
        }

        return trimmed;
    }
}
