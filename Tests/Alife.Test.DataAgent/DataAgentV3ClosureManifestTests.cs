using System.Text.RegularExpressions;
using Alife.Function.DataAgent;

namespace Alife.Test.DataAgent;

[TestFixture]
public sealed partial class DataAgentV3ClosureManifestTests
{
    const string InventoryStart = "[v3_closure_milestones]";
    const string InventoryEnd = "[/v3_closure_milestones]";
    const string MilestonePattern = @"^milestone=v3\.(0|[1-9]|1[0-9]|2[0-8])$";
    const string RuntimeBoundary = "changes_default_runtime=false";
    const string AuthorityBoundary = "grants_sidecar_authority=false";
    const string EvidenceTableHeader = "| Version | Evidence kind | Purpose | Exact evidence path | Required check / gate | Runtime boundary | Sidecar authority boundary |";
    const string EvidenceTableSeparator = "|---|---|---|---|---|---|---|";

    static readonly ClosureEvidenceRow[] ExpectedRows =
    [
        Row("v3.0", "DynamicReadiness", "Graph handshake boundary", "docs/dataagent/dataagent-v3.0-graph-handshake-boundary.md", "GraphHandshakeBoundaryPresent"),
        Row("v3.1", "DynamicReadiness", "Dev sidecar adapter", "docs/dataagent/dataagent-v3.1-dev-sidecar-adapter.md", "GraphHandshakeDevSidecarAdapterPresent"),
        Row("v3.2", "DynamicReadiness", "Progress bridge", "docs/dataagent/dataagent-v3.2-sidecar-progress-bridge.md", "GraphHandshakeDevSidecarProgressBridgePresent"),
        Row("v3.3", "DynamicReadiness", "NDJSON streaming", "docs/dataagent/dataagent-v3.3-ndjson-streaming-transport.md", "GraphHandshakeDevSidecarStreamingTransportPresent"),
        Row("v3.4", "StaticReadiness", "Manual live smoke", "docs/dataagent/dataagent-v3.4-dev-sidecar-live-smoke-harness.md", "GraphHandshakeDevSidecarLiveSmokeHarnessPresent"),
        Row("v3.5", "RegressionHardening", "Smoke contract regression", "Tests/Alife.Test.DataAgent/DataAgentGraphSidecarSmokeScriptContractTests.cs", "inherited V3.4/V3.6 gates"),
        Row("v3.6", "DynamicReadiness", "Sidecar observability", "sources/Alife.Function/Alife.Function.DataAgent/DataAgentGraphHandshakeModels.cs", "GraphHandshakeDevSidecarObservabilityContractPresent"),
        Row("v3.7", "RegressionHardening", "Reason-code hardening", "docs/superpowers/specs/2026-07-08-dataagent-v3.7-reason-code-stability-design.md", "inherited V3.6 gate"),
        Row("v3.8", "DynamicReadiness", "End-to-end chain", "Tests/Alife.Test.DataAgent/DataAgentEndToEndChainContractTests.cs", "DataAgentEndToEndChainContractPresent"),
        Row("v3.9", "DynamicReadiness", "Replay runbook", "Tests/Alife.Test.DataAgent/DataAgentReplayRunbookTests.cs", "DataAgentReplayRunbookPresent"),
        Row("v3.10", "StaticReadiness", "Runtime admission contract", "docs/dataagent/dataagent-v3.10-langgraph-runtime-readiness-contract.md", "LangGraphRuntimeReadinessContractPresent"),
        Row("v3.11", "DynamicReadiness", "Real LangGraph skeleton", "docs/dataagent/dataagent-v3.11-real-langgraph-sidecar-skeleton.md", "GraphHandshakeRealLangGraphSidecarSkeletonPresent"),
        Row("v3.12", "DynamicReadiness", "Replay parity", "docs/dataagent/dataagent-v3.12-replay-parity-shadow-comparison.md", "GraphHandshakeReplayParityShadowComparisonPresent"),
        Row("v3.13", "DynamicReadiness", "Bounded diagnostics", "docs/dataagent/dataagent-v3.13-bounded-diagnostics-explanation.md", "GraphHandshakeBoundedDiagnosticsExplanationPresent"),
        Row("v3.14", "DynamicReadiness", "Cross-module manifests", "docs/dataagent/dataagent-v3.14-cross-module-planner-manifests.md", "GraphHandshakeCrossModulePlannerManifestsPresent"),
        Row("v3.15", "DynamicReadiness", "Authority fallback regression", "docs/dataagent/dataagent-v3.15-authority-fallback-regression.md", "GraphHandshakeAuthorityFallbackRegressionPresent"),
        Row("v3.16", "DynamicReadiness", "Live smoke readiness", "docs/dataagent/dataagent-v3.16-langgraph-live-smoke-readiness.md", "GraphHandshakeLangGraphLiveSmokeReadinessPresent"),
        Row("v3.17", "DynamicReadiness", "Manual smoke harness", "docs/dataagent/dataagent-v3.17-langgraph-manual-smoke.md", "GraphHandshakeLangGraphManualSmokeHarnessPresent"),
        Row("v3.18", "OperatorArtifact", "Smoke artifact", "docs/dataagent/dataagent-v3.18-smoke-result-artifact.md", "GraphHandshakeSmokeResultArtifactFormatterPresent"),
        Row("v3.19", "OperatorArtifact", "Replay fixtures", "docs/dataagent/dataagent-v3.19-replay-fixture-pack.md", "GraphHandshakeReplayFixturePackPresent"),
        Row("v3.20", "OperatorArtifact", "Shadow replay report", "docs/dataagent/dataagent-v3.20-shadow-replay-report.md", "GraphHandshakeShadowReplayReportPresent"),
        Row("v3.21", "OperatorArtifact", "Replay report artifact", "docs/dataagent/dataagent-v3.21-manual-replay-report-artifact.md", "GraphHandshakeManualReplayReportArtifactWriterPresent"),
        Row("v3.22", "OperatorArtifact", "Artifact index", "docs/dataagent/dataagent-v3.22-manual-artifact-index.md", "GraphHandshakeManualArtifactIndexPresent"),
        Row("v3.23", "OperatorArtifact", "Audit bundle", "docs/dataagent/dataagent-v3.23-manual-audit-bundle.md", "GraphHandshakeManualAuditBundlePresent"),
        Row("v3.24", "DynamicReadiness", "Agent advisory contract", "docs/dataagent/dataagent-v3.24-agent-advisory-contract.md", "GraphHandshakeAgentAdvisoryContractPresent"),
        Row("v3.25", "DynamicReadiness", "Manual shadow provider", "docs/dataagent/dataagent-v3.25-real-langgraph-manual-shadow-provider.md", "GraphHandshakeRealLangGraphManualShadowProviderPresent"),
        Row("v3.26", "DynamicReadiness", "Replay diff gate", "docs/dataagent/dataagent-v3.26-harness-replay-diff-gate.md", "GraphHandshakeHarnessReplayDiffGatePresent"),
        Row("v3.27", "OperatorArtifact", "Operator evidence pack", "docs/dataagent/dataagent-v3.27-operator-evidence-pack.md", "GraphHandshakeOperatorEvidencePackPresent"),
        Row("v3.28", "FinalFreeze", "Final freeze", "docs/dataagent/dataagent-v3.28-final-readiness-freeze.md", "final freeze output")
    ];

