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

[TestFixture]
public sealed partial class DataAgentV3ClosureManifestTests
{
    [Test]
    public void DefaultManifestCoversV30ThroughV328ExactlyOnce()
    {
        IReadOnlyList<DataAgentV3MilestoneEvidence> manifest = DataAgentV3ClosureManifest.CreateDefault();
        string[] versions = Enumerable.Range(0, 29).Select(index => $"v3.{index}").ToArray();

        Assert.Multiple(() =>
        {
            Assert.That(manifest, Has.Count.EqualTo(29));
            Assert.That(manifest.Select(entry => entry.Version), Is.EqualTo(versions));
            Assert.That(manifest.Select(entry => entry.Version), Is.Unique);
            Assert.That(DataAgentV3ClosureManifest.ExpectedVersions, Is.EqualTo(versions));
            AssertChannel(manifest, "v3.4", DataAgentV3EvidenceKind.StaticReadiness, [], ["GraphHandshakeDevSidecarLiveSmokeHarnessPresent"], "GraphHandshakeDevSidecarLiveSmokeHarnessPresent");
            AssertChannel(manifest, "v3.5", DataAgentV3EvidenceKind.RegressionHardening, [], [], "inherited V3.4/V3.6 gates");
            AssertChannel(manifest, "v3.7", DataAgentV3EvidenceKind.RegressionHardening, [], [], "inherited V3.6 gate");
            AssertChannel(manifest, "v3.10", DataAgentV3EvidenceKind.StaticReadiness, [], ["LangGraphRuntimeReadinessContractPresent"], "LangGraphRuntimeReadinessContractPresent");
            AssertChannel(manifest, "v3.28", DataAgentV3EvidenceKind.FinalFreeze, [], [], "final freeze output");
            Assert.That(manifest, Has.All.Property(nameof(DataAgentV3MilestoneEvidence.ChangesDefaultRuntime)).False);
            Assert.That(manifest, Has.All.Property(nameof(DataAgentV3MilestoneEvidence.GrantsSidecarAuthority)).False);
        });
    }

    [Test]
    public void ManifestMatchesStructuredLedgerAcrossAllSevenFields()
    {
        DataAgentV3LedgerParseResult ledger = ParseRealLedgerWithProductionParser();
        DataAgentV3LedgerEntry[] manifestRows = DataAgentV3ClosureManifest.CreateDefault().Select(ToLedgerEntry).ToArray();

        Assert.Multiple(() =>
        {
            Assert.That(ledger.Errors, Is.Empty);
            Assert.That(ledger.MilestoneVersions, Is.EqualTo(DataAgentV3ClosureManifest.ExpectedVersions));
            Assert.That(ledger.Entries, Has.Count.EqualTo(29));
            Assert.That(manifestRows, Is.EqualTo(ledger.Entries));
        });
    }

    static IEnumerable<TestCaseData> StrictLedgerMutations()
    {
        string ledger = ReadRealLedger();
        string start = "[v3_closure_milestones]";
        string end = "[/v3_closure_milestones]";
        string row = ledger.Split('\n').Select(line => line.TrimEnd('\r')).Single(line => line.StartsWith("| v3.4 |", StringComparison.Ordinal));
        string compactRow = "|" + string.Join("|", row.Split('|')[1..^1].Select(value => value.Trim())) + "|";

        yield return new(ledger.Replace(start, "", StringComparison.Ordinal)) { TestName = "ParseLedgerRejectsMissingStart" };
        yield return new(ledger.Replace(end, "", StringComparison.Ordinal)) { TestName = "ParseLedgerRejectsMissingEnd" };
        yield return new(ledger.Replace(start, $"{start}\n{start}", StringComparison.Ordinal)) { TestName = "ParseLedgerRejectsDuplicateStart" };
        yield return new(ledger.Replace(end, $"{end}\n{end}", StringComparison.Ordinal)) { TestName = "ParseLedgerRejectsDuplicateEnd" };
        yield return new($"milestone=v3.4\n{ledger}") { TestName = "ParseLedgerRejectsValidMarkerOutsideBlock" };
        yield return new(ledger.Replace("milestone=v3.4", "milestone=v3.04", StringComparison.Ordinal)) { TestName = "ParseLedgerRejectsMalformedMarker" };
        yield return new(ledger.Replace("milestone=v3.28", "milestone=v3.29", StringComparison.Ordinal)) { TestName = "ParseLedgerRejectsV329OutOfRange" };
        yield return new(ledger.Replace("milestone=v3.3\nmilestone=v3.4", "milestone=v3.4\nmilestone=v3.3", StringComparison.Ordinal)) { TestName = "ParseLedgerRejectsWrongOrder" };
        yield return new(ledger.Replace(row, $"{row}\n{compactRow}", StringComparison.Ordinal)) { TestName = "ParseLedgerRejectsCompactDuplicateTableRow" };
    }

