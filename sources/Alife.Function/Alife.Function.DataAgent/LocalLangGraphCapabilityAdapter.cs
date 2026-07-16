using Alife.Function.LocalRuntime;

namespace Alife.Function.DataAgent;

public sealed class LocalLangGraphCapabilityAdapter(Uri endpoint, ILocalCapabilityProcessHost process) : LoopbackProcessCapabilityAdapter(CapabilityKind.LangGraph, endpoint, process);