    [Test]
    public void ClosureLedgerContainsEveryV3MilestoneExactlyOnce()
    {
        string repoRoot = FindRepoRoot();
        string ledger = File.ReadAllText(Path.Combine(repoRoot, "docs", "dataagent", "dataagent-v3-closure-ledger.md"));
        string[] versions = ParseMilestoneVersions(ledger);
        ClosureEvidenceRow[] rows = ParseEvidenceRows(ledger);
        string[] expectedVersions = Enumerable.Range(0, 29).Select(index => $"v3.{index}").ToArray();

        Assert.Multiple(() =>
        {
            Assert.That(versions, Is.EqualTo(expectedVersions));
            Assert.That(versions, Is.Unique);
            Assert.That(rows, Is.EqualTo(ExpectedRows));
            Assert.That(rows.Select(row => row.Version), Is.EqualTo(expectedVersions));
            Assert.That(rows.Select(row => row.Version), Is.Unique);
            Assert.That(rows.Select(row => row.RuntimeBoundary), Has.All.EqualTo(RuntimeBoundary));
            Assert.That(rows.Select(row => row.AuthorityBoundary), Has.All.EqualTo(AuthorityBoundary));

            foreach (ClosureEvidenceRow row in rows)
            {
                Assert.That(
                    File.Exists(Path.Combine(repoRoot, row.EvidencePath.Replace('/', Path.DirectorySeparatorChar))),
                    Is.True,
                    $"Missing evidence for {row.Version}: {row.EvidencePath}");
            }
        });
    }

