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
