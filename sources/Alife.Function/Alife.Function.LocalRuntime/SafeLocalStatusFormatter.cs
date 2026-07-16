using System.Text;

namespace Alife.Function.LocalRuntime;

public sealed class SafeLocalStatusFormatter
{
    public string Format(SafeLocalStatus status, string? unsafeDiagnostics = null)
    {
        StringBuilder text = new();
        text.Append("overall=").Append(Normalize(status.Overall));
        foreach ((string id, LocalAccountHealth health) in status.Accounts.OrderBy(x => x.Key)) text.Append(";account=").Append(id is "account-a" or "account-b" ? id : "unknown").Append(':').Append(health);
        foreach ((CapabilityKind kind, string state) in status.Capabilities.OrderBy(x => x.Key)) text.Append(";capability=").Append(kind).Append(':').Append(Normalize(state));
        text.Append(";reason=").Append(status.Reason); return text.ToString();
    }
    private static string Normalize(string value) => value.ToLowerInvariant() is "healthy" or "degraded" or "unavailable" or "draining" or "ready" or "running" or "completed" ? value.ToLowerInvariant() : "unavailable";
}
