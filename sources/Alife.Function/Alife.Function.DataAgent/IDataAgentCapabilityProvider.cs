using System.Collections.Generic;
using Alife.Function.FunctionCaller;

namespace Alife.Function.DataAgent;

public interface IDataAgentCapabilityProvider
{
    string Name { get; }
    IReadOnlyList<ToolCapabilityManifest> ToolManifests { get; }
    void Register(IDataAgentCapabilityRegistrar registrar);
}
