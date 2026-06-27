using System;
using System.Collections.Concurrent;
using System.Threading;
using System.Threading.Tasks;

namespace Alife.Function.MessageFilter;

public enum AgentApprovalRisk
{
    Low,
    Medium,
    High
}

public enum AgentApprovalStatus
{
    Pending,
    Approved,
    Denied,
    Expired
}

public sealed record AgentApprovalRequest(
    long Id,
    long OwnerUserId,
    string Title,
    AgentApprovalRisk Risk,
    string Summary,
    DateTimeOffset CreatedAt,
    DateTimeOffset ExpiresAt,
    AgentApprovalStatus Status);

public sealed record AgentApprovalExecutionResult(
    bool Executed,
    string Message,
    Exception? Exception = null);

public sealed class AgentApprovalService
{
    readonly ConcurrentDictionary<long, AgentApprovalRequest> requests = new();
    readonly ConcurrentDictionary<long, Func<Task<string>>> executors = new();
    long nextId;

    public AgentApprovalRequest CreateRequest(
        long ownerUserId,
        string title,
        AgentApprovalRisk risk,
        string summary,
        TimeSpan expiresAfter)
    {
        long id = Interlocked.Increment(ref nextId);
        DateTimeOffset now = DateTimeOffset.Now;
        AgentApprovalRequest request = new(
            id,
            ownerUserId,
            (title ?? "").Trim(),
            risk,
            (summary ?? "").Trim(),
            now,
            now.Add(expiresAfter),
            AgentApprovalStatus.Pending);
        requests[id] = request;
        return request;
    }

    public AgentApprovalRequest CreateExecutableRequest(
        long ownerUserId,
        string title,
        AgentApprovalRisk risk,
        string summary,
        TimeSpan expiresAfter,
        Func<Task<string>> execute)
    {
        ArgumentNullException.ThrowIfNull(execute);
        AgentApprovalRequest request = CreateRequest(ownerUserId, title, risk, summary, expiresAfter);
        executors[request.Id] = execute;
        return request;
    }

    public AgentApprovalRequest? GetRequest(long id)
    {
        if (requests.TryGetValue(id, out AgentApprovalRequest? request) == false)
            return null;

        if (request.Status == AgentApprovalStatus.Pending && request.ExpiresAt < DateTimeOffset.Now)
        {
            request = request with { Status = AgentApprovalStatus.Expired };
            requests[id] = request;
        }

        return request;
    }

    public bool TryApprove(long id, long actorUserId, out string message)
    {
        return TrySetStatus(id, actorUserId, AgentApprovalStatus.Approved, "approved", out message);
    }

    public async Task<AgentApprovalExecutionResult> ApproveAndExecuteAsync(long id, long actorUserId)
    {
        if (TryApprove(id, actorUserId, out string message) == false)
            return new AgentApprovalExecutionResult(false, message);

        if (executors.TryRemove(id, out Func<Task<string>>? execute) == false)
            return new AgentApprovalExecutionResult(false, $"approval #{id} approved but no executable action was registered");

        try
        {
            string executionMessage = await execute();
            return new AgentApprovalExecutionResult(true, executionMessage);
        }
        catch (Exception ex)
        {
            return new AgentApprovalExecutionResult(false, $"approval #{id} execution failed: {ex.Message}", ex);
        }
    }

    public bool TryDeny(long id, long actorUserId, out string message)
    {
        return TrySetStatus(id, actorUserId, AgentApprovalStatus.Denied, "denied", out message);
    }

    bool TrySetStatus(long id, long actorUserId, AgentApprovalStatus status, string statusText, out string message)
    {
        AgentApprovalRequest? request = GetRequest(id);
        if (request == null)
        {
            message = $"approval #{id} not found";
            return false;
        }

        if (request.OwnerUserId != actorUserId)
        {
            message = $"approval #{id} can only be handled by owner";
            return false;
        }

        if (request.Status != AgentApprovalStatus.Pending)
        {
            message = $"approval #{id} is {request.Status}";
            return false;
        }

        requests[id] = request with { Status = status };
        if (status == AgentApprovalStatus.Denied)
            executors.TryRemove(id, out _);
        message = $"approval #{id} {statusText}";
        return true;
    }
}
