namespace Alife.Function.DataAgent;

public interface IDataAgentQueryPlanner
{
    DataAgentQueryPlan Plan(DataAgentQueryRequest request);
}
