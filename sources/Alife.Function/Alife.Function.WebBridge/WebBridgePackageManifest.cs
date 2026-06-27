using System.Collections.Generic;

namespace Alife.Function.WebBridge;

public sealed class WebBridgePackageManifest
{
    public int SchemaVersion { get; set; } = 1;
    public string PackageId { get; set; } = string.Empty;
    public string PackageType { get; set; } = string.Empty;
    public string DisplayName { get; set; } = string.Empty;
    public string Version { get; set; } = string.Empty;
    public List<WebBridgePackageFile> Files { get; set; } = new();
    public WebBridgeConfigDraft ConfigDraft { get; set; } = new();
    public WebBridgeActivationPolicy ActivationPolicy { get; set; } = new();
}

public sealed class WebBridgePackageFile
{
    public string Kind { get; set; } = string.Empty;
    public string Url { get; set; } = string.Empty;
    public string RelativePath { get; set; } = string.Empty;
    public string Sha256 { get; set; } = string.Empty;
    public long Size { get; set; }
}

public sealed class WebBridgeConfigDraft
{
    public string CharacterName { get; set; } = string.Empty;
    public string CharacterCardPath { get; set; } = string.Empty;
    public string Live2DModelPath { get; set; } = string.Empty;
}

public sealed class WebBridgeActivationPolicy
{
    public bool AutoApply { get; set; }
    public bool RequiresLocalConfirmation { get; set; } = true;
}
