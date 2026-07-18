using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Alife.Function.Agent;

public sealed class AgentPublicSearchConfig
{
    public bool EnablePublicSearch { get; set; } = false;
    public int MaxResults { get; set; } = 3;
    public int MaxQueryChars { get; set; } = 160;
}

public sealed class AgentMultiSourceSearchConfig
{
    public bool Enabled { get; set; } = false;
    public bool ParallelBuiltInProviders { get; set; } = true;
    public int PerProviderTimeoutMilliseconds { get; set; } = 1500;
    public int MaxMergedResults { get; set; } = 5;
    public int FailureThreshold { get; set; } = 3;
    public int CircuitBreakSeconds { get; set; } = 60;
    public bool DetectSmartWebSearchPlugin { get; set; } = true;
}

public sealed record AgentPublicSearchResult(
    string Title,
    string Url,
    string Snippet);

public sealed record AgentPublicSearchResponse(
    bool Success,
    string Reason,
    IReadOnlyList<AgentPublicSearchResult> Results,
    string FormattedContent);

public interface IAgentPublicSearchProvider
{
    Task<IReadOnlyList<AgentPublicSearchResult>> SearchAsync(
        string query,
        int maxResults,
        CancellationToken cancellationToken = default);
}