    [TestCaseSource(nameof(StrictLedgerMutations))]
    public void ParseLedgerRejectsMalformedDocument(string ledger) =>
        Assert.That(DataAgentV3ClosureManifest.ParseLedger(ledger).Errors, Is.Not.Empty);

    [Test]
    public void ValidatorAcceptsCompleteEvidenceWithoutV4Checks()
    {
        Fixture fixture = CompleteFixture();
        DataAgentV3ClosureResult result = ValidateFixture(fixture);

        Assert.Multiple(() =>
        {
            Assert.That(result.Accepted, Is.True);
            Assert.That(result.StaticRequiredCheckCount, Is.EqualTo(111));
            Assert.That(result.FrozenCoreCheckCount, Is.EqualTo(95));
            Assert.That(result.MissingMilestoneVersions, Is.Empty);
            Assert.That(result.DuplicateMilestoneVersions, Is.Empty);
            Assert.That(result.UnexpectedMilestoneVersions, Is.Empty);
            Assert.That(result.MissingEvidencePaths, Is.Empty);
            Assert.That(result.MissingRequiredCheckNames, Is.Empty);
            Assert.That(result.FailedRequiredCheckNames, Is.Empty);
            Assert.That(result.DuplicateRequiredCheckNames, Is.Empty);
            Assert.That(result.UnexpectedV4CheckNames, Is.Empty);
            Assert.That(result.LedgerParseErrors, Is.Empty);
            Assert.That(result.LedgerParityMismatches, Is.Empty);
            Assert.That(result.AuthorityExpansionCount, Is.Zero);
            Assert.That(result.OperatorEvidencePackPresent, Is.True);
            Assert.That(result.StaticCountMatches, Is.True);
            Assert.That(result.CoreCountMatches, Is.True);
        });
    }

    [Test]
    public void ValidatorRejectsMissingV310MilestoneOrStaticContract()
    {
        Fixture fixture = CompleteFixture();
        DataAgentV3ClosureResult milestone = ValidateFixture(fixture with { Manifest = fixture.Manifest.Where(entry => entry.Version != "v3.10").ToArray() });
        DataAgentV3ClosureResult staticContract = ValidateFixture(fixture with { StaticNames = fixture.StaticNames.Where(name => name != "LangGraphRuntimeReadinessContractPresent").ToArray() });

        Assert.Multiple(() =>
        {
            Assert.That(milestone.MissingMilestoneVersions, Does.Contain("v3.10"));
            Assert.That(staticContract.MissingRequiredCheckNames, Does.Contain("LangGraphRuntimeReadinessContractPresent"));
            Assert.That(milestone.Accepted, Is.False);
            Assert.That(staticContract.Accepted, Is.False);
        });
    }

