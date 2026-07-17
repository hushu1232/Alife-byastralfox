using System;
using System.Collections.Generic;

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
    int MaxImagesPerMessage,
    string? FallbackProvider = null,
    string? FallbackModel = null,
    bool FallbackApiKeyConfigured = false);

public static class QChatVisionReadiness
{
    public static QChatVisionReadinessStatus Evaluate(
        QChatConfig config,
        QChatVisionProfile profile,
        IReadOnlyDictionary<string, Func<string?>> apiKeyResolvers)
    {
        ArgumentNullException.ThrowIfNull(config);
        ArgumentNullException.ThrowIfNull(profile);
        ArgumentNullException.ThrowIfNull(apiKeyResolvers);

        QChatVisionProviderCatalog catalog = config.VisionProviders ?? QChatVisionProviderCatalog.CreateDefault();
        string primaryId = Normalize(profile.PrimaryProvider, Normalize(profile.Provider, "agnes"));
        string? fallbackId = string.IsNullOrWhiteSpace(profile.FallbackProvider) ||
            string.Equals(primaryId, profile.FallbackProvider.Trim(), StringComparison.OrdinalIgnoreCase)
            ? null
            : profile.FallbackProvider.Trim();
        QChatVisionProviderSettings primary = catalog.Find(primaryId) ?? new QChatVisionProviderSettings
        {
            ProviderId = primaryId,
            Model = profile.Model,
            ApiEndpoint = profile.ApiEndpoint
        };
        QChatVisionProviderSettings? fallback = fallbackId == null ? null : catalog.Find(fallbackId);
        bool primaryKeyConfigured = ResolveApiKey(apiKeyResolvers, primaryId);
        bool fallbackKeyConfigured = fallbackId != null && ResolveApiKey(apiKeyResolvers, fallbackId);
        string model = Normalize(primary.Model, Normalize(profile.Model, config.AgnesVisionModel));
        string endpoint = Normalize(primary.ApiEndpoint, Normalize(profile.ApiEndpoint, config.AgnesVisionApiEndpoint));
        int maxImages = Math.Max(1, profile.MaxImagesPerMessage > 0 ? profile.MaxImagesPerMessage : config.MaxImagesPerMessage);
        string? fallbackModel = fallback == null ? null : Normalize(fallback.Model, "grok-4.5");

        if (config.EnableImageRecognition == false)
            return Status(false, "disabled", "image_recognition_disabled");
        if (primary.Enabled == false)
            return Status(false, "disabled", "primary_provider_disabled");
        if (primaryKeyConfigured == false)
            return Status(false, "missing_api_key", "primary_api_key_missing");
        if (Uri.TryCreate(endpoint, UriKind.Absolute, out Uri? endpointUri) == false ||
            (endpointUri.Scheme != Uri.UriSchemeHttp && endpointUri.Scheme != Uri.UriSchemeHttps))
        {
            return Status(false, "invalid_endpoint", "primary_endpoint_invalid");
        }

        return Status(true, "ready", "ready");

        QChatVisionReadinessStatus Status(bool ready, string status, string reason) => new(
            ready,
            status,
            reason,
            primaryId,
            model,
            endpoint,
            profile.RequiresPublicUrl,
            primaryKeyConfigured,
            maxImages,
            fallbackId,
            fallbackModel,
            fallbackKeyConfigured);
    }

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

    static bool ResolveApiKey(IReadOnlyDictionary<string, Func<string?>> resolvers, string providerId)
    {
        foreach ((string key, Func<string?> resolver) in resolvers)
        {
            if (string.Equals(key, providerId, StringComparison.OrdinalIgnoreCase))
                return string.IsNullOrWhiteSpace(resolver()) == false;
        }

        return false;
    }
}
