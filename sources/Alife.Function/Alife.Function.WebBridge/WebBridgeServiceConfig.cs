namespace Alife.Function.WebBridge;

public record WebBridgeServiceConfig
{
    public string ApiBaseUrl { get; set; } = "http://localhost:3000";
    public string? ApiToken { get; set; }
    public int RequestTimeoutSeconds { get; set; } = 10;
}
