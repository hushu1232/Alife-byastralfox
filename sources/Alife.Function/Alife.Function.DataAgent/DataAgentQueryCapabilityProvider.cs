using Alife.Function.FunctionCaller;
using Alife.Function.Interpreter;

namespace Alife.Function.DataAgent;

public sealed class DataAgentQueryCapabilityProvider(
    DataAgentService service,
    Action<string>? resultPublisher = null) : IDataAgentCapabilityProvider
{
    public string Name => nameof(DataAgentQueryCapabilityProvider);

    public IReadOnlyList<ToolCapabilityManifest> ToolManifests => DataAgentToolCapabilityManifests.Query;

    public void Register(IDataAgentCapabilityRegistrar registrar)
    {
        ArgumentNullException.ThrowIfNull(registrar);
        registrar.RegisterXmlHandlerWithoutStaticDocument(new XmlHandler(new DataAgentToolHandler(service, resultPublisher)));
    }
}
