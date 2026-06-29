using Alife.Function.Interpreter;

namespace Alife.Function.DataAgent;

public interface IDataAgentCapabilityRegistrar
{
    void RegisterXmlHandlerWithoutStaticDocument(XmlHandler handler, params string[] plainAreas);
}
