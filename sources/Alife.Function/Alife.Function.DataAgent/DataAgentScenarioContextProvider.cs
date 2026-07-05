namespace Alife.Function.DataAgent;

public sealed class DataAgentScenarioContextProvider : IDataAgentScenarioContextProvider
{
    readonly string scenarioPackPath;
    readonly DataAgentScenarioContextBuilder builder;

    public DataAgentScenarioContextProvider(
        string scenarioPackPath,
        DataAgentScenarioContextBuilder? builder = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(scenarioPackPath);

        this.scenarioPackPath = scenarioPackPath;
        this.builder = builder ?? new DataAgentScenarioContextBuilder();
    }

    public static DataAgentScenarioContextProvider CreateDefault()
    {
        string repoRoot = FindRepositoryRoot(AppContext.BaseDirectory);
        return new DataAgentScenarioContextProvider(Path.Combine(
            repoRoot,
            "docs",
            "dataagent",
            "scenario-packs",
            "engineering.zh-CN.json"));
    }

    public DataAgentScenarioContext Build(DataAgentCatalog catalog, string question)
    {
        ArgumentNullException.ThrowIfNull(catalog);

        if (File.Exists(scenarioPackPath) == false)
        {
            return new DataAgentScenarioContext(
                "unavailable",
                "und",
                [],
                [],
                [],
                [],
                DataAgentScenarioContext.ReasonPackUnavailable);
        }

        DataAgentScenarioKnowledgePack pack = DataAgentScenarioKnowledgePackProvider.Load(scenarioPackPath);
        return builder.Build(catalog, pack, question);
    }

    static string FindRepositoryRoot(string startDirectory)
    {
        DirectoryInfo? directory = new(startDirectory);
        while (directory != null)
        {
            if (File.Exists(Path.Combine(directory.FullName, "Alife.slnx")) &&
                Directory.Exists(Path.Combine(directory.FullName, "docs")))
            {
                return directory.FullName;
            }

            directory = directory.Parent;
        }

        return Directory.GetCurrentDirectory();
    }
}
