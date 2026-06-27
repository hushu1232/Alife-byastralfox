namespace Alife.Function.Agent;

public sealed record AgentGitHubUploadPlanResult(
    bool ReadyToRun,
    AgentExecutionGatewayDecision GatewayDecision,
    string Command,
    string Message);

public sealed class AgentGitHubUploadService(
    AgentActionAuthorizationService? authorization = null,
    string command = "powershell -ExecutionPolicy Bypass -File D:\\Alife\\tools\\upload-alife-service-via-foxd.ps1")
{
    readonly AgentActionAuthorizationService authorization = authorization ?? new AgentActionAuthorizationService();
    readonly string command = command;

    public AgentGitHubUploadPlanResult BuildUploadPlan(
        AgentPermissionRequest request,
        AgentPermissionConfig config)
    {
        AgentExecutionGatewayDecision decision = authorization.EvaluateExecution(request with
        {
            RiskLevel = AgentRiskLevel.High,
            Action = string.IsNullOrWhiteSpace(request.Action) ? "github.upload" : request.Action.Trim()
        }, config);

        if (decision.AllowedNow == false)
        {
            string prefix = decision.Status == AgentExecutionDecisionStatus.OwnerConfirmationRequired
                ? "Owner confirmation required"
                : "Blocked";
            return new AgentGitHubUploadPlanResult(
                ReadyToRun: false,
                decision,
                command,
                $"{prefix}: {decision.Reason}");
        }

        return new AgentGitHubUploadPlanResult(
            ReadyToRun: true,
            decision,
            command,
            "GitHub upload command is authorized but has not been executed.");
    }
}
