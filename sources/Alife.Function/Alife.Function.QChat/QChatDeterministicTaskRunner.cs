using System;
using System.Threading.Tasks;

namespace Alife.Function.QChat;

public enum QChatDeterministicTaskStatus
{
    Succeeded,
    Failed
}

public sealed record QChatDeterministicTaskContext(
    string TaskType,
    string? FileName,
    OneBotMessageType? TargetType,
    long? TargetId);

public sealed record QChatDeterministicTaskResult(
    QChatDeterministicTaskContext Context,
    QChatDeterministicTaskStatus Status,
    string? Error,
    Exception? Exception)
{
    public bool Succeeded => Status == QChatDeterministicTaskStatus.Succeeded;
}

public static class QChatDeterministicTaskRunner
{
    public static async Task<QChatDeterministicTaskResult> ExecuteAsync(
        QChatDeterministicTaskContext context,
        Func<Task> action)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(action);

        try
        {
            await action();
            return new QChatDeterministicTaskResult(
                context,
                QChatDeterministicTaskStatus.Succeeded,
                Error: null,
                Exception: null);
        }
        catch (Exception ex)
        {
            return new QChatDeterministicTaskResult(
                context,
                QChatDeterministicTaskStatus.Failed,
                ex.Message,
                ex);
        }
    }
}