    [Test]
    public void ValidatorRejectsMissingFailedOrDuplicateRequiredChecks()
    {
        Fixture fixture = CompleteFixture();
        string required = fixture.Manifest.SelectMany(entry => entry.RequiredDynamicCheckNames).First();
        int requiredIndex = fixture.DynamicChecks.FindIndex(check => check.Name == required);
        int baselineIndex = fixture.DynamicChecks.FindIndex(check => check.Name.StartsWith("BaselineCheck", StringComparison.Ordinal));
        int operatorIndex = fixture.DynamicChecks.FindIndex(check => check.Name == "GraphHandshakeOperatorEvidencePackPresent");
        List<DataAgentReadinessCheck> missing = [.. fixture.DynamicChecks];
        List<DataAgentReadinessCheck> failed = [.. fixture.DynamicChecks];
        List<DataAgentReadinessCheck> duplicate = [.. fixture.DynamicChecks];
        List<DataAgentReadinessCheck> malformedOperator = [.. fixture.DynamicChecks];
        missing[requiredIndex] = new("ReplacementBaseline", true, "baseline=true");
        failed[requiredIndex] = failed[requiredIndex] with { Passed = false };
        duplicate[baselineIndex] = duplicate[requiredIndex];
        malformedOperator[operatorIndex] = malformedOperator[operatorIndex] with { Detail = "operator_evidence_pack=true" };

        DataAgentV3ClosureResult missingResult = ValidateFixture(fixture with { DynamicChecks = missing });
        DataAgentV3ClosureResult failedResult = ValidateFixture(fixture with { DynamicChecks = failed });
        DataAgentV3ClosureResult duplicateResult = ValidateFixture(fixture with { DynamicChecks = duplicate });
        DataAgentV3ClosureResult duplicateStatic = ValidateFixture(fixture with { StaticNames = [.. fixture.StaticNames, fixture.StaticNames[0]] });
        DataAgentV3ClosureResult operatorResult = ValidateFixture(fixture with { DynamicChecks = malformedOperator });

        Assert.Multiple(() =>
        {
            Assert.That(missingResult.MissingRequiredCheckNames, Does.Contain(required));
            Assert.That(failedResult.FailedRequiredCheckNames, Does.Contain(required));
            Assert.That(duplicateResult.DuplicateRequiredCheckNames, Does.Contain(required));
            Assert.That(duplicateStatic.DuplicateRequiredCheckNames, Does.Contain(fixture.StaticNames[0]));
            Assert.That(operatorResult.OperatorEvidencePackPresent, Is.False);
            Assert.That(operatorResult.Accepted, Is.False);
        });
    }

    [Test]
    public void ValidatorRejectsMissingEvidencePathAndStaticOrCoreCountDrift()
    {
        Fixture fixture = CompleteFixture();
        string path = fixture.Manifest[0].EvidencePath;
        DataAgentV3ClosureResult evidence = ValidateFixture(fixture with { EvidencePaths = fixture.EvidencePaths.Where(value => value != path).ToHashSet(StringComparer.Ordinal) });
        DataAgentV3ClosureResult staticDrift = ValidateFixture(fixture with { StaticCount = 110 });
        DataAgentV3ClosureResult coreDrift = ValidateFixture(fixture with { DynamicChecks = fixture.DynamicChecks.Skip(1).ToList() });

        Assert.Multiple(() =>
        {
            Assert.That(evidence.MissingEvidencePaths, Does.Contain(path));
            Assert.That(staticDrift.StaticCountMatches, Is.False);
            Assert.That(staticDrift.StaticRequiredCheckCount, Is.EqualTo(110));
            Assert.That(coreDrift.CoreCountMatches, Is.False);
            Assert.That(coreDrift.FrozenCoreCheckCount, Is.EqualTo(94));
        });
    }

    [Test]
    public void ValidatorRejectsV4SubstitutionAndAuthorityExpansion()
    {
        Fixture fixture = CompleteFixture();
        List<DataAgentReadinessCheck> v4Checks = [.. fixture.DynamicChecks];
        int baseline = v4Checks.FindIndex(check => check.Name.StartsWith("BaselineCheck", StringComparison.Ordinal));
        v4Checks[baseline] = new("GraphHandshakeRealLangGraphManualShadowIntegrationPresent", true, "v4=true");
        DataAgentV3MilestoneEvidence authority = fixture.Manifest[0] with { GrantsSidecarAuthority = true };
        DataAgentV3MilestoneEvidence runtime = fixture.Manifest[0] with { ChangesDefaultRuntime = true };

        DataAgentV3ClosureResult v4 = ValidateFixture(fixture with { DynamicChecks = v4Checks });
        DataAgentV3ClosureResult authorityResult = ValidateFixture(fixture with { Manifest = [authority, .. fixture.Manifest.Skip(1)] });
        DataAgentV3ClosureResult runtimeResult = ValidateFixture(fixture with { Manifest = [runtime, .. fixture.Manifest.Skip(1)] });

        Assert.Multiple(() =>
        {
            Assert.That(v4.UnexpectedV4CheckNames, Does.Contain("GraphHandshakeRealLangGraphManualShadowIntegrationPresent"));
            Assert.That(authorityResult.AuthorityExpansionCount, Is.EqualTo(1));
            Assert.That(runtimeResult.AuthorityExpansionCount, Is.EqualTo(1));
            Assert.That(v4.Accepted, Is.False);
            Assert.That(authorityResult.Accepted, Is.False);
            Assert.That(runtimeResult.Accepted, Is.False);
        });
    }

