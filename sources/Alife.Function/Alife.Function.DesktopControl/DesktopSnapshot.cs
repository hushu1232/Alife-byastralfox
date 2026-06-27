namespace Alife.Function.DesktopControl;

public sealed record SystemHealthSnapshot(
    int ProcessorCount,
    long TotalMemoryMb,
    long UsedMemoryMb,
    long TotalDiskMb,
    long FreeDiskMb);

public sealed record ProcessSnapshot(
    int ProcessId,
    string Name,
    long WorkingSetMb);

public sealed record WindowSnapshot(
    nint Handle,
    string Title,
    string ProcessName);

public sealed record DesktopSnapshot(
    DateTimeOffset CapturedAt,
    SystemHealthSnapshot Health,
    IReadOnlyList<ProcessSnapshot> Processes,
    IReadOnlyList<WindowSnapshot> Windows,
    IReadOnlyList<string> Warnings)
{
    public string FormatCompact()
    {
        string status = Warnings.Count == 0 ? "ok" : "warning";
        return string.Join(
            Environment.NewLine,
            $"desktop_status={status}",
            $"captured_at={CapturedAt:O}",
            $"processor_count={Health.ProcessorCount}",
            $"memory_used_mb={Health.UsedMemoryMb}",
            $"memory_total_mb={Health.TotalMemoryMb}",
            $"disk_free_mb={Health.FreeDiskMb}",
            $"disk_total_mb={Health.TotalDiskMb}",
            $"processes={Processes.Count}",
            $"windows={Windows.Count}",
            $"warnings={Warnings.Count}");
    }
}
