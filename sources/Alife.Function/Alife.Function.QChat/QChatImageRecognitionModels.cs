using System;
using System.Threading;
using System.Threading.Tasks;

namespace Alife.Function.QChat;

public enum QChatImageSourceKind
{
    PublicUrl,
    MissingUrl,
    Unsupported
}

public enum QChatImageRecognitionAction
{
    Skip,
    Analyze
}

public enum QChatImageRecognitionFailureKind
{
    None,
    Disabled,
    NoImages,
    MissingApiKey,
    MissingPublicUrl,
    PolicySkipped,
    TooManyImages,
    HttpError,
    Timeout,
    InvalidResponse
}

public sealed record QChatImageCandidate(
    string Segment,
    string? Url,
    string? File,
    string? Summary)
{
    public QChatImageSourceKind SourceKind =>
        Uri.TryCreate(Url, UriKind.Absolute, out Uri? uri) &&
        (uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeHttps)
            ? QChatImageSourceKind.PublicUrl
            : QChatImageSourceKind.MissingUrl;
}

public sealed record QChatImageRecognitionPolicyContext(
    QChatConfig Config,
    QChatSenderRole SenderRole,
    OneBotMessageType MessageType,
    bool IsMentionedOrWoken,
    bool IsPassiveGroupMessage,
    int ImageCount);

public sealed record QChatImageRecognitionPolicyDecision(
    QChatImageRecognitionAction Action,
    string Reason,
    int MaxImages);

public sealed record QChatImageRecognitionProviderRequest(
    string ImageUrl,
    string Prompt,
    string Model,
    int MaxTokens,
    string? ApiEndpoint = null);

public sealed record QChatImageRecognitionTokenUsage(
    int? PromptTokens,
    int? CompletionTokens,
    int? TotalTokens);

public sealed record QChatImageRecognitionProviderResult(
    bool Success,
    string ProviderName,
    string Model,
    string Content,
    QChatImageRecognitionFailureKind FailureKind,
    string FailureReason,
    QChatImageRecognitionTokenUsage? Usage = null)
{
    public static QChatImageRecognitionProviderResult Ok(
        string providerName,
        string model,
        string content,
        QChatImageRecognitionTokenUsage? usage = null)
    {
        return new QChatImageRecognitionProviderResult(
            true,
            providerName,
            model,
            content,
            QChatImageRecognitionFailureKind.None,
            string.Empty,
            usage);
    }

    public static QChatImageRecognitionProviderResult Fail(
        string providerName,
        string model,
        QChatImageRecognitionFailureKind failureKind,
        string failureReason,
        QChatImageRecognitionTokenUsage? usage = null)
    {
        return new QChatImageRecognitionProviderResult(
            false,
            providerName,
            model,
            string.Empty,
            failureKind,
            failureReason,
            usage);
    }
}

public sealed record QChatImageRecognitionContext(
    QChatConfig Config,
    OneBotMessageEvent MessageEvent,
    QChatSenderRole SenderRole,
    bool IsMentionedOrWoken,
    bool IsPassiveGroupMessage,
    QChatVisionProfile? VisionProfile = null);

public interface IQChatImageRecognitionClient
{
    string ProviderName { get; }

    Task<QChatImageRecognitionProviderResult> AnalyzeAsync(
        QChatImageRecognitionProviderRequest request,
        CancellationToken cancellationToken = default);
}
