namespace Alife.Function.DataAgent;

public interface IDataAgentScenarioContextProvider
{
    DataAgentScenarioContext Build(DataAgentCatalog catalog, string question);
}
