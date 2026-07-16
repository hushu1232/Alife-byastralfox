using System.Net;
using System.Text.Json;

namespace Alife.Function.LocalRuntime;

public static class LocalProductionConfiguration
{
    public static LocalProductionPlan Parse(string json)
    {
        try
        {
            using JsonDocument document = JsonDocument.Parse(json);
            RejectSensitiveProperties(document.RootElement);
            if (!document.RootElement.TryGetProperty("accounts", out JsonElement accountsValue) || accountsValue.ValueKind != JsonValueKind.Array || accountsValue.GetArrayLength() != 2)
                throw new LocalProductionConfigurationException("Exactly two accounts are required.");
            List<LocalAccountSlot> accounts = accountsValue.EnumerateArray().Select(ParseAccount).ToList();
            if (!accounts.Select(x => x.Id).OrderBy(x => x).SequenceEqual(["account-a", "account-b"]) || accounts.Select(x => x.OneBotUrl.Port).Distinct().Count() != 2)
                throw new LocalProductionConfigurationException("Accounts or ports are invalid.");
            int maxQueueDepth = OptionalPositive(document.RootElement, "maxQueueDepth", 16);
            int drainTimeoutSeconds = OptionalPositive(document.RootElement, "drainTimeoutSeconds", 90);
            int idleTimeoutSeconds = OptionalPositive(document.RootElement, "idleTimeoutSeconds", 300);
            if (maxQueueDepth is < 1 or > 100)
                throw new LocalProductionConfigurationException("Queue depth must be between 1 and 100.");
            return new LocalProductionPlan(accounts, maxQueueDepth, TimeSpan.FromSeconds(drainTimeoutSeconds), TimeSpan.FromSeconds(idleTimeoutSeconds));
        }
        catch (LocalProductionConfigurationException) { throw; }
        catch (Exception exception) when (exception is JsonException or ArgumentException) { throw new LocalProductionConfigurationException("Local production configuration is invalid."); }
    }

    private static LocalAccountSlot ParseAccount(JsonElement item)
    {
        string id = Required(item, "id");
        if (!Uri.TryCreate(Required(item, "oneBotUrl"), UriKind.Absolute, out Uri? url) || url.Scheme != "ws" || !IsLoopback(url.Host) || url.Port <= 0)
            throw new LocalProductionConfigurationException("OneBot URL must use loopback.");
        return new LocalAccountSlot(id, url, Absolute(Required(item, "runtimeRoot")), Absolute(Required(item, "storageRoot")), Absolute(Required(item, "tempRoot")));
    }

    private static string Required(JsonElement item, string name) => item.TryGetProperty(name, out JsonElement value) && value.ValueKind == JsonValueKind.String && !string.IsNullOrWhiteSpace(value.GetString()) ? value.GetString()! : throw new LocalProductionConfigurationException($"{name} is required.");
    private static int OptionalPositive(JsonElement item, string name, int fallback) => !item.TryGetProperty(name, out JsonElement value) ? fallback : value.ValueKind == JsonValueKind.Number && value.TryGetInt32(out int result) && result > 0 ? result : throw new LocalProductionConfigurationException($"{name} must be positive.");
    private static string Absolute(string path) => Path.IsPathFullyQualified(path) ? Path.GetFullPath(path) : throw new LocalProductionConfigurationException("Roots must be absolute.");
    private static bool IsLoopback(string host) => host.Equals("localhost", StringComparison.OrdinalIgnoreCase) || IPAddress.TryParse(host, out IPAddress? ip) && IPAddress.IsLoopback(ip);
    private static void RejectSensitiveProperties(JsonElement value)
    {
        if (value.ValueKind == JsonValueKind.Object) foreach (JsonProperty property in value.EnumerateObject()) { if (new[] { "token", "secret", "password", "connectionString", "ownerId" }.Any(word => property.Name.Contains(word, StringComparison.OrdinalIgnoreCase))) throw new LocalProductionConfigurationException("Sensitive configuration is forbidden."); RejectSensitiveProperties(property.Value); }
        else if (value.ValueKind == JsonValueKind.Array) foreach (JsonElement item in value.EnumerateArray()) RejectSensitiveProperties(item);
    }
}
