namespace Alife.Function.QChat;

public enum QChatMediaSourceKind
{
    OneBotFile,
    RemoteUrl,
    ManagedLocalFile
}

public sealed record QChatMediaSource(
    QChatMediaSourceKind Kind,
    string Id,
    string OriginalName,
    string? Url,
    string? LocalPath,
    long? Size);
