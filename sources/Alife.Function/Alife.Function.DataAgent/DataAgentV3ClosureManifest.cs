using System.Text.RegularExpressions;

namespace Alife.Function.DataAgent;

public enum DataAgentV3EvidenceKind
{
    DynamicReadiness,
    StaticReadiness,
    ContractTest,
    RegressionHardening,
    OperatorArtifact,
    FinalFreeze
}

public sealed record DataAgentV3MilestoneEvidence(
    string Version,
    DataAgentV3EvidenceKind EvidenceKind,
    string Purpose,
    string EvidencePath,
    string RequiredGateLabel,
    IReadOnlyList<string> RequiredDynamicCheckNames,
    IReadOnlyList<string> RequiredStaticCheckNames,
    bool ChangesDefaultRuntime,
    bool GrantsSidecarAuthority);

public sealed record DataAgentV3LedgerEntry(
    string Version,
    DataAgentV3EvidenceKind EvidenceKind,
    string Purpose,
    string EvidencePath,
    string RequiredGateLabel,
    bool ChangesDefaultRuntime,
    bool GrantsSidecarAuthority);

public sealed record DataAgentV3LedgerParseResult(
    IReadOnlyList<string> MilestoneVersions,
    IReadOnlyList<DataAgentV3LedgerEntry> Entries,
    IReadOnlyList<string> Errors);

public sealed record DataAgentV3ClosureResult(
    bool Accepted,
    int StaticRequiredCheckCount,
    int FrozenCoreCheckCount,
    IReadOnlyList<string> MissingMilestoneVersions,
    IReadOnlyList<string> DuplicateMilestoneVersions,
    IReadOnlyList<string> UnexpectedMilestoneVersions,
    IReadOnlyList<string> MissingEvidencePaths,
    IReadOnlyList<string> MissingRequiredCheckNames,
    IReadOnlyList<string> FailedRequiredCheckNames,
    IReadOnlyList<string> DuplicateRequiredCheckNames,
    IReadOnlyList<string> UnexpectedV4CheckNames,
    IReadOnlyList<string> LedgerParseErrors,
    IReadOnlyList<string> LedgerParityMismatches,
    int AuthorityExpansionCount,
    bool OperatorEvidencePackPresent,
    bool StaticCountMatches,
    bool CoreCountMatches);

public static class DataAgentV3ClosureManifest
{
    const string InventoryStart = "[v3_closure_milestones]";
    const string InventoryEnd = "[/v3_closure_milestones]";
    const string EvidenceTableHeader = "| Version | Evidence kind | Purpose | Exact evidence path | Required check / gate | Runtime boundary | Sidecar authority boundary |";
    const string EvidenceTableSeparator = "|---|---|---|---|---|---|---|";
    const string RuntimeBoundary = "changes_default_runtime=false";
    const string AuthorityBoundary = "grants_sidecar_authority=false";
    const string MilestonePattern = @"^milestone=v3\.(0|[1-9]|1[0-9]|2[0-8])$";

    public const int ExpectedFrozenStaticRequiredCount = 111;
    public const int ExpectedFrozenCoreCount = 95;

    public static IReadOnlyList<string> ExpectedVersions { get; } =
        Enumerable.Range(0, 29).Select(index => $"v3.{index}").ToArray();

    public static IReadOnlySet<string> V4OnlyCheckNames { get; } = new HashSet<string>(StringComparer.Ordinal)
    {
        "GraphHandshakeRealLangGraphManualShadowIntegrationPresent",
        "GraphHandshakeRealLangGraphManualShadowContextBudgetPresent"
    };

