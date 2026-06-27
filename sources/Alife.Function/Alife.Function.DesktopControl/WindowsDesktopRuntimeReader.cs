using System.Diagnostics;

namespace Alife.Function.DesktopControl;

public sealed class WindowsDesktopRuntimeReader : IDesktopRuntimeReader
{
    public Task<DesktopSnapshot> CaptureAsync(CancellationToken cancellationToken = default)
    {
        List<string> warnings = [];
        Process[] processes = SafeReadProcesses(warnings);
        ProcessSnapshot[] processSnapshots = CaptureProcesses(processes, warnings);
        WindowSnapshot[] windowSnapshots = CaptureWindows(processes, warnings);
        SystemHealthSnapshot health = CaptureHealth(warnings);

        foreach (Process process in processes)
            process.Dispose();

        DesktopSnapshot snapshot = new(
            DateTimeOffset.Now,
            health,
            processSnapshots,
            windowSnapshots,
            warnings);
        return Task.FromResult(snapshot);
    }

    static Process[] SafeReadProcesses(List<string> warnings)
    {
        try
        {
            return Process.GetProcesses();
        }
        catch (Exception ex)
        {
            warnings.Add($"process_capture_failed={ex.GetType().Name}");
            return [];
        }
    }

    static ProcessSnapshot[] CaptureProcesses(IEnumerable<Process> processes, List<string> warnings)
    {
        try
        {
            return processes
                .Select(process =>
                {
                    int processId = SafeRead(() => process.Id, 0);
                    string name = SafeRead(() => process.ProcessName, "unknown");
                    long workingSetMb = SafeRead(() => process.WorkingSet64 / 1024 / 1024, 0);
                    return new ProcessSnapshot(processId, name, workingSetMb);
                })
                .Where(process => process.ProcessId > 0)
                .OrderBy(process => process.Name, StringComparer.OrdinalIgnoreCase)
                .ToArray();
        }
        catch (Exception ex)
        {
            warnings.Add($"process_snapshot_failed={ex.GetType().Name}");
            return [];
        }
    }

    static WindowSnapshot[] CaptureWindows(IEnumerable<Process> processes, List<string> warnings)
    {
        try
        {
            return processes
                .Select(process =>
                {
                    nint handle = SafeRead(() => process.MainWindowHandle, nint.Zero);
                    string title = SafeRead(() => process.MainWindowTitle, string.Empty).Trim();
                    string name = SafeRead(() => process.ProcessName, "unknown");
                    return new WindowSnapshot(handle, title, name);
                })
                .Where(window => window.Handle != nint.Zero && string.IsNullOrWhiteSpace(window.Title) == false)
                .OrderBy(window => window.ProcessName, StringComparer.OrdinalIgnoreCase)
                .ThenBy(window => window.Title, StringComparer.OrdinalIgnoreCase)
                .ToArray();
        }
        catch (Exception ex)
        {
            warnings.Add($"window_snapshot_failed={ex.GetType().Name}");
            return [];
        }
    }

    static SystemHealthSnapshot CaptureHealth(List<string> warnings)
    {
        try
        {
            string? systemRoot = Path.GetPathRoot(Environment.SystemDirectory);
            DriveInfo? systemDrive = DriveInfo.GetDrives()
                .Where(drive => drive.IsReady)
                .OrderByDescending(drive => drive.Name.Equals(systemRoot, StringComparison.OrdinalIgnoreCase))
                .FirstOrDefault();

            long totalDiskMb = systemDrive?.TotalSize / 1024 / 1024 ?? 0;
            long freeDiskMb = systemDrive?.AvailableFreeSpace / 1024 / 1024 ?? 0;
            long totalMemoryMb = GC.GetGCMemoryInfo().TotalAvailableMemoryBytes / 1024 / 1024;
            long usedMemoryMb = Process.GetCurrentProcess().WorkingSet64 / 1024 / 1024;
            return new SystemHealthSnapshot(Environment.ProcessorCount, totalMemoryMb, usedMemoryMb, totalDiskMb, freeDiskMb);
        }
        catch (Exception ex)
        {
            warnings.Add($"health_capture_failed={ex.GetType().Name}");
            return new SystemHealthSnapshot(Environment.ProcessorCount, 0, 0, 0, 0);
        }
    }

    static T SafeRead<T>(Func<T> read, T fallback)
    {
        try
        {
            return read();
        }
        catch
        {
            return fallback;
        }
    }
}
