using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Alife.Framework;
using Alife.Platform;
using Autofac;

namespace Alife.Function.MessageFilter;

[Module(
    "Unified Self Context",
    "Builds one self-context prompt that explains selected modules as the character's body, senses, memory, communication, and action abilities.",
    defaultCategory: "Alife Official/Living Environment",
    LaunchOrder = -90)]
public class SelfContextService : InteractiveModule<SelfContextService>
    , IContextContributor
{
    public IEnumerable<IEmbodiedCapability>? CapabilitySourceOverride { get; set; }

    public override async Task AwakeAsync(AwakeContext context)
    {
        await base.AwakeAsync(context);
    }

    public IEnumerable<ContextContribution> GetContextContributions()
    {
        IEmbodiedCapability[] selectedCapabilities = ResolveCapabilities()
            .Where(capability => ReferenceEquals(capability, this) == false)
            .ToArray();
        string prompt = EmbodiedCapabilityPromptFormatter.Format(
            selectedCapabilities,
            ex => AlifeTerminal.LogWarning(ex.ToString()));

        if (string.IsNullOrWhiteSpace(prompt))
            return [];

        return [
            new ContextContribution(
                "self-context",
                prompt,
                Priority: 1000,
                MaxLength: 2600)
        ];
    }

    IEnumerable<IEmbodiedCapability> ResolveCapabilities()
    {
        if (CapabilitySourceOverride != null)
            return CapabilitySourceOverride;

        try
        {
            return ChatActivity.ModuleService.Resolve<IEnumerable<IEmbodiedCapability>>();
        }
        catch (Exception ex)
        {
            AlifeTerminal.LogWarning($"Self context capabilities unavailable: {ex.Message}");
            return [];
        }
    }
}
