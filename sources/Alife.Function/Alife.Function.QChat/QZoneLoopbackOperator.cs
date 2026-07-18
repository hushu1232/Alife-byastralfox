using System;
using System.IO;
using System.Net;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;

namespace Alife.Function.QChat;

[JsonConverter(typeof(JsonStringEnumConverter))]
public enum QZoneLoopbackOperatorOperation
{
    Read,
    Post,
    Comment,
    Like,
    Image,
    Delete,
}

[JsonConverter(typeof(JsonStringEnumConverter))]
public enum QZoneLoopbackOperatorResultCode
{
    Accepted,
    InvalidOperation,
    InvalidEndpoint,
    InvalidRequest,
    Completed,
    OperationRejected,
    OperationFailed,
}

public sealed record QZoneLoopbackOperatorRequest
{
    [JsonPropertyName("operation")]
    public QZoneLoopbackOperatorOperation? Operation { get; init; }

    [JsonPropertyName("target_id")]
    public long? TargetId { get; init; }

    [JsonPropertyName("post_id")]
    public string? PostId { get; init; }

    [JsonPropertyName("comment_id")]
    public string? CommentId { get; init; }

    [JsonPropertyName("content")]
    public string? Content { get; init; }

    [JsonPropertyName("comment_count")]
    public int CommentCount { get; init; } = 20;

    [JsonPropertyName("source_kind")]
    public string? SourceKind { get; init; }

    [JsonPropertyName("source_value")]
    public string? SourceValue { get; init; }

    [JsonPropertyName("topic_id")]
    public string? TopicId { get; init; }

    [JsonPropertyName("feeds_key")]
    public string? FeedsKey { get; init; }

    [JsonPropertyName("created_at")]
    public long? CreatedAtUnixSeconds { get; init; }

    public QZoneLoopbackOperatorResult Validate()
    {
        return Operation is { } operation && Enum.IsDefined(operation)
            ? QZoneLoopbackOperatorResult.Accepted()
            : QZoneLoopbackOperatorResult.Rejected(QZoneLoopbackOperatorResultCode.InvalidOperation);
    }
}

public sealed record QZoneLoopbackOperatorResult(
    [property: JsonPropertyName("succeeded")] bool Succeeded,
    [property: JsonPropertyName("code")] QZoneLoopbackOperatorResultCode Code)
{
    public static QZoneLoopbackOperatorResult Accepted() =>
        new(true, QZoneLoopbackOperatorResultCode.Accepted);

    public static QZoneLoopbackOperatorResult Completed() =>
        new(true, QZoneLoopbackOperatorResultCode.Completed);

    public static QZoneLoopbackOperatorResult OperationRejected() =>
        Rejected(QZoneLoopbackOperatorResultCode.OperationRejected);

    public static QZoneLoopbackOperatorResult OperationFailed() =>
        Rejected(QZoneLoopbackOperatorResultCode.OperationFailed);

    public static QZoneLoopbackOperatorResult Rejected(QZoneLoopbackOperatorResultCode code)
    {
        if (code == QZoneLoopbackOperatorResultCode.Accepted)
            throw new ArgumentOutOfRangeException(nameof(code));

        return new QZoneLoopbackOperatorResult(false, code);
    }
}

public sealed class QZoneLoopbackOperatorHost : IAsyncDisposable
{
    const int MaximumRequestBytes = 64 * 1024;

    readonly HttpListener listener = new();
    readonly CancellationTokenSource shutdown = new();
    readonly QZoneService qzoneService;
    readonly object sync = new();
    Task? listenerTask;
    bool disposed;

    public QZoneLoopbackOperatorHost(QZoneLoopbackOperatorEndpoint endpoint, QZoneService qzoneService)
    {
        Endpoint = endpoint ?? throw new ArgumentNullException(nameof(endpoint));
        this.qzoneService = qzoneService ?? throw new ArgumentNullException(nameof(qzoneService));
        listener.Prefixes.Add(endpoint.Uri.AbsoluteUri);
    }

    public QZoneLoopbackOperatorEndpoint Endpoint { get; }
    public bool IsRunning => listener.IsListening;

    public Task StartAsync()
    {
        lock (sync)
        {
            ObjectDisposedException.ThrowIf(disposed, this);
            if (listener.IsListening)
                return Task.CompletedTask;

            listener.Start();
            listenerTask = ListenAsync(shutdown.Token);
            return Task.CompletedTask;
        }
    }

