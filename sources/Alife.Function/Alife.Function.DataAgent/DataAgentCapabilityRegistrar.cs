using Alife.Function.FunctionCaller;
using Alife.Function.Interpreter;

namespace Alife.Function.DataAgent;

public sealed class DataAgentCapabilityRegistrar(XmlFunctionCaller functionService)
    : IDataAgentCapabilityRegistrar
{
    readonly XmlFunctionCaller functionService = functionService ?? throw new ArgumentNullException(nameof(functionService));

    public void RegisterXmlHandlerWithoutStaticDocument(XmlHandler handler, params string[] plainAreas)
    {
        ArgumentNullException.ThrowIfNull(handler);
        functionService.RegisterHandlerWithoutDocument(handler, plainAreas);
    }
}
