namespace Alife.Function.DataAgent;

public enum DataAgentV43OperatorDisposition
{
    Adopted,
    Useful,
    Rejected,
    NotReviewed
}

public enum DataAgentV43ValueStatus
{
    ProvenUseful,
    Promising,
    Unproven,
    Rejected
}

public sealed record DataAgentV43CrossModuleValueInput(
    DataAgentV42OperatorEvidencePacket? Packet,
    IReadOnlyList<string> CapabilityNames,
    DataAgentV43OperatorDisposition OperatorDisposition,
    int ReviewBeforeMs,
    int ReviewAfterMs);

public sealed record DataAgentV43CrossModuleValueResult(
    bool Accepted,
    string ReasonCode,
    string ContractVersion,
    string SourceBaseline,
    DataAgentV43ValueStatus Status,
    DataAgentV43OperatorDisposition OperatorDisposition,
    IReadOnlyList<string> CapabilityNames,
    int PacketScore,
    int ReplayAlignmentScore,
    int ManifestScore,
    int OperatorScore,
    int ReviewTimeScore,
    int TotalScore,
    bool ProductionShadowEligible,
    bool AgentAdvisoryOnly,
    bool CSharpValidationAuthority,
    bool AllowsExecution,
    bool AllowsStateWrite,
    bool AllowsVisibleText,
    bool DefaultResultChanged,
    bool StoresSecrets,
    bool StoresSql,
    bool StoresHiddenContext,
    IReadOnlyList<string> ReasonCodes);

public static class DataAgentV43CrossModuleValueEvaluator
{
    public const int MaxCapabilities = 6;
    public const int MaxReviewMs = 3_600_000;
    public const int ProductionShadowEligibilityScore = 80;

    public static DataAgentV43CrossModuleValueResult Evaluate(DataAgentV43CrossModuleValueInput? input)
    {
        if (input?.Packet is null)
            return Reject(input?.OperatorDisposition ?? DataAgentV43OperatorDisposition.NotReviewed, "v4_3_value_input_missing");

        if (PacketAccepted(input.Packet) == false)
            return Reject(input.OperatorDisposition, "v4_3_value_packet_rejected");

        if (TimingValid(input.ReviewBeforeMs, input.ReviewAfterMs) == false)
            return Reject(input.OperatorDisposition, "v4_3_value_review_timing_invalid");

        IReadOnlyList<string>? capabilities = ValidateCapabilities(input.CapabilityNames);
        if (capabilities is null)
            return Reject(input.OperatorDisposition, "v4_3_value_manifest_rejected");

        int packetScore = 25;
        int replayScore = input.Packet.ReplayDiffGatePassed ? 25 : 0;
        int manifestScore = 20;
        int operatorScore = input.OperatorDisposition switch
        {
            DataAgentV43OperatorDisposition.Adopted => 20,
            DataAgentV43OperatorDisposition.Useful => 10,
            _ => 0
        };
        int timeScore = ReviewTimeScore(input);
        int total = packetScore + replayScore + manifestScore + operatorScore + timeScore;
        DataAgentV43ValueStatus status = Status(input.OperatorDisposition, input.Packet.ReplayDiffGatePassed, total);

        return new DataAgentV43CrossModuleValueResult(
            Accepted: true,
            ReasonCode: "v4_3_cross_module_value_scored",
            ContractVersion: "v4.3",
            SourceBaseline: "v4.2",
            status,
            input.OperatorDisposition,
            capabilities,
            packetScore,
            replayScore,
            manifestScore,
            operatorScore,
            timeScore,
            total,
            ProductionShadowEligible: status == DataAgentV43ValueStatus.ProvenUseful,
            AgentAdvisoryOnly: true,
            CSharpValidationAuthority: true,
            AllowsExecution: false,
            AllowsStateWrite: false,
            AllowsVisibleText: false,
            DefaultResultChanged: false,
            StoresSecrets: false,
            StoresSql: false,
            StoresHiddenContext: false,
            ReasonCodes: ["v4_3_cross_module_value_scored", StatusReason(status)]);
    }

    static bool PacketAccepted(DataAgentV42OperatorEvidencePacket packet) =>
        packet.Accepted &&
        packet.Status == DataAgentV42OperatorEvidenceStatus.Accepted &&
        packet.FallbackRequired == false &&
        packet.AgentAdvisoryOnly &&
        packet.CSharpValidationAuthority &&
        packet.DefaultResultChanged == false &&
        packet.StoresSecrets == false &&
        packet.StoresSql == false &&
        packet.StoresHiddenContext == false;

    static bool TimingValid(int beforeMs, int afterMs) =>
        beforeMs >= 0 && beforeMs <= MaxReviewMs &&
        afterMs >= 0 && afterMs <= MaxReviewMs &&
        afterMs <= beforeMs;

    static IReadOnlyList<string>? ValidateCapabilities(IReadOnlyList<string>? requested)
    {
        if (requested is null || requested.Count == 0 || requested.Count > MaxCapabilities)
            return null;

        if (requested.Distinct(StringComparer.Ordinal).Count() != requested.Count)
            return null;

        IReadOnlyDictionary<string, DataAgentCrossModulePlannerManifest> manifests =
            DataAgentCrossModulePlannerManifestFactory.CreateDefault()
                .ToDictionary(item => item.CapabilityName, StringComparer.Ordinal);
        List<string> accepted = [];
        foreach (string capabilityName in requested)
        {
            if (manifests.TryGetValue(capabilityName, out DataAgentCrossModulePlannerManifest? manifest) == false)
                return null;

            DataAgentCrossModulePlannerManifestValidationResult validation =
                DataAgentCrossModulePlannerManifestValidator.Validate(manifest);
            if (validation.Accepted == false ||
                manifest.PlannerOnly == false ||
                manifest.AllowsExecution ||
                manifest.AllowsStateWrite ||
                manifest.AllowsVisibleText)
            {
                return null;
            }

            accepted.Add(capabilityName);
        }

        return accepted;
    }

