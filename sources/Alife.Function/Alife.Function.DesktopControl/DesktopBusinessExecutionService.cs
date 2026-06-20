using System.Diagnostics;

namespace Alife.Function.DesktopControl;

public sealed record DesktopBusinessExecutionResult(
    bool Success,
    string Message)
{
    public bool MarksDraftExecuted { get; init; } = Success;
}

public interface IDesktopApprovedDraftExecutor
{
    Task<DesktopBusinessExecutionResult> ExecuteAsync(
        DesktopActionDraftEntry draft,
        CancellationToken cancellationToken = default);
}

public sealed class WindowsDesktopBusinessExecutor(
    DesktopBusinessActionRegistry? actionRegistry = null) : IDesktopApprovedDraftExecutor
{
    readonly DesktopBusinessActionRegistry actionRegistry = actionRegistry ?? DesktopBusinessActionRegistry.CreateDefault();

    public Task<DesktopBusinessExecutionResult> ExecuteAsync(
        DesktopActionDraftEntry draft,
        CancellationToken cancellationToken = default)
    {
        if (actionRegistry.TryResolve(draft.RequestedAction, out DesktopBusinessActionDescriptor? action) == false)
        {
            return Task.FromResult(new DesktopBusinessExecutionResult(
                false,
                "desktop_execution=denied reason=unsupported_action")
            {
                MarksDraftExecuted = false
            });
        }

        using Process? _ = Process.Start(new ProcessStartInfo
        {
            FileName = action!.ExecutableName,
            Arguments = action.Arguments,
            UseShellExecute = false
        });
        return Task.FromResult(new DesktopBusinessExecutionResult(
            true,
            $"desktop_execution=started action={action.ActionKey}"));
    }
}
