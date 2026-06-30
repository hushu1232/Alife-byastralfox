using Alife.Function.FunctionCaller;
using Alife.Function.Interpreter;

namespace Alife.Function.DataAgent;

public sealed class DataAgentAnalysisCapabilityProvider(
    IDataAgentAnalysisOrchestrator orchestrator,
    Action<string>? resultPublisher = null) : IDataAgentCapabilityProvider
{
    public string Name => nameof(DataAgentAnalysisCapabilityProvider);

    public IReadOnlyList<ToolCapabilityManifest> ToolManifests => DataAgentToolCapabilityManifests.Analysis;

    public void Register(IDataAgentCapabilityRegistrar registrar)
    {
        ArgumentNullException.ThrowIfNull(registrar);
        registrar.RegisterXmlHandlerWithoutStaticDocument(new XmlHandler(new DataAgentAnalysisToolHandler(orchestrator, resultPublisher)));
    }
}
