using System;
using System.Linq;
using System.Threading.Tasks;
using Alife.Function.MessageFilter;

namespace Alife.Function.Agent;

public sealed record AgentActionGatewayResult<T>(
    bool Executed,
    T? Value,
    AgentExecutionGatewayDecision Decision,
    string Message,
    Exception? Exception = null,
    AgentApprovalRequest? ApprovalRequest = null);

public class AgentActionGatewayService(
    AgentAuditLogService? auditLog = null,
    AgentActionAuthorizationService? authorization = null,
    AgentApprovalService? approvalService = null)
{
    readonly AgentAuditLogService? auditLog = auditLog;
    readonly AgentActionAuthorizationService authorization = authorization ?? new AgentActionAuthorizationService();
    readonly AgentApprovalService? approvalService = approvalService;

    public async Task<AgentActionGatewayResult<T>> ExecuteAsync<T>(
        AgentPermissionRequest request,
        AgentPermissionConfig config,
        Func<Task<T>> execute,
        string detail = "")
    {
        ArgumentNullException.ThrowIfNull(config);
        ArgumentNullException.ThrowIfNull(execute);

        AgentExecutionGatewayDecision decision = authorization.EvaluateExecution(request, config);
        string action = decision.Action;
        string actor = DescribeActor(request, decision);
        string normalizedDetail = detail?.Trim() ?? string.Empty;

        if (decision.AllowedNow == false)
        {
            if (decision.Status == AgentExecutionDecisionStatus.OwnerConfirmationRequired
                && approvalService != null
                && config.OwnerUserIds.Count > 0)
            {
                long ownerUserId = config.OwnerUserIds.OrderBy(id => id).First();
                AgentApprovalRequest approval = approvalService.CreateExecutableRequest(
                    ownerUserId,
                    action,
                    ToApprovalRiskLevel(decision.RiskLevel),
                    normalizedDetail,
                    TimeSpan.FromMinutes(10),
                    async () =>
                    {
                        T value = await execute();
                        Record(action, actor, normalizedDetail, decision.RiskLevel, succeeded: true);
                        return $"Executed: {decision.Reason}";
                    });
                string approvalMessage = $"Owner confirmation required: approval #{approval.Id} for {action}. Use /approve {approval.Id} or /deny {approval.Id}.";
                Record(action, actor, normalizedDetail, decision.RiskLevel, succeeded: false, error: approvalMessage);
                return new AgentActionGatewayResult<T>(false, default, decision, approvalMessage, ApprovalRequest: approval);
            }

            string prefix = decision.Status == AgentExecutionDecisionStatus.OwnerConfirmationRequired
                ? "Owner confirmation required"
                : "Blocked";
            string message = $"{prefix}: {decision.Reason}";
            Record(action, actor, normalizedDetail, decision.RiskLevel, succeeded: false, error: message);
            return new AgentActionGatewayResult<T>(false, default, decision, message);
        }

        try
        {
            T value = await execute();
            string message = $"Executed: {decision.Reason}";
            Record(action, actor, normalizedDetail, decision.RiskLevel, succeeded: true);
            return new AgentActionGatewayResult<T>(true, value, decision, message);
        }
        catch (Exception exception)
        {
            string message = $"Failed: {exception.Message}";
            Record(action, actor, normalizedDetail, decision.RiskLevel, succeeded: false, error: message);
            return new AgentActionGatewayResult<T>(false, default, decision, message, exception);
        }
    }

    void Record(string action, string actor, string detail, AgentRiskLevel riskLevel, bool succeeded, string? error = null)
    {
        auditLog?.Record(
            action,
            actor,
            detail,
            ToAuditRiskLevel(riskLevel),
            succeeded,
            error);
    }

    static AgentAuditRiskLevel ToAuditRiskLevel(AgentRiskLevel riskLevel)
    {
        return riskLevel switch
        {
            AgentRiskLevel.Low => AgentAuditRiskLevel.Low,
            AgentRiskLevel.Medium => AgentAuditRiskLevel.Medium,
            _ => AgentAuditRiskLevel.High
        };
    }

    static AgentApprovalRisk ToApprovalRiskLevel(AgentRiskLevel riskLevel)
    {
        return riskLevel switch
        {
            AgentRiskLevel.Low => AgentApprovalRisk.Low,
            AgentRiskLevel.Medium => AgentApprovalRisk.Medium,
            _ => AgentApprovalRisk.High
        };
    }

    static string DescribeActor(AgentPermissionRequest request, AgentExecutionGatewayDecision decision)
    {
        string prefix = decision.Priority switch
        {
            AgentActorPriority.Owner => "owner",
            AgentActorPriority.System => "system",
            AgentActorPriority.GroupParticipant => "group",
            _ => "guest"
        };

        return request.ActorUserId == null ? prefix : $"{prefix}:{request.ActorUserId.Value}";
    }
}
