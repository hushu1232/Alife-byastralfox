using System;
using System.Collections.Generic;
using System.Linq;
using Alife.Function.FunctionCaller;

namespace Alife.Function.DataAgent;

public sealed class DataAgentCapabilityRegistry
{
    readonly List<IDataAgentCapabilityProvider> providers = [];
    readonly List<string> providerNamesInOrder = [];
    readonly List<string> toolNamesInOrder = [];
    readonly List<ToolCapabilityManifest> toolManifests = [];
    readonly HashSet<string> providerNames = new(StringComparer.Ordinal);
    readonly HashSet<string> toolNames = new(StringComparer.Ordinal);

    public IReadOnlyList<IDataAgentCapabilityProvider> Providers => providers.ToArray();
    public IReadOnlyList<string> ProviderNames => providerNamesInOrder.ToArray();
    public IReadOnlyList<string> ToolNames => toolNamesInOrder.ToArray();
    public IReadOnlyList<ToolCapabilityManifest> ToolManifests => toolManifests.ToArray();

    public void Add(IDataAgentCapabilityProvider provider)
    {
        ArgumentNullException.ThrowIfNull(provider);

        string providerName = provider.Name;
        if (string.IsNullOrWhiteSpace(providerName))
        {
            throw new ArgumentException("DataAgent capability provider name cannot be blank.", nameof(provider));
        }

        if (providerNames.Contains(providerName))
        {
            throw new InvalidOperationException($"Duplicate DataAgent capability provider '{providerName}'.");
        }

        IReadOnlyList<ToolCapabilityManifest>? manifests = provider.ToolManifests;
        if (manifests is null)
        {
            throw new ArgumentException($"DataAgent capability provider '{providerName}' returned null tool manifests.", nameof(provider));
        }

        ToolCapabilityManifest[] manifestSnapshot = manifests.ToArray();
        List<ToolCapabilityManifest> validatedManifests = new(manifestSnapshot.Length);
        List<string> validatedToolNames = new(manifestSnapshot.Length);
        HashSet<string> providerToolNames = new(StringComparer.Ordinal);

        foreach (ToolCapabilityManifest? manifest in manifestSnapshot)
        {
            if (manifest is null)
            {
                throw new ArgumentException($"DataAgent capability provider '{providerName}' returned a null tool manifest.", nameof(provider));
            }

            if (string.IsNullOrWhiteSpace(manifest.Name))
            {
                throw new ArgumentException($"DataAgent tool capability name cannot be blank for provider '{providerName}'.", nameof(provider));
            }

            if (toolNames.Contains(manifest.Name) || !providerToolNames.Add(manifest.Name))
            {
                throw new InvalidOperationException($"Duplicate DataAgent tool capability '{manifest.Name}'.");
            }

            validatedManifests.Add(manifest);
            validatedToolNames.Add(manifest.Name);
        }

        providers.Add(provider);
        providerNames.Add(providerName);
        providerNamesInOrder.Add(providerName);
        toolManifests.AddRange(validatedManifests);
        toolNamesInOrder.AddRange(validatedToolNames);
        foreach (string toolName in validatedToolNames)
        {
            toolNames.Add(toolName);
        }
    }
}
