namespace Alife.Function.WebBridge;

public static class WebBridgeSyncMilestones
{
    public const string ManifestFetched = "manifestFetched";
    public const string FilesDownloaded = "filesDownloaded";
    public const string HashValidated = "hashValidated";
    public const string PackageStaged = "packageStaged";
    public const string ConfirmationRequested = "confirmationRequested";
    public const string PackageApplied = "packageApplied";
    public const string PackageFailed = "packageFailed";
}

public static class WebBridgeSyncErrorCodes
{
    public const string WebBridgeOffline = "WEBBRIDGE_OFFLINE";
    public const string PackageHashMismatch = "PACKAGE_HASH_MISMATCH";
    public const string PackageApplyFailed = "PACKAGE_APPLY_FAILED";
    public const string PackageDownloadFailed = "PACKAGE_DOWNLOAD_FAILED";
    public const string PackageSecurityBlocked = "PACKAGE_SECURITY_BLOCKED";
}

public sealed class WebBridgeSyncMilestoneReport
{
    public string Milestone { get; set; } = string.Empty;
    public long? PackageVersion { get; set; }
    public string? ReportedAt { get; set; }
    public WebBridgeSyncErrorReport? Error { get; set; }
}

public sealed class WebBridgeSyncErrorReport
{
    public string Code { get; set; } = WebBridgeSyncErrorCodes.PackageApplyFailed;
    public string? Message { get; set; }
    public string? Detail { get; set; }
}
