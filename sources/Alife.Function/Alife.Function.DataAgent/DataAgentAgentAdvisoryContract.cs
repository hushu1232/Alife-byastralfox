namespace Alife.Function.DataAgent;

public sealed record DataAgentAgentAdvisoryRequest(
    string ContractVersion,
    string RunId,
    string Task,
    string CurrentState,
    IReadOnlyList<string> AllowedAdvisoryActions,
    IReadOnlyList<string> ForbiddenAuthorities,
    string LastSuccessfulStep,
    string FailureCategory,
    IReadOnlyList<string> EvidenceRefs,
    string ArtifactIndexToken,
    string ExpectedResponseSchema,
    bool AgentAdvisoryOnly,
    bool HarnessExecutionAuthority,
    bool CSharpValidationAuthority,
    bool DefaultResultChanged);

public sealed record DataAgentAgentAdvisoryResponse(
    string AdvisoryId,
    string Summary,
    string ReasonCode,
    double Confidence,
    IReadOnlyList<string> EvidenceRefs,
    IReadOnlyList<string> ProposedNextSteps,
    IReadOnlyList<string> ForbiddenAuthorityClaims,
    bool RequiresOperatorAction,
    bool RequestsExecution,
    bool RequestsStateWrite,
    bool RequestsVisibleText,
    bool DefaultResultChanged);

public sealed record DataAgentAgentAdvisoryValidationResult(
    bool Accepted,
    string ReasonCode);

public static class DataAgentAgentAdvisoryContract
{
    public const string ContractVersionMarker = "contract_version=v3.24";

    const int MaxTokenLength = 128;
    const int MaxTextLength = 512;
    const int MaxSchemaLength = 512;
    const int MaxListItems = 12;
    const string AcceptedReasonCode = "agent_advisory_contract_accepted";

    public static DataAgentAgentAdvisoryValidationResult ValidateRequest(
        DataAgentAgentAdvisoryRequest? request)
    {
        if (request is null)
            return Reject("advisory_request_missing");

        if (string.Equals(request.ContractVersion, "v3.24", StringComparison.Ordinal) == false)
            return Reject("advisory_contract_version_mismatch");

        if (request.AgentAdvisoryOnly == false ||
            request.HarnessExecutionAuthority == false ||
            request.CSharpValidationAuthority == false)
        {
            return Reject("advisory_boundary_missing");
        }

        if (request.DefaultResultChanged)
            return Reject("advisory_default_result_changed");

        if (HasSafeToken(request.RunId) == false ||
            HasSafeText(request.Task, MaxTextLength) == false ||
            HasSafeText(request.CurrentState, MaxTextLength) == false ||
            HasSafeToken(request.LastSuccessfulStep) == false ||
            HasSafeToken(request.FailureCategory) == false ||
            HasSafeToken(request.ArtifactIndexToken) == false ||
            HasSafeSchema(request.ExpectedResponseSchema) == false)
        {
            return Reject("advisory_request_invalid_field");
        }

        if (HasSafeTokenList(request.AllowedAdvisoryActions) == false ||
            HasSafeTokenList(request.ForbiddenAuthorities) == false ||
            HasSafeEvidenceList(request.EvidenceRefs) == false)
        {
            return Reject("advisory_request_invalid_list");
        }

        return Accept();
    }

    public static DataAgentAgentAdvisoryValidationResult ValidateResponse(
        DataAgentAgentAdvisoryRequest? request,
        DataAgentAgentAdvisoryResponse? response)
    {
        DataAgentAgentAdvisoryValidationResult requestValidation = ValidateRequest(request);
        if (requestValidation.Accepted == false)
            return requestValidation;

        if (response is null)
            return Reject("advisory_response_missing");

        if (response.DefaultResultChanged)
            return Reject("advisory_default_result_changed");

        if (response.RequestsExecution ||
            response.RequestsStateWrite ||
            response.RequestsVisibleText ||
            response.ForbiddenAuthorityClaims is null ||
            response.ForbiddenAuthorityClaims.Count > 0)
        {
            return Reject("advisory_forbidden_authority_claimed");
        }

        if (HasSafeToken(response.AdvisoryId) == false ||
            HasSafeText(response.Summary, MaxTextLength) == false ||
            HasSafeToken(response.ReasonCode) == false ||
            response.Confidence is < 0 or > 1 ||
            HasSafeEvidenceList(response.EvidenceRefs) == false ||
            HasSafeTokenList(response.ProposedNextSteps) == false)
        {
            if (ContainsUnsafeResponseText(response))
                return Reject("advisory_unsafe_text");

            return Reject("advisory_response_invalid_field");
        }

        if (response.EvidenceRefs.Count == 0)
            return Reject("advisory_missing_evidence");

        if (response.EvidenceRefs.All(item => request!.EvidenceRefs.Contains(item, StringComparer.Ordinal)) == false)
            return Reject("advisory_unknown_evidence_ref");

        return Accept();
    }

