using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace Alife.Function.Agent;

public sealed class FallbackPublicSearchProvider(params IAgentPublicSearchProvider[] providers) : IAgentPublicSearchProvider
{
    readonly IAgentPublicSearchProvider[] providers = providers.Where(provider => provider != null).ToArray();

    public async Task<IReadOnlyList<AgentPublicSearchResult>> SearchAsync(
        string query,
        int maxResults,
        CancellationToken cancellationToken = default)
    {
        Exception? lastError = null;
        foreach (IAgentPublicSearchProvider provider in providers)
        {
            cancellationToken.ThrowIfCancellationRequested();
            try
            {
                IReadOnlyList<AgentPublicSearchResult> results = await provider.SearchAsync(
                    query,
                    maxResults,
                    cancellationToken);
                if (results.Count > 0)
                    return results;
            }
            catch (Exception ex) when (ex is not OperationCanceledException || cancellationToken.IsCancellationRequested == false)
            {
                lastError = ex;
            }
        }

        if (lastError != null)
            throw lastError;

        return [];
    }
}
