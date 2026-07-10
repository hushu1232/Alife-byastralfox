namespace Alife.Function.DataAgent;

public sealed record DataAgentRealLangGraphManualShadowContextBudgetOptions(
    int MaxEnvelopeChars,
    int MaxLayerChars,
    IReadOnlyList<string> RequiredLayerNames);

public sealed record DataAgentRealLangGraphManualShadowBudgetedContextLayer(
    string Name,
    string Text,
    int OriginalChars,
    int IncludedChars,
    bool Truncated);

public sealed record DataAgentRealLangGraphManualShadowContextEnvelope(
    bool Accepted,
    string ReasonCode,
    int MaxEnvelopeChars,
    int MaxLayerChars,
    int TotalIncludedChars,
    int LayerCount,
    IReadOnlyList<DataAgentRealLangGraphManualShadowBudgetedContextLayer> Layers,
    bool DefaultResultChanged,
    bool StoresSecrets,
    bool StoresSql,
    bool StoresHiddenContext,
    IReadOnlyList<string> ReasonCodes);

public static class DataAgentRealLangGraphManualShadowContextBudgetBuilder
{
    public static DataAgentRealLangGraphManualShadowContextEnvelope Build(
        IReadOnlyList<DataAgentRealLangGraphManualShadowContextLayer>? layers,
        DataAgentRealLangGraphManualShadowContextBudgetOptions options)
    {
        if (options.MaxEnvelopeChars <= 0 || options.MaxLayerChars <= 0)
            return Rejected("manual_shadow_context_budget_invalid", options);

        if (layers is null || layers.Count == 0)
            return Rejected("manual_shadow_context_layers_missing", options);

        if (options.RequiredLayerNames is null || options.RequiredLayerNames.Count == 0)
            return Rejected("manual_shadow_context_required_layers_missing", options);

        List<DataAgentRealLangGraphManualShadowBudgetedContextLayer> included = [];
        int remaining = options.MaxEnvelopeChars;

        foreach (string requiredName in options.RequiredLayerNames)
        {
            DataAgentRealLangGraphManualShadowContextLayer? layer = layers.FirstOrDefault(candidate =>
                string.Equals(candidate.Name, requiredName, StringComparison.Ordinal));
            if (layer is null)
                return Rejected("manual_shadow_context_required_layer_missing", options);

            if (ContainsUnsafe(layer.Name) || ContainsUnsafe(layer.Text))
                return Rejected("manual_shadow_context_unsafe_text", options);

            string text = layer.Text ?? string.Empty;
            int originalChars = text.Length;
            int allowedForLayer = Math.Min(options.MaxLayerChars, Math.Max(0, remaining));
            string bounded = text.Length <= allowedForLayer ? text : text[..allowedForLayer];
            bool truncated = bounded.Length < text.Length;
            remaining -= bounded.Length;

            included.Add(new DataAgentRealLangGraphManualShadowBudgetedContextLayer(
                SafeToken(layer.Name),
                bounded,
                originalChars,
                bounded.Length,
                truncated));
        }

        return new DataAgentRealLangGraphManualShadowContextEnvelope(
            true,
            "manual_shadow_context_budget_ready",
            options.MaxEnvelopeChars,
            options.MaxLayerChars,
            included.Sum(layer => layer.IncludedChars),
            included.Count,
            included,
            DefaultResultChanged: false,
            StoresSecrets: false,
            StoresSql: false,
            StoresHiddenContext: false,
            ReasonCodes: ["manual_shadow_context_budget_ready"]);
    }

    static DataAgentRealLangGraphManualShadowContextEnvelope Rejected(
        string reasonCode,
        DataAgentRealLangGraphManualShadowContextBudgetOptions options) =>
        new(
            false,
            reasonCode,
            Math.Max(0, options.MaxEnvelopeChars),
            Math.Max(0, options.MaxLayerChars),
            0,
            0,
            [],
            DefaultResultChanged: false,
            StoresSecrets: false,
            StoresSql: false,
            StoresHiddenContext: false,
            ReasonCodes: [reasonCode]);

    static string SafeToken(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return "redacted";

        string trimmed = value.Trim();
        return ContainsUnsafe(trimmed)
            ? "redacted"
            : trimmed;
    }

    static bool ContainsUnsafe(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return false;

        return DataAgentGraphHandshakeUnsafeDiagnosticDetector.ContainsUnsafeText(value) ||
               value.Contains("hidden_context", StringComparison.OrdinalIgnoreCase);
    }
}

public static class DataAgentRealLangGraphManualShadowContextBudgetFormatter
{
    public static string Format(DataAgentRealLangGraphManualShadowContextEnvelope envelope)
    {
        string[] lines =
        [
            "[dataagent_v4_1_context_budget]",
            "manual_shadow_context_budget=true",
            $"accepted={LowerBool(envelope.Accepted)}",
            $"reason_code={SafeToken(envelope.ReasonCode)}",
            $"max_envelope_chars={envelope.MaxEnvelopeChars}",
            $"max_layer_chars={envelope.MaxLayerChars}",
            $"total_included_chars={envelope.TotalIncludedChars}",
            $"layer_count={envelope.LayerCount}",
            $"default_result_changed={LowerBool(envelope.DefaultResultChanged)}",
            $"stores_secrets={LowerBool(envelope.StoresSecrets)}",
            $"stores_sql={LowerBool(envelope.StoresSql)}",
            $"stores_hidden_context={LowerBool(envelope.StoresHiddenContext)}",
            $"reason_codes={FormatReasonCodes(envelope.ReasonCodes)}",
            "[/dataagent_v4_1_context_budget]"
        ];

        return string.Join(Environment.NewLine, lines);
    }

    static string FormatReasonCodes(IReadOnlyList<string>? reasonCodes)
    {
        if (reasonCodes is null || reasonCodes.Count == 0)
            return "redacted";

        return string.Join(",", reasonCodes.Select(SafeToken));
    }

    static string SafeToken(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return "redacted";

        string trimmed = value.Trim();
        if (trimmed.Contains("hidden_context", StringComparison.OrdinalIgnoreCase) ||
            DataAgentGraphHandshakeUnsafeDiagnosticDetector.ContainsUnsafeText(trimmed))
        {
            return "redacted";
        }

        return trimmed;
    }

    static string LowerBool(bool value) => value ? "true" : "false";
}