    [Test]
    public void EvidenceTableParserDoesNotIgnoreCompactWhitespaceDuplicateRow()
    {
        string repoRoot = FindRepoRoot();
        string ledger = File.ReadAllText(Path.Combine(repoRoot, "docs", "dataagent", "dataagent-v3-closure-ledger.md"));
        string standardRow = SplitLines(ledger).Single(line => line.StartsWith("| v3.4 |", StringComparison.Ordinal));
        string compactRow = standardRow
            .Replace(" | ", "|", StringComparison.Ordinal)
            .Replace("| ", "|", StringComparison.Ordinal)
            .Replace(" |", "|", StringComparison.Ordinal);
        string mutatedLedger = ledger.Replace(
            standardRow,
            $"{standardRow}{Environment.NewLine}{compactRow}",
            StringComparison.Ordinal);

        ClosureEvidenceRow[] rows = ParseEvidenceRows(mutatedLedger);

        Assert.Multiple(() =>
        {
            Assert.That(rows, Has.Length.EqualTo(30));
            Assert.That(rows.Select(row => row.Version).Count(version => version == "v3.4"), Is.EqualTo(2));
            Assert.That(rows, Is.Not.EqualTo(ExpectedRows));
        });
    }

    static string[] ParseMilestoneVersions(string ledger)
    {
        string[] lines = SplitLines(ledger);
        int start = FindSingleDelimiter(lines, InventoryStart);
        int end = FindSingleDelimiter(lines, InventoryEnd);
        Assert.That(end, Is.GreaterThan(start), "The milestone inventory end delimiter must follow its start delimiter.");

        string[] inventoryLines = lines[(start + 1)..end];
        foreach (string line in inventoryLines)
        {
            Assert.That(Regex.IsMatch(line, MilestonePattern, RegexOptions.CultureInvariant), Is.True, $"Invalid milestone inventory line: '{line}'");
        }

        return inventoryLines.Select(line => line["milestone=".Length..]).ToArray();
    }

    static ClosureEvidenceRow[] ParseEvidenceRows(string ledger)
    {
        string[] lines = SplitLines(ledger);
        int header = FindSingleDelimiter(lines, EvidenceTableHeader);
        Assert.That(header + 1, Is.LessThan(lines.Length), "The closure table must include a separator row.");
        Assert.That(lines[header + 1], Is.EqualTo(EvidenceTableSeparator), "The closure table separator row is malformed.");

        List<ClosureEvidenceRow> rows = [];
        for (int index = header + 2; index < lines.Length; index++)
        {
            string line = lines[index].Trim();
            if (line.Length == 0) break;
            rows.Add(ParseEvidenceRow(line));
        }
        return rows.ToArray();
    }

    static ClosureEvidenceRow ParseEvidenceRow(string line)
    {
        line = line.Trim();
        string[] columns = line.Split('|');
        Assert.Multiple(() =>
        {
            Assert.That(columns, Has.Length.EqualTo(9), $"Evidence rows must contain exactly seven columns: {line}");
            Assert.That(columns[0], Is.Empty, $"Evidence rows must start with '|': {line}");
            Assert.That(columns[^1], Is.Empty, $"Evidence rows must end with '|': {line}");
        });

        string[] values = columns[1..^1].Select(column => column.Trim()).ToArray();
        return new ClosureEvidenceRow(
            values[0],
            values[1],
            values[2],
            UnwrapOptionalCodeSpan(values[3]),
            UnwrapOptionalCodeSpan(values[4]),
            UnwrapOptionalCodeSpan(values[5]),
            UnwrapOptionalCodeSpan(values[6]));
    }

    static string UnwrapOptionalCodeSpan(string value)
    {
        bool startsWithBacktick = value.StartsWith('`');
        bool endsWithBacktick = value.EndsWith('`');
        Assert.That(startsWithBacktick, Is.EqualTo(endsWithBacktick), $"Malformed Markdown code span: {value}");
        return startsWithBacktick ? value[1..^1] : value;
    }

    static string[] SplitLines(string text) =>
        text.Split('\n').Select(line => line.TrimEnd('\r')).ToArray();

    static int FindSingleDelimiter(string[] lines, string delimiter)
    {
        int[] indexes = lines
            .Select((line, index) => (line, index))
            .Where(item => item.line == delimiter)
            .Select(item => item.index)
            .ToArray();
        Assert.That(indexes, Has.Exactly(1).Items, $"Expected exactly one '{delimiter}' line.");
        return indexes.Single();
    }

    static ClosureEvidenceRow Row(string version, string evidenceKind, string purpose, string evidencePath, string requiredCheck) =>
        new(version, evidenceKind, purpose, evidencePath, requiredCheck, RuntimeBoundary, AuthorityBoundary);

    static string FindRepoRoot()
    {
        for (DirectoryInfo? directory = new(TestContext.CurrentContext.TestDirectory); directory != null; directory = directory.Parent)
        {
            if (File.Exists(Path.Combine(directory.FullName, "Alife.slnx"))) return directory.FullName;
        }
        throw new DirectoryNotFoundException("Could not locate repository root.");
    }

    sealed record ClosureEvidenceRow(
        string Version,
        string EvidenceKind,
        string Purpose,
        string EvidencePath,
        string RequiredCheck,
        string RuntimeBoundary,
        string AuthorityBoundary);
}
