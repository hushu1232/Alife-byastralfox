namespace Alife.Function.DataAgent;

public sealed record DataAgentLangGraphManualShadowPayload(
    string ProviderName,
    bool CapturedByOperator,
    bool RuntimeStartedByAlife,
    bool DependenciesInstalledByAlife,
    bool SidecarCalledByAlife,
    DataAgentAgentAdvisoryResponse Advisory);

public sealed record DataAgentLangGraphManualShadowResult(
    bool Accepted,
    string ReasonCode,
    string ProviderName,
    bool ManualShadowOnly,
    bool StartsRuntime,
    bool InstallsDependencies,
    bool CallsSidecar,
    bool FallbackRequired,
    bool DefaultResultChanged,
    bool StoresSecrets,
    bool StoresSql,
    bool StoresHiddenContext,
    DataAgentAgentAdvisoryValidationResult Validation,
    DataAgentAgentAdvisoryResponse? Advisory);

public static class DataAgentLangGraphManualShadowProvider
{
    public const string ForbiddenAuthorityClaimedReasonCode = "advisory_forbidden_authority_claimed";

    public static DataAgentLangGraphManualShadowResult Evaluate(
        DataAgentAgentAdvisoryRequest request,
        DataAgentLangGraphManualShadowPayload? payload)
    {
        ArgumentNullException.ThrowIfNull(request);

        if (payload is null)
        {
            return Reject(
                "langgraph",
                "langgraph_manual_shadow_payload_missing",
                new DataAgentAgentAdvisoryValidationResult(false, "langgraph_manual_shadow_payload_missing"),
                advisory: null);
        }

        if (IsSafeProviderToken(payload.ProviderName) == false ||
            string.Equals(payload.ProviderName, "langgraph", StringComparison.Ordinal) == false)
        {
            return Reject(
                "redacted",
                "langgraph_manual_shadow_provider_invalid",
                new DataAgentAgentAdvisoryValidationResult(false, "langgraph_manual_shadow_provider_invalid"),
                advisory: null);
        }

        if (payload.CapturedByOperator == false ||
            payload.RuntimeStartedByAlife ||
            payload.DependenciesInstalledByAlife ||
            payload.SidecarCalledByAlife)
        {
            return Reject(
                payload.ProviderName,
                "langgraph_manual_shadow_boundary_violation",
                new DataAgentAgentAdvisoryValidationResult(false, "langgraph_manual_shadow_boundary_violation"),
                advisory: null);
        }

        DataAgentAgentAdvisoryValidationResult validation =
            DataAgentAgentAdvisoryContract.ValidateResponse(request, payload.Advisory);
        if (validation.Accepted == false)
            return Reject(payload.ProviderName, validation.ReasonCode, validation, advisory: null);

        return new DataAgentLangGraphManualShadowResult(
            Accepted: true,
            ReasonCode: "langgraph_manual_shadow_advisory_accepted",
            ProviderName: payload.ProviderName,
            ManualShadowOnly: true,
            StartsRuntime: false,
            InstallsDependencies: false,
            CallsSidecar: false,
            FallbackRequired: false,
            DefaultResultChanged: false,
            StoresSecrets: false,
            StoresSql: false,
            StoresHiddenContext: false,
            validation,
            payload.Advisory);
    }

    static DataAgentLangGraphManualShadowResult Reject(
        string providerName,
        string reasonCode,
        DataAgentAgentAdvisoryValidationResult validation,
        DataAgentAgentAdvisoryResponse? advisory)
    {
        return new DataAgentLangGraphManualShadowResult(
            Accepted: false,
            reasonCode,
            providerName,
            ManualShadowOnly: true,
            StartsRuntime: false,
            InstallsDependencies: false,
            CallsSidecar: false,
            FallbackRequired: true,
            DefaultResultChanged: false,
            StoresSecrets: false,
            StoresSql: false,
            StoresHiddenContext: false,
            validation,
            advisory);
    }

    static bool IsSafeProviderToken(string? value)
    {
        if (string.IsNullOrWhiteSpace(value) ||
            value.Length > DataAgentGraphHandshakeLimits.MaxReasonCodeLength ||
            DataAgentGraphHandshakeUnsafeDiagnosticDetector.ContainsUnsafeText(value))
        {
            return false;
        }

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

public static class DataAgentLangGraphManualShadowFormatter
{
    public static string Format(DataAgentLangGraphManualShadowResult result)
    {
        ArgumentNullException.ThrowIfNull(result);

        return string.Join(
            Environment.NewLine,
            "real_langgraph_manual_shadow_provider=true",
            "langgraph_provider_only=true",
            $"manual_shadow_only={LowerBool(result.ManualShadowOnly)}",
            "agent_advisory_contract=v3.24",
            $"accepted={LowerBool(result.Accepted)}",
            $"reason_code={SafeToken(result.ReasonCode)}",
            $"provider_name={SafeToken(result.ProviderName)}",
            $"fallback_required={LowerBool(result.FallbackRequired)}",
            $"starts_runtime={LowerBool(result.StartsRuntime)}",
            $"installs_dependencies={LowerBool(result.InstallsDependencies)}",
            $"calls_sidecar={LowerBool(result.CallsSidecar)}",
            $"default_result_changed={LowerBool(result.DefaultResultChanged)}",
            $"stores_secrets={LowerBool(result.StoresSecrets)}",
            $"stores_sql={LowerBool(result.StoresSql)}",
            $"stores_hidden_context={LowerBool(result.StoresHiddenContext)}",
            string.Empty);
    }

    static string LowerBool(bool value) => value ? "true" : "false";

    static string SafeToken(string? value)
    {
        if (string.IsNullOrWhiteSpace(value) ||
            value.Length > DataAgentGraphHandshakeLimits.MaxReasonCodeLength ||
            DataAgentGraphHandshakeUnsafeDiagnosticDetector.ContainsUnsafeText(value))
        {
            return "redacted";
        }

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

            return "redacted";
        }

        return value;
    }
}