    public static IReadOnlyList<DataAgentV3MilestoneEvidence> CreateDefault() =>
    [
        Dynamic("v3.0", "Graph handshake boundary", "docs/dataagent/dataagent-v3.0-graph-handshake-boundary.md", "GraphHandshakeBoundaryPresent"),
        Dynamic("v3.1", "Dev sidecar adapter", "docs/dataagent/dataagent-v3.1-dev-sidecar-adapter.md", "GraphHandshakeDevSidecarAdapterPresent"),
        Dynamic("v3.2", "Progress bridge", "docs/dataagent/dataagent-v3.2-sidecar-progress-bridge.md", "GraphHandshakeDevSidecarProgressBridgePresent"),
        Dynamic("v3.3", "NDJSON streaming", "docs/dataagent/dataagent-v3.3-ndjson-streaming-transport.md", "GraphHandshakeDevSidecarStreamingTransportPresent"),
        Static("v3.4", "Manual live smoke", "docs/dataagent/dataagent-v3.4-dev-sidecar-live-smoke-harness.md", "GraphHandshakeDevSidecarLiveSmokeHarnessPresent"),
        GateOnly("v3.5", DataAgentV3EvidenceKind.RegressionHardening, "Smoke contract regression", "Tests/Alife.Test.DataAgent/DataAgentGraphSidecarSmokeScriptContractTests.cs", "inherited V3.4/V3.6 gates"),
        Dynamic("v3.6", "Sidecar observability", "sources/Alife.Function/Alife.Function.DataAgent/DataAgentGraphHandshakeModels.cs", "GraphHandshakeDevSidecarObservabilityContractPresent"),
        GateOnly("v3.7", DataAgentV3EvidenceKind.RegressionHardening, "Reason-code hardening", "docs/superpowers/specs/2026-07-08-dataagent-v3.7-reason-code-stability-design.md", "inherited V3.6 gate"),
        Dynamic("v3.8", "End-to-end chain", "Tests/Alife.Test.DataAgent/DataAgentEndToEndChainContractTests.cs", "DataAgentEndToEndChainContractPresent"),
        Dynamic("v3.9", "Replay runbook", "Tests/Alife.Test.DataAgent/DataAgentReplayRunbookTests.cs", "DataAgentReplayRunbookPresent"),
        Static("v3.10", "Runtime admission contract", "docs/dataagent/dataagent-v3.10-langgraph-runtime-readiness-contract.md", "LangGraphRuntimeReadinessContractPresent"),
        Dynamic("v3.11", "Real LangGraph skeleton", "docs/dataagent/dataagent-v3.11-real-langgraph-sidecar-skeleton.md", "GraphHandshakeRealLangGraphSidecarSkeletonPresent"),
        Dynamic("v3.12", "Replay parity", "docs/dataagent/dataagent-v3.12-replay-parity-shadow-comparison.md", "GraphHandshakeReplayParityShadowComparisonPresent"),
        Dynamic("v3.13", "Bounded diagnostics", "docs/dataagent/dataagent-v3.13-bounded-diagnostics-explanation.md", "GraphHandshakeBoundedDiagnosticsExplanationPresent"),
        Dynamic("v3.14", "Cross-module manifests", "docs/dataagent/dataagent-v3.14-cross-module-planner-manifests.md", "GraphHandshakeCrossModulePlannerManifestsPresent"),
        Dynamic("v3.15", "Authority fallback regression", "docs/dataagent/dataagent-v3.15-authority-fallback-regression.md", "GraphHandshakeAuthorityFallbackRegressionPresent"),
        Dynamic("v3.16", "Live smoke readiness", "docs/dataagent/dataagent-v3.16-langgraph-live-smoke-readiness.md", "GraphHandshakeLangGraphLiveSmokeReadinessPresent"),
        Dynamic("v3.17", "Manual smoke harness", "docs/dataagent/dataagent-v3.17-langgraph-manual-smoke.md", "GraphHandshakeLangGraphManualSmokeHarnessPresent"),
        Dynamic("v3.18", DataAgentV3EvidenceKind.OperatorArtifact, "Smoke artifact", "docs/dataagent/dataagent-v3.18-smoke-result-artifact.md", "GraphHandshakeSmokeResultArtifactFormatterPresent"),
        Dynamic("v3.19", DataAgentV3EvidenceKind.OperatorArtifact, "Replay fixtures", "docs/dataagent/dataagent-v3.19-replay-fixture-pack.md", "GraphHandshakeReplayFixturePackPresent"),
        Dynamic("v3.20", DataAgentV3EvidenceKind.OperatorArtifact, "Shadow replay report", "docs/dataagent/dataagent-v3.20-shadow-replay-report.md", "GraphHandshakeShadowReplayReportPresent"),
        Dynamic("v3.21", DataAgentV3EvidenceKind.OperatorArtifact, "Replay report artifact", "docs/dataagent/dataagent-v3.21-manual-replay-report-artifact.md", "GraphHandshakeManualReplayReportArtifactWriterPresent"),
        Dynamic("v3.22", DataAgentV3EvidenceKind.OperatorArtifact, "Artifact index", "docs/dataagent/dataagent-v3.22-manual-artifact-index.md", "GraphHandshakeManualArtifactIndexPresent"),
        Dynamic("v3.23", DataAgentV3EvidenceKind.OperatorArtifact, "Audit bundle", "docs/dataagent/dataagent-v3.23-manual-audit-bundle.md", "GraphHandshakeManualAuditBundlePresent"),
        Dynamic("v3.24", "Agent advisory contract", "docs/dataagent/dataagent-v3.24-agent-advisory-contract.md", "GraphHandshakeAgentAdvisoryContractPresent"),
        Dynamic("v3.25", "Manual shadow provider", "docs/dataagent/dataagent-v3.25-real-langgraph-manual-shadow-provider.md", "GraphHandshakeRealLangGraphManualShadowProviderPresent"),
        Dynamic("v3.26", "Replay diff gate", "docs/dataagent/dataagent-v3.26-harness-replay-diff-gate.md", "GraphHandshakeHarnessReplayDiffGatePresent"),
        Dynamic("v3.27", DataAgentV3EvidenceKind.OperatorArtifact, "Operator evidence pack", "docs/dataagent/dataagent-v3.27-operator-evidence-pack.md", "GraphHandshakeOperatorEvidencePackPresent"),
        GateOnly("v3.28", DataAgentV3EvidenceKind.FinalFreeze, "Final freeze", "docs/dataagent/dataagent-v3.28-final-readiness-freeze.md", "final freeze output")
    ];

