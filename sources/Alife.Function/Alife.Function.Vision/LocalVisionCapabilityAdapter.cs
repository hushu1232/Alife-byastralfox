using Alife.Function.LocalRuntime;
using System;

namespace Alife.Function.Vision;

public sealed class LocalVisionCapabilityAdapter(Uri endpoint, ILocalCapabilityProcessHost process) : LoopbackProcessCapabilityAdapter(CapabilityKind.Vision, endpoint, process);
