using System;
using System.Collections.Generic;
using System.Linq;
using Alife.Function.FunctionCaller;

namespace Alife.Function.DataAgent;

public sealed class DataAgentCapabilityRegistry
{
    readonly List<IDataAgentCapabilityProvider> providers = [];
    readonly List<ToolCapabilityManifest> toolManifests = [];
    readonly HashSet<string> providerNames = new(StringComparer.Ordinal);
    readonly HashSet<string> toolNames = new(StringComparer.Ordinal);

    public IReadOnlyList<IDataAgentCapabilityProvider> Providers => providers;
    public IReadOnlyList<string> ProviderNames => providers.Select(provider => provider.Name).ToArray();
    public IReadOnlyList<string> ToolNames => toolManifests.Select(manifest => manifest.Name).ToArray();
    public IReadOnlyList<ToolCapabilityManifest> ToolManifests => toolManifests;

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

        List<ToolCapabilityManifest> validatedManifests = new(manifests.Count);
        HashSet<string> addedToolNames = new(StringComparer.Ordinal);

        foreach (ToolCapabilityManifest? manifest in manifests)
        {
            if (manifest is null)
            {
                throw new ArgumentException($"DataAgent capability provider '{providerName}' returned a null tool manifest.", nameof(provider));
            }

            if (string.IsNullOrWhiteSpace(manifest.Name))
            {
                throw new ArgumentException($"DataAgent tool capability name cannot be blank for provider '{providerName}'.", nameof(provider));
            }

            if (toolNames.Contains(manifest.Name) || !addedToolNames.Add(manifest.Name))
            {
                throw new InvalidOperationException($"Duplicate DataAgent tool capability '{manifest.Name}'.");
            }

            validatedManifests.Add(manifest);
        }

        providers.Add(provider);
        providerNames.Add(providerName);
        toolManifests.AddRange(validatedManifests);
        foreach (string toolName in addedToolNames)
        {
            toolNames.Add(toolName);
        }
    }
}
