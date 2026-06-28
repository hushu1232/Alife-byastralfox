namespace Alife.Function.DataAgent;

public interface IDataAgentQueryPlanner
{
    DataAgentQueryPlanEnvelope Plan(DataAgentQueryRequest request);
}