    [Test]
    public void ValidatorRejectsMutationOfEachLedgerParityField()
    {
        Fixture fixture = CompleteFixture();
        DataAgentV3MilestoneEvidence value = fixture.Manifest[0];
        DataAgentV3MilestoneEvidence[] mutations =
        [
            value with { Version = "v3.29" },
            value with { EvidenceKind = DataAgentV3EvidenceKind.ContractTest },
            value with { Purpose = "mutated" },
            value with { EvidencePath = "docs/dataagent/mutated.md" },
            value with { RequiredGateLabel = "MutatedGate" },
            value with { ChangesDefaultRuntime = true },
            value with { GrantsSidecarAuthority = true }
        ];
        DataAgentV3ClosureResult[] results = mutations.Select(mutation =>
        {
            DataAgentV3MilestoneEvidence[] manifest = [mutation, .. fixture.Manifest.Skip(1)];
            return ValidateFixture(fixture with { Manifest = manifest, EvidencePaths = manifest.Select(entry => entry.EvidencePath).ToHashSet(StringComparer.Ordinal) });
        }).ToArray();

        Assert.Multiple(() =>
        {
            Assert.That(results, Has.All.Property(nameof(DataAgentV3ClosureResult.Accepted)).False);
            Assert.That(results.Select(result => result.LedgerParityMismatches), Has.All.Not.Empty);
        });
    }

    static Fixture CompleteFixture()
    {
        IReadOnlyList<DataAgentV3MilestoneEvidence> manifest = DataAgentV3ClosureManifest.CreateDefault();
        List<DataAgentReadinessCheck> checks = manifest.SelectMany(entry => entry.RequiredDynamicCheckNames)
            .Select(name => new DataAgentReadinessCheck(name, true, name == "GraphHandshakeOperatorEvidencePackPresent" ? "operator_evidence_pack=true;operator_decides=true" : "required=true"))
            .ToList();
        for (int index = checks.Count; index < 95; index++) checks.Add(new($"BaselineCheck{index:000}", true, "baseline=true"));
        return new(
            manifest,
            checks,
            manifest.SelectMany(entry => entry.RequiredStaticCheckNames).ToArray(),
            manifest.Select(entry => entry.EvidencePath).ToHashSet(StringComparer.Ordinal),
            ParseRealLedgerWithProductionParser(),
            111);
    }

    static DataAgentV3ClosureResult ValidateFixture(Fixture fixture) => DataAgentV3ClosureValidator.Validate(
        fixture.Manifest, fixture.DynamicChecks, fixture.Ledger, fixture.StaticNames, fixture.EvidencePaths, fixture.StaticCount);

    static DataAgentV3LedgerParseResult ParseRealLedgerWithProductionParser() => DataAgentV3ClosureManifest.ParseLedger(ReadRealLedger());
    static string ReadRealLedger() => File.ReadAllText(Path.Combine(FindRepoRoot(), "docs", "dataagent", "dataagent-v3-closure-ledger.md"));
    static DataAgentV3LedgerEntry ToLedgerEntry(DataAgentV3MilestoneEvidence entry) => new(
        entry.Version, entry.EvidenceKind, entry.Purpose, entry.EvidencePath, entry.RequiredGateLabel, entry.ChangesDefaultRuntime, entry.GrantsSidecarAuthority);

    static void AssertChannel(IReadOnlyList<DataAgentV3MilestoneEvidence> manifest, string version, DataAgentV3EvidenceKind kind, string[] dynamicNames, string[] staticNames, string gate)
    {
        DataAgentV3MilestoneEvidence entry = manifest.Single(item => item.Version == version);
        Assert.Multiple(() =>
        {
            Assert.That(entry.EvidenceKind, Is.EqualTo(kind));
            Assert.That(entry.RequiredDynamicCheckNames, Is.EqualTo(dynamicNames));
            Assert.That(entry.RequiredStaticCheckNames, Is.EqualTo(staticNames));
            Assert.That(entry.RequiredGateLabel, Is.EqualTo(gate));
        });
    }

    sealed record Fixture(
        IReadOnlyList<DataAgentV3MilestoneEvidence> Manifest,
        List<DataAgentReadinessCheck> DynamicChecks,
        string[] StaticNames,
        HashSet<string> EvidencePaths,
        DataAgentV3LedgerParseResult Ledger,
        int StaticCount);
}
