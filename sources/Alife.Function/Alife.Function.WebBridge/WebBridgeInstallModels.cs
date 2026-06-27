using System;
using System.Collections.Generic;

namespace Alife.Function.WebBridge;

public sealed class WebBridgeInstallRequest
{
    public string PackageId { get; set; } = string.Empty;
}

public sealed class WebBridgeInstallResult
{
    public string PackageId { get; set; } = string.Empty;
    public string Status { get; set; } = WebBridgePackageStatus.PendingActivation;
    public string PackageRootPath { get; set; } = string.Empty;
    public string ManifestPath { get; set; } = string.Empty;
    public string ConfigDraftPath { get; set; } = string.Empty;
    public int InstalledFiles { get; set; }
    public List<string> Warnings { get; set; } = new();
}

public static class WebBridgePackageStatus
{
    public const string Downloaded = "downloaded";
    public const string Verified = "verified";
    public const string Installed = "installed";
    public const string PendingActivation = "pendingActivation";
    public const string Failed = "failed";
}

public sealed class WebBridgeInstalledPackageRecord
{
    public string PackageId { get; set; } = string.Empty;
    public string PackageType { get; set; } = string.Empty;
    public string Version { get; set; } = string.Empty;
    public string Status { get; set; } = WebBridgePackageStatus.PendingActivation;
    public DateTimeOffset InstalledAtUtc { get; set; } = DateTimeOffset.UtcNow;
    public string PackageRootPath { get; set; } = string.Empty;
    public string ManifestPath { get; set; } = string.Empty;
    public string ConfigDraftPath { get; set; } = string.Empty;
}

public sealed class WebBridgeLocalCatalog
{
    public List<WebBridgeInstalledPackageRecord> InstalledPackages { get; set; } = new();
}
