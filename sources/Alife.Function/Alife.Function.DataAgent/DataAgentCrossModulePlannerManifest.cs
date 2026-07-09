namespace Alife.Function.DataAgent;

public sealed record DataAgentCrossModulePlannerManifest(
    string CapabilityName,
    string ModuleName,
    bool PlannerOnly,
    bool AllowsExecution,
    bool AllowsStateWrite,
    bool AllowsVisibleText,
    IReadOnlyList<string> AllowedAdvisoryActions,
    IReadOnlyList<string> DeniedCapabilityMarkers,
    string SafetyNotes);

public sealed record DataAgentCrossModulePlannerManifestValidationResult(
    bool Accepted,
    string ReasonCode);

public static class DataAgentCrossModulePlannerManifestFactory
{
    public static IReadOnlyList<DataAgentCrossModulePlannerManifest> CreateDefault()
    {
        return
        [
            Create("qchat.intent_hint", "qchat", "intent_hint"),
            Create("memory.candidate_summary", "memory", "summarize"),
            Create("browser.task_plan", "browser", "plan"),
            Create("desktop.task_plan", "desktop", "plan"),
            Create("emotion.expression_hint", "emotion", "hint"),
            Create("deskpet.expression_hint", "deskpet", "hint")
        ];
    }

    static DataAgentCrossModulePlannerManifest Create(
        string capabilityName,
        string moduleName,
        string advisoryAction)
    {
        return new DataAgentCrossModulePlannerManifest(
            capabilityName,
            moduleName,
            PlannerOnly: true,
            AllowsExecution: false,
            AllowsStateWrite: false,
            AllowsVisibleText: false,
            AllowedAdvisoryActions: [advisoryAction],
            DeniedCapabilityMarkers: DataAgentCrossModulePlannerManifestValidator.RequiredDeniedCapabilityMarkers,
            SafetyNotes: "planner_only_no_execution_no_write_no_visible_text");
    }
}

public static class DataAgentCrossModulePlannerManifestValidator
{
    public static IReadOnlyList<string> RequiredDeniedCapabilityMarkers { get; } =
    [
        "qchat.send",
        "qq.ingress",
        "tool.execute",
        "sql.execute",
        "checkpoint.write",
        "memory.write",
        "browser.execute",
        "desktop.execute",
        "file.write",
        "voice.output",
        "tts.output",
        "audit.write",
        "progress.write",
        "diagnostics.write"
    ];

    public static DataAgentCrossModulePlannerManifestValidationResult Validate(
        DataAgentCrossModulePlannerManifest? manifest)
    {
        if (manifest is null)
            return Reject("planner_manifest_missing");

        if (HasSafeToken(manifest.CapabilityName) == false ||
            HasSafeToken(manifest.ModuleName) == false)
        {
            return Reject("planner_manifest_invalid_identity");
        }

        if (manifest.PlannerOnly == false ||
            manifest.AllowsExecution ||
            manifest.AllowsStateWrite ||
            manifest.AllowsVisibleText)
        {
            return Reject("planner_manifest_authority_claimed");
        }

        if (manifest.AllowedAdvisoryActions is null ||
            manifest.AllowedAdvisoryActions.Count == 0 ||
            manifest.AllowedAdvisoryActions.Any(HasSafeToken) == false)
        {
            return Reject("planner_manifest_invalid_advisory_action");
        }

        if (manifest.DeniedCapabilityMarkers is null ||
            RequiredDeniedCapabilityMarkers.All(required =>
                manifest.DeniedCapabilityMarkers.Contains(required, StringComparer.Ordinal)) == false)
        {
            return Reject("planner_manifest_missing_denied_marker");
        }

        if (string.IsNullOrWhiteSpace(manifest.SafetyNotes) ||
            DataAgentGraphHandshakeUnsafeDiagnosticDetector.ContainsUnsafeText(manifest.SafetyNotes))
        {
            return Reject("planner_manifest_invalid_safety_notes");
        }

        return new DataAgentCrossModulePlannerManifestValidationResult(true, "planner_manifest_accepted");
    }

    static DataAgentCrossModulePlannerManifestValidationResult Reject(string reasonCode)
    {
        return new DataAgentCrossModulePlannerManifestValidationResult(false, reasonCode);
    }

    static bool HasSafeToken(string? value)
    {
        if (string.IsNullOrWhiteSpace(value) || value.Length > 128)
            return false;

        foreach (char current in value)
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

            return false;
        }

        return true;
    }
}
