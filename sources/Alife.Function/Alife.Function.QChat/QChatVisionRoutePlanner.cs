using System;

namespace Alife.Function.QChat;

public sealed record QChatVisionRoutePlan(
    string PrimaryProvider,
    string? FallbackProvider,
    string Reason,
    TimeSpan TotalTimeout);

public static class QChatVisionRoutePlanner
{
    static readonly string[] OcrKeywords =
    [
        "ocr", "read text", "extract text", "read the screenshot", "图片里的文字", "图中文字", "读出", "识别文字"
    ];

    static readonly string[] UiOrCodeKeywords =
    [
        "screenshot", "table", "chart", "code", "error", "ui", "截图", "表格", "图表", "代码", "报错", "界面", "流程图"
    ];

    public static QChatVisionRoutePlan Plan(
        QChatVisionProfile profile,
        string? userText,
        QChatVisionProviderCatalog? catalog = null,
        TimeSpan? totalTimeout = null)
    {
        ArgumentNullException.ThrowIfNull(profile);

        string primary = Normalize(profile.PrimaryProvider, profile.Provider, "agnes");
        string complexProvider = Normalize(profile.ComplexRequestProvider, primary);
        string reason = "default_image";
        bool directComplex = false;
        if (ContainsAny(userText, OcrKeywords))
        {
            primary = complexProvider;
            reason = "complex_ocr";
            directComplex = true;
        }
        else if (ContainsAny(userText, UiOrCodeKeywords))
        {
            primary = complexProvider;
            reason = "complex_ui_or_code";
            directComplex = true;
        }

        string? fallback = directComplex ? null : NormalizeOptional(profile.FallbackProvider);
        if (string.Equals(primary, fallback, StringComparison.OrdinalIgnoreCase) ||
            IsEnabled(catalog, fallback) == false)
        {
            fallback = null;
        }

        return new QChatVisionRoutePlan(
            primary,
            fallback,
            reason,
            totalTimeout.GetValueOrDefault(TimeSpan.FromSeconds(12)));
    }

    public static bool ShouldFallback(QChatImageRecognitionFailureKind failureKind)
    {
        return failureKind is QChatImageRecognitionFailureKind.MissingApiKey or
            QChatImageRecognitionFailureKind.Timeout or
            QChatImageRecognitionFailureKind.HttpError or
            QChatImageRecognitionFailureKind.InvalidResponse;
    }

    static bool ContainsAny(string? text, string[] keywords)
    {
        if (string.IsNullOrWhiteSpace(text))
            return false;

        foreach (string keyword in keywords)
        {
            if (text.Contains(keyword, StringComparison.OrdinalIgnoreCase))
                return true;
        }

        return false;
    }

    static bool IsEnabled(QChatVisionProviderCatalog? catalog, string? providerId)
    {
        if (catalog == null || string.IsNullOrWhiteSpace(providerId))
            return string.IsNullOrWhiteSpace(providerId) == false;

        QChatVisionProviderSettings? provider = catalog.Find(providerId);
        return provider == null || provider.Enabled;
    }

    static string Normalize(string? preferred, string? fallback, string defaultValue)
    {
        return string.IsNullOrWhiteSpace(preferred)
            ? (string.IsNullOrWhiteSpace(fallback) ? defaultValue : fallback.Trim())
            : preferred.Trim();
    }

    static string Normalize(string? preferred, string fallback) => Normalize(preferred, fallback, fallback);

    static string? NormalizeOptional(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