    public async ValueTask DisposeAsync()
    {
        Task? activeListener;
        lock (sync)
        {
            if (disposed)
                return;

            disposed = true;
            shutdown.Cancel();
            listener.Close();
            activeListener = listenerTask;
        }

        try
        {
            if (activeListener != null)
                await activeListener.ConfigureAwait(false);
        }
        finally
        {
            shutdown.Dispose();
        }
    }

    async Task ListenAsync(CancellationToken cancellationToken)
    {
        while (cancellationToken.IsCancellationRequested == false)
        {
            HttpListenerContext context;
            try
            {
                context = await listener.GetContextAsync().WaitAsync(cancellationToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                return;
            }
            catch (HttpListenerException) when (cancellationToken.IsCancellationRequested || disposed)
            {
                return;
            }
            catch (ObjectDisposedException) when (cancellationToken.IsCancellationRequested || disposed)
            {
                return;
            }

            await HandleAsync(context, cancellationToken).ConfigureAwait(false);
        }
    }

    async Task HandleAsync(HttpListenerContext context, CancellationToken cancellationToken)
    {
        QZoneLoopbackOperatorResult result;
        try
        {
            if (context.Request.HttpMethod.Equals("POST", StringComparison.OrdinalIgnoreCase) == false)
            {
                result = QZoneLoopbackOperatorResult.Rejected(QZoneLoopbackOperatorResultCode.InvalidRequest);
            }
            else
            {
                QZoneLoopbackOperatorRequest? request = await ReadRequestAsync(context.Request, cancellationToken).ConfigureAwait(false);
                result = request?.Validate() is { Succeeded: true }
                    ? await DispatchAsync(request)
                    : QZoneLoopbackOperatorResult.Rejected(QZoneLoopbackOperatorResultCode.InvalidRequest);
            }
        }
        catch (JsonException)
        {
            result = QZoneLoopbackOperatorResult.Rejected(QZoneLoopbackOperatorResultCode.InvalidRequest);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            return;
        }
        catch (Exception)
        {
            result = QZoneLoopbackOperatorResult.OperationFailed();
        }

        try
        {
            byte[] payload = JsonSerializer.SerializeToUtf8Bytes(result);
            context.Response.StatusCode = (int)HttpStatusCode.OK;
            context.Response.ContentType = "application/json";
            context.Response.ContentLength64 = payload.Length;
            await context.Response.OutputStream.WriteAsync(payload, cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            return;
        }
        catch (HttpListenerException) when (cancellationToken.IsCancellationRequested || disposed)
        {
            return;
        }
        catch (ObjectDisposedException) when (cancellationToken.IsCancellationRequested || disposed)
        {
            return;
        }
        finally
        {
            context.Response.Close();
        }
    }

    async Task<QZoneLoopbackOperatorResult> DispatchAsync(QZoneLoopbackOperatorRequest request)
    {
        try
        {
            bool completed = request.Operation switch
            {
                QZoneLoopbackOperatorOperation.Read => await ReadAsync(request).ConfigureAwait(false),
                QZoneLoopbackOperatorOperation.Post => await PostAsync(request).ConfigureAwait(false),
                QZoneLoopbackOperatorOperation.Comment => await CommentAsync(request).ConfigureAwait(false),
                QZoneLoopbackOperatorOperation.Like => await LikeAsync(request).ConfigureAwait(false),
                QZoneLoopbackOperatorOperation.Image => await ImageAsync(request).ConfigureAwait(false),
                QZoneLoopbackOperatorOperation.Delete => await DeleteAsync(request).ConfigureAwait(false),
                _ => false,
            };
            return completed
                ? QZoneLoopbackOperatorResult.Completed()
                : QZoneLoopbackOperatorResult.OperationRejected();
        }
        catch (ArgumentException)
        {
            return QZoneLoopbackOperatorResult.Rejected(QZoneLoopbackOperatorResultCode.InvalidRequest);
        }
        catch (Exception)
        {
            return QZoneLoopbackOperatorResult.OperationFailed();
        }
    }

    async Task<bool> ReadAsync(QZoneLoopbackOperatorRequest request)
    {
        if (request.TargetId is not > 0 || request.CommentCount is < 0 or > 50)
            throw new ArgumentException(nameof(request));

        QZoneQueryResult result = await qzoneService.QZoneLatestPostAndComments(request.TargetId.Value, request.CommentCount).ConfigureAwait(false);
        return result.Succeeded;
    }

    async Task<bool> PostAsync(QZoneLoopbackOperatorRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.Content))
            throw new ArgumentException(nameof(request));

        return (await qzoneService.QZonePost(request.Content).ConfigureAwait(false)).Executed;
    }

