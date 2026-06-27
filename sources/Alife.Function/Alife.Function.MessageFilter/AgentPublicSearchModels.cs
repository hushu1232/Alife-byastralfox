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
