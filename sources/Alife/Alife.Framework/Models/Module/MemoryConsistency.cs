using System.Threading;
using System.Threading.Tasks;

namespace Alife.Framework;

public sealed record MemoryConsistencySnapshot(
    int MissingArchiveFiles,
    int MissingIndexRecords,
    int ContentMismatches,
    int RepairedArchiveFiles,
    int RepairedIndexRecords,
    int RepairedContentMismatches)
{
    public static MemoryConsistencySnapshot Empty { get; } = new(0, 0, 0, 0, 0, 0);

    public int TotalIssues => MissingArchiveFiles + MissingIndexRecords + ContentMismatches;
    public bool HasIssues => TotalIssues > 0;
}

public interface IMemoryConsistencyReporter
{
    MemoryConsistencySnapshot GetMemoryConsistencySnapshot();

    Task<MemoryConsistencySnapshot> RepairMemoryConsistencyAsync(CancellationToken cancellationToken = default);
}