    public static DataAgentV3LedgerParseResult ParseLedger(string ledger)
    {
        if (ledger is null)
        {
            return new([], [], ["Ledger document is missing."]);
        }

        string[] lines = ledger.Split('\n').Select(line => line.TrimEnd('\r')).ToArray();
        List<string> errors = [];
        List<string> milestones = [];
        List<DataAgentV3LedgerEntry> entries = [];

        int[] starts = FindLines(lines, InventoryStart);
        int[] ends = FindLines(lines, InventoryEnd);
        if (starts.Length != 1) errors.Add("The milestone inventory must have exactly one start delimiter.");
        if (ends.Length != 1) errors.Add("The milestone inventory must have exactly one end delimiter.");

        if (starts.Length == 1 && ends.Length == 1)
        {
            int start = starts[0];
            int end = ends[0];
            if (end <= start)
            {
                errors.Add("The milestone inventory delimiters are out of order.");
            }
            else
            {
                for (int index = start + 1; index < end; index++)
                {
                    if (!Regex.IsMatch(lines[index], MilestonePattern, RegexOptions.CultureInvariant))
                    {
                        errors.Add($"Milestone inventory line {index + 1} is malformed.");
                        continue;
                    }
                    milestones.Add(lines[index]["milestone=".Length..]);
                }

                for (int index = 0; index < lines.Length; index++)
                {
                    if ((index < start || index > end) && Regex.IsMatch(lines[index], MilestonePattern, RegexOptions.CultureInvariant))
                    {
                        errors.Add($"Milestone marker at line {index + 1} is outside the inventory.");
                    }
                }
            }
        }

        ValidateVersionInventory(milestones, "milestone inventory", errors);

        int[] headers = FindLines(lines, EvidenceTableHeader);
        if (headers.Length != 1)
        {
            errors.Add("The closure evidence table must have exactly one exact header.");
        }
        else
        {
            int header = headers[0];
            if (header + 1 >= lines.Length || lines[header + 1] != EvidenceTableSeparator)
            {
                errors.Add("The closure evidence table separator is missing or malformed.");
            }
            else
            {
                for (int index = header + 2; index < lines.Length; index++)
                {
                    string line = lines[index].Trim();
                    if (line.Length == 0) break;
                    DataAgentV3LedgerEntry? entry = ParseEvidenceRow(line, index + 1, errors);
                    if (entry is not null) entries.Add(entry);
                }
            }
        }

        ValidateVersionInventory(entries.Select(entry => entry.Version).ToArray(), "evidence table", errors);
        return new(milestones.ToArray(), entries.ToArray(), errors.ToArray());
    }

