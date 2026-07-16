using Alife.Function.LocalRuntime;
using System;

namespace Alife.Function.Speech;

public sealed class LocalSpeechCapabilityAdapter(Uri endpoint, ILocalCapabilityProcessHost process) : LoopbackProcessCapabilityAdapter(CapabilityKind.Speech, endpoint, process);
