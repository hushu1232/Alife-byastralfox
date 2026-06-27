using System;
using System.Threading;
using System.Threading.Tasks;

namespace Alife.Framework;

public interface IAutobiographicalMemorySink
{
    Task<string> InsertAutobiographicalMemoryAsync(
        string summary,
        string content,
        DateTime startTime,
        DateTime endTime,
        CancellationToken cancellationToken = default);
}

public sealed record AutobiographicalMemoryForgetResult(
    bool Success,
    string Message,
    string? MemoryName);

public sealed record AutobiographicalMemoryPurgeResult(
    bool Success,
    string Message,
    string? MemoryName,
    string? TrashPath);

public interface IAutobiographicalMemoryController
{
    Task<AutobiographicalMemoryForgetResult> ForgetAutobiographicalMemoryAsync(
        string memoryName,
        CancellationToken cancellationToken = default);

    Task<AutobiographicalMemoryPurgeResult> PurgeAutobiographicalMemoryAsync(
        string memoryName,
        CancellationToken cancellationToken = default);
}