    static DataAgentAgentAdvisoryValidationResult Accept()
    {
        return new DataAgentAgentAdvisoryValidationResult(true, AcceptedReasonCode);
    }

    static DataAgentAgentAdvisoryValidationResult Reject(string reasonCode)
    {
        return new DataAgentAgentAdvisoryValidationResult(false, reasonCode);
    }

    static bool HasSafeTokenList(IReadOnlyList<string>? values)
    {
        if (values is null ||
            values.Count == 0 ||
            values.Count > MaxListItems)
        {
            return false;
        }

        return values.All(HasSafeToken);
    }

    static bool HasSafeEvidenceList(IReadOnlyList<string>? values)
    {
        if (values is null ||
            values.Count > MaxListItems)
        {
            return false;
        }

        return values.All(HasSafeEvidenceToken);
    }

    static bool HasSafeSchema(string? value)
    {
        return HasSafeText(value, MaxSchemaLength) &&
               value!.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).All(HasSafeToken);
    }

    static bool HasSafeText(string? value, int maxLength)
    {
        return string.IsNullOrWhiteSpace(value) == false &&
               value.Length <= maxLength &&
               DataAgentGraphHandshakeUnsafeDiagnosticDetector.ContainsUnsafeText(value) == false;
    }

    static bool HasSafeEvidenceToken(string? value)
    {
        return HasSafeToken(value, allowColon: true);
    }

    static bool HasSafeToken(string? value)
    {
        return HasSafeToken(value, allowColon: false);
    }

    static bool HasSafeToken(string? value, bool allowColon)
    {
        if (string.IsNullOrWhiteSpace(value) || value.Length > MaxTokenLength)
            return false;

        if (DataAgentGraphHandshakeUnsafeDiagnosticDetector.ContainsUnsafeText(value))
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

            if (allowColon && current == ':')
                continue;

            return false;
        }

        return true;
    }

    static bool ContainsUnsafeResponseText(DataAgentAgentAdvisoryResponse response)
    {
        return DataAgentGraphHandshakeUnsafeDiagnosticDetector.ContainsUnsafeText(response.AdvisoryId) ||
               DataAgentGraphHandshakeUnsafeDiagnosticDetector.ContainsUnsafeText(response.Summary) ||
               DataAgentGraphHandshakeUnsafeDiagnosticDetector.ContainsUnsafeText(response.ReasonCode) ||
               response.EvidenceRefs.Any(DataAgentGraphHandshakeUnsafeDiagnosticDetector.ContainsUnsafeText) ||
               response.ProposedNextSteps.Any(DataAgentGraphHandshakeUnsafeDiagnosticDetector.ContainsUnsafeText) ||
               response.ForbiddenAuthorityClaims.Any(DataAgentGraphHandshakeUnsafeDiagnosticDetector.ContainsUnsafeText);
    }
}

public static class DataAgentAgentAdvisoryFormatter
{
    public static string Format(
        DataAgentAgentAdvisoryRequest request,
        DataAgentAgentAdvisoryResponse response)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(response);

        List<string> lines =
        [
            "agent_advisory_contract=true",
            $"contract_version={SafeToken(request.ContractVersion)}",
            "token_budget_context_layers=true",
            "evidence_first_response=true",
            $"agent_advisory_only={LowerBool(request.AgentAdvisoryOnly)}",
            $"harness_execution_authority={LowerBool(request.HarnessExecutionAuthority)}",
            $"csharp_validation_authority={LowerBool(request.CSharpValidationAuthority)}",
            $"default_result_changed={LowerBool(request.DefaultResultChanged || response.DefaultResultChanged)}",
            $"run_id={SafeToken(request.RunId)}",
            $"failure_category={SafeToken(request.FailureCategory)}",
            $"last_successful_step={SafeToken(request.LastSuccessfulStep)}",
            $"artifact_index_token={SafeToken(request.ArtifactIndexToken)}",
            $"advisory_id={SafeToken(response.AdvisoryId)}",
            $"reason_code={SafeToken(response.ReasonCode)}",
            $"confidence={response.Confidence:0.##}",
            $"requires_operator_action={LowerBool(response.RequiresOperatorAction)}"
        ];

        foreach (string evidenceRef in response.EvidenceRefs.Take(4))
            lines.Add($"evidence_ref={SafeToken(evidenceRef, allowColon: true)}");

        foreach (string nextStep in response.ProposedNextSteps.Take(4))
            lines.Add($"proposed_next_step={SafeToken(nextStep)}");

        return string.Join(Environment.NewLine, lines) + Environment.NewLine;
    }

    static string LowerBool(bool value) => value ? "true" : "false";

    static string SafeToken(string? value, bool allowColon = false)
    {
        if (string.IsNullOrWhiteSpace(value) ||
            value.Length > 128 ||
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

            if (allowColon && current == ':')
                continue;

            return "redacted";
        }

        return value;
    }
}