    static DataAgentV3MilestoneEvidence Dynamic(string version, string purpose, string path, string check) =>
        Dynamic(version, DataAgentV3EvidenceKind.DynamicReadiness, purpose, path, check);

    static DataAgentV3MilestoneEvidence Dynamic(string version, DataAgentV3EvidenceKind kind, string purpose, string path, string check) =>
        new(version, kind, purpose, path, check, [check], [], false, false);

    static DataAgentV3MilestoneEvidence Static(string version, string purpose, string path, string check) =>
        new(version, DataAgentV3EvidenceKind.StaticReadiness, purpose, path, check, [], [check], false, false);

    static DataAgentV3MilestoneEvidence GateOnly(string version, DataAgentV3EvidenceKind kind, string purpose, string path, string gate) =>
        new(version, kind, purpose, path, gate, [], [], false, false);

    static int[] FindLines(string[] lines, string value) => lines
        .Select((line, index) => (line, index))
        .Where(item => item.line == value)
        .Select(item => item.index)
        .ToArray();

    static DataAgentV3LedgerEntry? ParseEvidenceRow(string line, int lineNumber, List<string> errors)
    {
        string[] columns = line.Split('|');
        if (columns.Length != 9 || columns[0].Length != 0 || columns[^1].Length != 0)
        {
            errors.Add($"Evidence row {lineNumber} must contain exactly seven logical fields.");
            return null;
        }

        string[] values = new string[7];
        for (int index = 0; index < values.Length; index++)
        {
            string? value = UnwrapCodeSpan(columns[index + 1].Trim());
            if (value is null)
            {
                errors.Add($"Evidence row {lineNumber}, field {index + 1} has a malformed code span.");
                return null;
            }
            values[index] = value;
        }

        if (!Enum.TryParse(values[1], false, out DataAgentV3EvidenceKind kind) ||
            !Enum.IsDefined(kind) || int.TryParse(values[1], out _))
        {
            errors.Add($"Evidence row {lineNumber} has an invalid evidence kind.");
            return null;
        }
        if (values[5] != RuntimeBoundary)
        {
            errors.Add($"Evidence row {lineNumber} changes the runtime boundary.");
            return null;
        }
        if (values[6] != AuthorityBoundary)
        {
            errors.Add($"Evidence row {lineNumber} changes the authority boundary.");
            return null;
        }

        return new(values[0], kind, values[2], values[3], values[4], false, false);
    }

    static string? UnwrapCodeSpan(string value)
    {
        int backticks = value.Count(character => character == '`');
        if (backticks == 0) return value;
        if (backticks != 2 || value.Length < 3 || value[0] != '`' || value[^1] != '`') return null;
        return value[1..^1];
    }

    static void ValidateVersionInventory(IReadOnlyList<string> versions, string source, List<string> errors)
    {
        string[] missing = ExpectedVersions.Except(versions, StringComparer.Ordinal).ToArray();
        string[] unexpected = versions.Except(ExpectedVersions, StringComparer.Ordinal).Distinct(StringComparer.Ordinal).ToArray();
        string[] duplicates = versions.GroupBy(version => version, StringComparer.Ordinal).Where(group => group.Count() > 1).Select(group => group.Key).ToArray();
        if (missing.Length != 0) errors.Add($"The {source} is missing expected milestone versions.");
        if (unexpected.Length != 0) errors.Add($"The {source} contains unexpected milestone versions.");
        if (duplicates.Length != 0) errors.Add($"The {source} contains duplicate milestone versions.");
        if (!versions.SequenceEqual(ExpectedVersions, StringComparer.Ordinal)) errors.Add($"The {source} is not in exact V3.0-V3.28 order.");
    }
}

