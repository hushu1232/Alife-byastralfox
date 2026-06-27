using System;

namespace Alife.Function.QChat;

public sealed record QChatVisionReadinessStatus(
    bool Ready,
    string Status,
    string Reason,
    string Provider,
    string Model,
    string Endpoint,
    bool PublicUrlRequired,
    bool ApiKeyConfigured,
    int MaxImagesPerMessage);

public static class QChatVisionReadiness
{
    public static QChatVisionReadinessStatus Evaluate(
        QChatConfig config,
        Func<string?>? apiKeyResolver = null)
    {
        ArgumentNullException.ThrowIfNull(config);

        string provider = Normalize(config.ImageRecognitionProvider, "agnes");
        string model = Normalize(config.AgnesVisionModel, "agnes-2.0-flash");
        string endpoint = Normalize(config.AgnesVisionApiEndpoint, "https://apihub.agnes-ai.com/v1/chat/completions");
        int maxImages = Math.Max(1, config.MaxImagesPerMessage);
        bool apiKeyConfigured = string.IsNullOrWhiteSpace(apiKeyResolver?.Invoke() ?? config.AgnesVisionApiKey) == false;

        if (config.EnableImageRecognition == false)
        {
            return new QChatVisionReadinessStatus(
                Ready: false,
                Status: "disabled",
                Reason: "image_recognition_disabled",
                Provider: provider,
                Model: model,
                Endpoint: endpoint,
                PublicUrlRequired: true,
                ApiKeyConfigured: apiKeyConfigured,
                MaxImagesPerMessage: maxImages);
        }

        if (apiKeyConfigured == false)
        {
            return new QChatVisionReadinessStatus(
                Ready: false,
                Status: "missing_api_key",
                Reason: "agnes_api_key_missing",
                Provider: provider,
                Model: model,
                Endpoint: endpoint,
                PublicUrlRequired: true,
                ApiKeyConfigured: false,
                MaxImagesPerMessage: maxImages);
        }

        if (Uri.TryCreate(endpoint, UriKind.Absolute, out Uri? endpointUri) == false ||
            (endpointUri.Scheme != Uri.UriSchemeHttp && endpointUri.Scheme != Uri.UriSchemeHttps))
        {
            return new QChatVisionReadinessStatus(
                Ready: false,
                Status: "invalid_endpoint",
                Reason: "agnes_endpoint_invalid",
                Provider: provider,
                Model: model,
                Endpoint: endpoint,
                PublicUrlRequired: true,
                ApiKeyConfigured: true,
                MaxImagesPerMessage: maxImages);
        }

        return new QChatVisionReadinessStatus(
            Ready: true,
            Status: "ready",
            Reason: "ready",
            Provider: provider,
            Model: model,
            Endpoint: endpoint,
            PublicUrlRequired: true,
            ApiKeyConfigured: true,
            MaxImagesPerMessage: maxImages);
    }

    static string Normalize(string? value, string fallback)
    {
        return string.IsNullOrWhiteSpace(value) ? fallback : value.Trim();
    }
}