    static int ReviewTimeScore(DataAgentV43CrossModuleValueInput input)
    {
        if (input.OperatorDisposition is not DataAgentV43OperatorDisposition.Adopted and
            not DataAgentV43OperatorDisposition.Useful ||
            input.ReviewBeforeMs <= 0)
        {
            return 0;
        }

        int reduction = input.ReviewBeforeMs - input.ReviewAfterMs;
        return reduction * 10 / input.ReviewBeforeMs;
    }

    static DataAgentV43ValueStatus Status(
        DataAgentV43OperatorDisposition disposition,
        bool replayAligned,
        int score)
    {
        if (disposition is DataAgentV43OperatorDisposition.Rejected or DataAgentV43OperatorDisposition.NotReviewed)
            return DataAgentV43ValueStatus.Unproven;

        if (score >= ProductionShadowEligibilityScore && replayAligned)
            return DataAgentV43ValueStatus.ProvenUseful;

        return score >= 60
            ? DataAgentV43ValueStatus.Promising
            : DataAgentV43ValueStatus.Unproven;
    }

    static string StatusReason(DataAgentV43ValueStatus status) => status switch
    {
        DataAgentV43ValueStatus.ProvenUseful => "v4_3_value_proven_useful",
        DataAgentV43ValueStatus.Promising => "v4_3_value_promising",
        DataAgentV43ValueStatus.Unproven => "v4_3_value_unproven",
        _ => "v4_3_value_rejected"
    };

    static DataAgentV43CrossModuleValueResult Reject(
        DataAgentV43OperatorDisposition disposition,
        string reasonCode) =>
        new(
            Accepted: false,
            reasonCode,
            ContractVersion: "v4.3",
            SourceBaseline: "v4.2",
            Status: DataAgentV43ValueStatus.Rejected,
            disposition,
            CapabilityNames: [],
            PacketScore: 0,
            ReplayAlignmentScore: 0,
            ManifestScore: 0,
            OperatorScore: 0,
            ReviewTimeScore: 0,
            TotalScore: 0,
            ProductionShadowEligible: false,
            AgentAdvisoryOnly: true,
            CSharpValidationAuthority: true,
            AllowsExecution: false,
            AllowsStateWrite: false,
            AllowsVisibleText: false,
            DefaultResultChanged: false,
            StoresSecrets: false,
            StoresSql: false,
            StoresHiddenContext: false,
            ReasonCodes: [reasonCode]);
}

public static class DataAgentV43CrossModuleValueFormatter
{
    public static string Format(DataAgentV43CrossModuleValueResult result)
    {
        ArgumentNullException.ThrowIfNull(result);

        return string.Join(
            Environment.NewLine,
            "cross_module_value_score=v4.3",
            $"source_baseline={result.SourceBaseline}",
            $"accepted={LowerBool(result.Accepted)}",
            $"reason_code={result.ReasonCode}",
            $"status={StatusToken(result.Status)}",
            $"operator_disposition={DispositionToken(result.OperatorDisposition)}",
            $"capabilities={string.Join(',', result.CapabilityNames)}",
            $"packet_score={result.PacketScore}",
            $"replay_alignment_score={result.ReplayAlignmentScore}",
            $"manifest_score={result.ManifestScore}",
            $"operator_score={result.OperatorScore}",
            $"review_time_score={result.ReviewTimeScore}",
            $"total_score={result.TotalScore}",
            $"production_shadow_eligible={LowerBool(result.ProductionShadowEligible)}",
            $"agent_advisory_only={LowerBool(result.AgentAdvisoryOnly)}",
            $"csharp_validation_authority={LowerBool(result.CSharpValidationAuthority)}",
            $"allows_execution={LowerBool(result.AllowsExecution)}",
            $"allows_state_write={LowerBool(result.AllowsStateWrite)}",
            $"allows_visible_text={LowerBool(result.AllowsVisibleText)}",
            $"default_result_changed={LowerBool(result.DefaultResultChanged)}",
            $"stores_secrets={LowerBool(result.StoresSecrets)}",
            $"stores_sql={LowerBool(result.StoresSql)}",
            $"stores_hidden_context={LowerBool(result.StoresHiddenContext)}",
            $"reason_codes={string.Join(',', result.ReasonCodes)}");
    }

    static string StatusToken(DataAgentV43ValueStatus status) => status switch
    {
        DataAgentV43ValueStatus.ProvenUseful => "proven_useful",
        DataAgentV43ValueStatus.Promising => "promising",
        DataAgentV43ValueStatus.Unproven => "unproven",
        _ => "rejected"
    };

    static string DispositionToken(DataAgentV43OperatorDisposition disposition) => disposition switch
    {
        DataAgentV43OperatorDisposition.Adopted => "adopted",
        DataAgentV43OperatorDisposition.Useful => "useful",
        DataAgentV43OperatorDisposition.Rejected => "rejected",
        _ => "not_reviewed"
    };

    static string LowerBool(bool value) => value ? "true" : "false";
}
