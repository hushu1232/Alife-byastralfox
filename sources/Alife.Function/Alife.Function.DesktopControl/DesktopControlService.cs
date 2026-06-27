namespace Alife.Function.DesktopControl;

public sealed class DesktopControlService(
    IDesktopRuntimeReader reader,
    DesktopCapabilityRegistry? capabilityRegistry = null)
{
    readonly DesktopCapabilityRegistry capabilityRegistry = capabilityRegistry ?? DesktopCapabilityRegistry.CreateDefault();

    public string GetCapabilitySummary()
    {
        return capabilityRegistry.FormatForOwner();
    }

    public async Task<string> GetStatusAsync(CancellationToken cancellationToken = default)
    {
        DesktopSnapshot snapshot = await reader.CaptureAsync(cancellationToken);
        return snapshot.FormatCompact();
    }

    public async Task<string> GetProcessListAsync(int maxItems = 20, CancellationToken cancellationToken = default)
    {
        DesktopSnapshot snapshot = await reader.CaptureAsync(cancellationToken);
        int limit = Math.Max(1, maxItems);
        List<string> lines = [$"processes_shown={Math.Min(limit, snapshot.Processes.Count)}"];
        lines.AddRange(snapshot.Processes
            .OrderByDescending(process => process.WorkingSetMb)
            .ThenBy(process => process.Name, StringComparer.OrdinalIgnoreCase)
            .Take(limit)
            .Select(process => $"{process.ProcessId} {process.Name} memory_mb={process.WorkingSetMb}"));
        return string.Join(Environment.NewLine, lines);
    }

    public async Task<string> GetWindowListAsync(int maxItems = 20, CancellationToken cancellationToken = default)
    {
        DesktopSnapshot snapshot = await reader.CaptureAsync(cancellationToken);
        int limit = Math.Max(1, maxItems);
        IReadOnlyList<WindowSnapshot> windows = snapshot.Windows
            .Where(window => string.IsNullOrWhiteSpace(window.Title) == false)
            .Take(limit)
            .ToArray();
        List<string> lines = [$"windows_shown={windows.Count}"];
        lines.AddRange(windows.Select(window => $"{window.ProcessName}: {window.Title}"));
        return string.Join(Environment.NewLine, lines);
    }
}