public static class DataAgentV3ClosureValidator
{
    const string OperatorCheckName = "GraphHandshakeOperatorEvidencePackPresent";

    public static DataAgentV3ClosureResult Validate(
        IEnumerable<DataAgentV3MilestoneEvidence> manifest,
        IEnumerable<DataAgentReadinessCheck> dynamicChecks,
        DataAgentV3LedgerParseResult ledger,
        IEnumerable<string> staticCheckNames,
        IReadOnlySet<string> existingEvidencePaths,
        int staticRequiredCount)
    {
        DataAgentV3MilestoneEvidence[] manifestEntries = manifest.ToArray();
        DataAgentReadinessCheck[] dynamicEntries = dynamicChecks.ToArray();
        string[] staticNames = staticCheckNames.ToArray();
        string[] expected = DataAgentV3ClosureManifest.ExpectedVersions.ToArray();
        string[][] milestoneSources =
        [
            manifestEntries.Select(entry => entry.Version).ToArray(),
            ledger.MilestoneVersions.ToArray(),
            ledger.Entries.Select(entry => entry.Version).ToArray()
        ];

        string[] missingMilestones = milestoneSources
            .SelectMany(source => expected.Except(source, StringComparer.Ordinal))
            .Distinct(StringComparer.Ordinal).ToArray();
        string[] duplicateMilestones = milestoneSources
            .SelectMany(source => source.GroupBy(version => version, StringComparer.Ordinal).Where(group => group.Count() > 1).Select(group => group.Key))
            .Distinct(StringComparer.Ordinal).ToArray();
        string[] unexpectedMilestones = milestoneSources
            .SelectMany(source => source.Except(expected, StringComparer.Ordinal))
            .Distinct(StringComparer.Ordinal).ToArray();

        string[] missingPaths = manifestEntries.Select(entry => entry.EvidencePath)
            .Where(path => !existingEvidencePaths.Contains(path)).Distinct(StringComparer.Ordinal).ToArray();
        string[] requiredDynamic = manifestEntries.SelectMany(entry => entry.RequiredDynamicCheckNames).Distinct(StringComparer.Ordinal).ToArray();
        string[] requiredStatic = manifestEntries.SelectMany(entry => entry.RequiredStaticCheckNames).Distinct(StringComparer.Ordinal).ToArray();
        string[] dynamicNames = dynamicEntries.Select(check => check.Name).ToArray();

        string[] missingRequired = requiredDynamic.Except(dynamicNames, StringComparer.Ordinal)
            .Concat(requiredStatic.Except(staticNames, StringComparer.Ordinal)).Distinct(StringComparer.Ordinal).ToArray();
        string[] failedRequired = requiredDynamic.Where(name =>
                dynamicEntries.Where(check => check.Name == name).Any(check => !check.Passed))
            .Distinct(StringComparer.Ordinal).ToArray();
        string[] duplicateRequired = DuplicateRequiredNames(dynamicNames, requiredDynamic)
            .Concat(DuplicateRequiredNames(staticNames, requiredStatic)).Distinct(StringComparer.Ordinal).ToArray();
        string[] unexpectedV4 = dynamicNames.Concat(staticNames)
            .Where(DataAgentV3ClosureManifest.V4OnlyCheckNames.Contains).Distinct(StringComparer.Ordinal).ToArray();

        List<string> parityMismatches = [];
        foreach (string version in expected)
        {
            DataAgentV3MilestoneEvidence[] manifestMatches = manifestEntries.Where(entry => entry.Version == version).ToArray();
            DataAgentV3LedgerEntry[] ledgerMatches = ledger.Entries.Where(entry => entry.Version == version).ToArray();
            if (manifestMatches.Length != 1 || ledgerMatches.Length != 1)
            {
                parityMismatches.Add($"{version}:{nameof(DataAgentV3LedgerEntry.Version)}");
                continue;
            }
            AddParityMismatches(manifestMatches[0], ledgerMatches[0], parityMismatches);
        }

        int authorityExpansionCount = manifestEntries.Count(entry => entry.ChangesDefaultRuntime || entry.GrantsSidecarAuthority);
        DataAgentReadinessCheck[] operatorChecks = dynamicEntries.Where(check => check.Name == OperatorCheckName).ToArray();
        bool operatorPackPresent = operatorChecks.Length == 1 && operatorChecks[0].Passed &&
            operatorChecks[0].Detail.Contains("operator_evidence_pack=true", StringComparison.Ordinal) &&
            operatorChecks[0].Detail.Contains("operator_decides=true", StringComparison.Ordinal);
        bool staticCountMatches = staticRequiredCount == DataAgentV3ClosureManifest.ExpectedFrozenStaticRequiredCount;
        bool coreCountMatches = dynamicEntries.Length == DataAgentV3ClosureManifest.ExpectedFrozenCoreCount;

        bool accepted = missingMilestones.Length == 0 && duplicateMilestones.Length == 0 && unexpectedMilestones.Length == 0 &&
            missingPaths.Length == 0 && missingRequired.Length == 0 && failedRequired.Length == 0 && duplicateRequired.Length == 0 &&
            unexpectedV4.Length == 0 && ledger.Errors.Count == 0 && parityMismatches.Count == 0 && authorityExpansionCount == 0 &&
            operatorPackPresent && staticCountMatches && coreCountMatches;

        return new(
            accepted,
            staticRequiredCount,
            dynamicEntries.Length,
            missingMilestones,
            duplicateMilestones,
            unexpectedMilestones,
            missingPaths,
            missingRequired,
            failedRequired,
            duplicateRequired,
            unexpectedV4,
            ledger.Errors.ToArray(),
            parityMismatches.ToArray(),
            authorityExpansionCount,
            operatorPackPresent,
            staticCountMatches,
            coreCountMatches);
    }

