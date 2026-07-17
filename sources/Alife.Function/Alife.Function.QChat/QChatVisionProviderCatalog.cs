using System;
using System.Collections.Generic;
using System.Linq;

namespace Alife.Function.QChat;

public sealed class QChatVisionProviderSettings
{
    public string ProviderId { get; set; } = "";
    public string Model { get; set; } = "";
    public string ApiEndpoint { get; set; } = "";
    public string ApiKeyEnvironmentVariable { get; set; } = "";
    public bool Enabled { get; set; } = true;
}

public sealed class QChatVisionProviderCatalog
{
    public List<QChatVisionProviderSettings> Providers { get; set; } = [];

    public static QChatVisionProviderCatalog CreateDefault() => new()
    {
        Providers =
        [
            new QChatVisionProviderSettings
            {
                ProviderId = "agnes",
                Model = "agnes-2.0-flash",
                ApiEndpoint = "https://apihub.agnes-ai.com/v1/chat/completions",
                ApiKeyEnvironmentVariable = QChatAgnesVisionApiKeyResolver.DefaultEnvironmentVariableName
            },
            new QChatVisionProviderSettings
            {
                ProviderId = "grok",
                Model = "grok-4.5",
                ApiKeyEnvironmentVariable = "ALIFE_GROK_VISION_API_KEY"
            }
        ]
    };

    public QChatVisionProviderSettings? Find(string? providerId)
    {
        string normalized = providerId?.Trim() ?? "";
        return Providers.FirstOrDefault(item =>
            string.Equals(item.ProviderId?.Trim(), normalized, StringComparison.OrdinalIgnoreCase));
    }
}
