using System.Diagnostics;

namespace Alife.Function.DesktopControl;

public sealed record DesktopBusinessExecutionResult(
    bool Success,
    string Message);

public interface IDesktopApprovedDraftExecutor
{
    Task<DesktopBusinessExecutionResult> ExecuteAsync(
        DesktopActionDraftEntry draft,
        CancellationToken cancellationToken = default);
}

public sealed class WindowsDesktopBusinessExecutor : IDesktopApprovedDraftExecutor
{
    public Task<DesktopBusinessExecutionResult> ExecuteAsync(
        DesktopActionDraftEntry draft,
        CancellationToken cancellationToken = default)
    {
        string action = NormalizeAction(draft.RequestedAction);
        if (action != "open notepad")
        {
            return Task.FromResult(new DesktopBusinessExecutionResult(
                false,
                "desktop_execution=denied reason=unsupported_action"));
        }

        using Process? _ = Process.Start(new ProcessStartInfo
        {
            FileName = "notepad.exe",
            UseShellExecute = false
        });
        return Task.FromResult(new DesktopBusinessExecutionResult(
            true,
            "desktop_execution=started action=open_notepad"));
    }

    static string NormalizeAction(string value)
    {
        string normalized = (value ?? string.Empty).Trim().ToLowerInvariant();
        while (normalized.Contains("  ", StringComparison.Ordinal))
            normalized = normalized.Replace("  ", " ", StringComparison.Ordinal);
        return normalized;
    }
}