    static IEnumerable<string> DuplicateRequiredNames(IEnumerable<string> actualNames, IReadOnlyCollection<string> requiredNames) =>
        actualNames.GroupBy(name => name, StringComparer.Ordinal)
            .Where(group => group.Count() > 1 && requiredNames.Contains(group.Key, StringComparer.Ordinal))
            .Select(group => group.Key);

    static void AddParityMismatches(DataAgentV3MilestoneEvidence manifest, DataAgentV3LedgerEntry ledger, List<string> mismatches)
    {
        AddMismatch(manifest.Version == ledger.Version, ledger.Version, nameof(DataAgentV3LedgerEntry.Version), mismatches);
        AddMismatch(manifest.EvidenceKind == ledger.EvidenceKind, ledger.Version, nameof(DataAgentV3LedgerEntry.EvidenceKind), mismatches);
        AddMismatch(manifest.Purpose == ledger.Purpose, ledger.Version, nameof(DataAgentV3LedgerEntry.Purpose), mismatches);
        AddMismatch(manifest.EvidencePath == ledger.EvidencePath, ledger.Version, nameof(DataAgentV3LedgerEntry.EvidencePath), mismatches);
        AddMismatch(manifest.RequiredGateLabel == ledger.RequiredGateLabel, ledger.Version, nameof(DataAgentV3LedgerEntry.RequiredGateLabel), mismatches);
        AddMismatch(manifest.ChangesDefaultRuntime == ledger.ChangesDefaultRuntime, ledger.Version, nameof(DataAgentV3LedgerEntry.ChangesDefaultRuntime), mismatches);
        AddMismatch(manifest.GrantsSidecarAuthority == ledger.GrantsSidecarAuthority, ledger.Version, nameof(DataAgentV3LedgerEntry.GrantsSidecarAuthority), mismatches);
    }

    static void AddMismatch(bool matches, string version, string field, List<string> mismatches)
    {
        if (!matches) mismatches.Add($"{version}:{field}");
    }
}
