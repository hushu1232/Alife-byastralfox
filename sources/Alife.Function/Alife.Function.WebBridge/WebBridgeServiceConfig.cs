namespace Alife.Function.WebBridge;

public record WebBridgeServiceConfig
{
    public string ApiBaseUrl { get; set; } = "http://localhost:3000";
    public string? ApiToken { get; set; }
    public int RequestTimeoutSeconds { get; set; } = 10;
    public string? AssetRootPath { get; set; }
    public bool AutoSyncEnabled { get; set; }
    public int SyncIntervalMilliseconds { get; set; } = 30000;
    public bool SyncAssetsEnabled { get; set; } = true;
}
