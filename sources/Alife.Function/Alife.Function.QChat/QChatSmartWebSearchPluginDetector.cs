using System;
using System.Collections.Generic;
using System.Linq;

namespace Alife.Function.QChat;

public sealed record QChatSmartWebSearchPluginStatus(
    bool DetectionEnabled,
    bool IsLoaded,
    string Code,
    string Description);

public static class QChatSmartWebSearchPluginDetector
{
    const string PluginAssemblyName = "Alife.Plugin.SmartWebSearch";

    public static QChatSmartWebSearchPluginStatus Detect(bool enabled, IEnumerable<string> assemblyNames)
    {
        if (enabled == false)
            return new(false, false, "disabled", "SmartWebSearch detection is disabled; QChat structured research is independent.");

        bool loaded = assemblyNames.Any(name => string.Equals(
            name,
            PluginAssemblyName,
            StringComparison.OrdinalIgnoreCase));
        return loaded
            ? new(true, true, "loaded", "SmartWebSearch native plugin is loaded; its credentials and XML tools remain plugin-managed.")
            : new(true, false, "not_loaded", "SmartWebSearch is not loaded; QChat continues with built-in structured providers.");
    }

    public static QChatSmartWebSearchPluginStatus Detect(bool enabled) => Detect(
        enabled,
        AppDomain.CurrentDomain.GetAssemblies()
            .Select(assembly => assembly.GetName().Name ?? ""));
}
