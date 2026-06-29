using Alife.Function.FunctionCaller;
using Alife.Function.Interpreter;

namespace Alife.Function.DataAgent;

public sealed class DataAgentCapabilityRegistrar(XmlFunctionCaller functionService)
    : IDataAgentCapabilityRegistrar
{
    public void RegisterXmlHandlerWithoutStaticDocument(XmlHandler handler, params string[] plainAreas)
    {
        ArgumentNullException.ThrowIfNull(handler);
        functionService.RegisterHandlerWithoutDocument(handler, plainAreas);
    }
}
