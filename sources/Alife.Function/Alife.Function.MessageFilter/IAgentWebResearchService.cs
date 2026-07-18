using System.Threading;
using System.Threading.Tasks;

namespace Alife.Function.Agent;

public interface IAgentWebResearchService
{
    Task<AgentWebResearchResult> ResearchAsync(
        AgentWebResearchRequest request,
        CancellationToken cancellationToken = default);
}