    async Task<bool> CommentAsync(QZoneLoopbackOperatorRequest request)
    {
        if (request.TargetId is not > 0 ||
            string.IsNullOrWhiteSpace(request.PostId) ||
            string.IsNullOrWhiteSpace(request.Content))
        {
            throw new ArgumentException(nameof(request));
        }

        return (await qzoneService.QZoneComment(request.TargetId.Value, request.PostId, request.Content).ConfigureAwait(false)).Executed;
    }

    async Task<bool> LikeAsync(QZoneLoopbackOperatorRequest request)
    {
        if (request.TargetId is not > 0 || string.IsNullOrWhiteSpace(request.PostId))
            throw new ArgumentException(nameof(request));

        return (await qzoneService.QZoneLike(request.TargetId.Value, request.PostId).ConfigureAwait(false)).Executed;
    }

    async Task<bool> ImageAsync(QZoneLoopbackOperatorRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.Content) ||
            string.IsNullOrWhiteSpace(request.SourceKind) ||
            string.IsNullOrWhiteSpace(request.SourceValue))
        {
            throw new ArgumentException(nameof(request));
        }

        return (await qzoneService.QZonePostImage(request.Content, request.SourceKind, request.SourceValue).ConfigureAwait(false)).Executed;
    }

    async Task<bool> DeleteAsync(QZoneLoopbackOperatorRequest request)
    {
        if (request.TargetId is not > 0 ||
            string.IsNullOrWhiteSpace(request.PostId) ||
            string.IsNullOrWhiteSpace(request.TopicId) ||
            string.IsNullOrWhiteSpace(request.FeedsKey) ||
            request.CreatedAtUnixSeconds is null)
        {
            throw new ArgumentException(nameof(request));
        }

        QZonePostSnapshot post = new(
            request.PostId,
            request.TargetId.Value,
            string.Empty,
            request.TopicId,
            request.FeedsKey,
            request.CreatedAtUnixSeconds);
        return (await qzoneService.QZoneDeleteOwnPost(post).ConfigureAwait(false)).Executed;
    }

    static async Task<QZoneLoopbackOperatorRequest?> ReadRequestAsync(
        HttpListenerRequest request,
        CancellationToken cancellationToken)
    {
        if (request.ContentLength64 > MaximumRequestBytes)
            return null;

        byte[] buffer = new byte[8192];
        using MemoryStream body = new();
        while (true)
        {
            int read = await request.InputStream.ReadAsync(buffer.AsMemory(), cancellationToken).ConfigureAwait(false);
            if (read == 0)
                break;
            if (body.Length > MaximumRequestBytes - read)
                return null;

            body.Write(buffer, 0, read);
        }

        return JsonSerializer.Deserialize<QZoneLoopbackOperatorRequest>(body.ToArray());
    }
}

public sealed class QZoneLoopbackOperatorEndpoint
{
    QZoneLoopbackOperatorEndpoint(Uri uri)
    {
        Uri = uri;
    }

    public Uri Uri { get; }

    public static bool TryCreate(
        string? value,
        out QZoneLoopbackOperatorEndpoint? endpoint,
        out QZoneLoopbackOperatorResultCode code)
    {
        if (Uri.TryCreate(value, UriKind.Absolute, out Uri? uri) == false ||
            uri.Scheme != Uri.UriSchemeHttp ||
            IsAllowedHost(uri) == false ||
            string.IsNullOrEmpty(uri.UserInfo) == false ||
            string.IsNullOrEmpty(uri.Query) == false ||
            string.IsNullOrEmpty(uri.Fragment) == false)
        {
            endpoint = null;
            code = QZoneLoopbackOperatorResultCode.InvalidEndpoint;
            return false;
        }

        endpoint = new QZoneLoopbackOperatorEndpoint(NormalizePrefix(uri));
        code = QZoneLoopbackOperatorResultCode.Accepted;
        return true;
    }

    static bool IsAllowedHost(Uri uri)
    {
        return string.Equals(uri.Host, "127.0.0.1", StringComparison.Ordinal) ||
               string.Equals(uri.Host, "localhost", StringComparison.OrdinalIgnoreCase) ||
               (IPAddress.TryParse(uri.Host.Trim('[', ']'), out IPAddress? address) &&
                IPAddress.IPv6Loopback.Equals(address));
    }

    static Uri NormalizePrefix(Uri uri)
    {
        return uri.AbsolutePath.EndsWith("/", StringComparison.Ordinal)
            ? uri
            : new Uri(uri.AbsoluteUri + "/", UriKind.Absolute);
    }
}
