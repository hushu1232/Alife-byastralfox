using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using System.Net.Http;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace Alife.Function.QChat;

public sealed class QZoneHttpException(string code) : InvalidOperationException(code)
{
    public string Code { get; } = code;
}

public sealed class QZoneHttpRuntime(IQZoneSessionProvider sessionProvider, HttpClient httpClient) : IQZoneRuntime
{
    public const string FeedListUrl = "https://user.qzone.qq.com/proxy/domain/taotao.qq.com/cgi-bin/emotion_cgi_msglist_v6";
    public const string PublishUrl = "https://user.qzone.qq.com/proxy/domain/taotao.qzone.qq.com/cgi-bin/emotion_cgi_publish_v6";
    public const string CommentUrl = "https://user.qzone.qq.com/proxy/domain/taotao.qzone.qq.com/cgi-bin/emotion_cgi_re_feeds";
    public const string ReplyUrl = "https://user.qzone.qq.com/proxy/domain/taotao.qzone.qq.com/cgi-bin/emotion_cgi_addreply_ugc";
    public const string LikeUrl = "https://user.qzone.qq.com/proxy/domain/w.qzone.qq.com/cgi-bin/likes/internal_dolike_app";
    public const string DeleteUrl = "https://user.qzone.qq.com/proxy/domain/taotao.qzone.qq.com/cgi-bin/emotion_cgi_delete_v6";
    public const string UploadUrl = "https://up.qzone.qq.com/cgi-bin/upload/cgi_upload_image";

    readonly IQZoneSessionProvider sessionProvider = sessionProvider ?? throw new ArgumentNullException(nameof(sessionProvider));
    readonly HttpClient httpClient = httpClient ?? throw new ArgumentNullException(nameof(httpClient));

    public async Task PublishPost(string content)
    {
        QZoneSession session = await sessionProvider.GetSessionAsync();
        await SendFormAsync(session, PublishUrl,
        [
            ("hostuin", session.AccountId.ToString()),
            ("con", content),
            ("feedversion", "1"),
            ("ver", "1"),
            ("ugc_right", "1"),
            ("format", "json"),
            ("qzreferrer", $"https://user.qzone.qq.com/{session.AccountId}"),
        ]);
    }

    public async Task Comment(long targetId, string postId, string content)
    {
        QZoneSession session = await sessionProvider.GetSessionAsync();
        await SendFormAsync(session, CommentUrl,
        [
            ("uin", session.AccountId.ToString()),
            ("hostUin", targetId.ToString()),
            ("topicId", $"{targetId}_{postId}"),
            ("commentUin", session.AccountId.ToString()),
            ("content", content),
            ("format", "json"),
            ("qzreferrer", $"https://user.qzone.qq.com/{targetId}"),
        ]);
    }

    public async Task ReplyComment(long targetId, string postId, string commentId, string content)
    {
        QZoneSession session = await sessionProvider.GetSessionAsync();
        await SendFormAsync(session, ReplyUrl,
        [
            ("uin", session.AccountId.ToString()),
            ("hostUin", targetId.ToString()),
            ("topicId", $"{targetId}_{postId}"),
            ("commentUin", session.AccountId.ToString()),
            ("commentId", commentId),
            ("content", content),
            ("format", "json"),
            ("qzreferrer", $"https://user.qzone.qq.com/{targetId}"),
        ]);
    }

    public async Task LikePost(long targetId, string postId)
    {
        QZoneSession session = await sessionProvider.GetSessionAsync();
        await SendFormAsync(session, LikeUrl,
        [
            ("opuin", session.AccountId.ToString()),
            ("unikey", $"{targetId}_{postId}"),
            ("curkey", postId),
            ("appid", "311"),
            ("format", "json"),
        ]);
    }

    public async Task DeletePost(QZonePostSnapshot post)
    {
        ArgumentNullException.ThrowIfNull(post);

        QZoneSession session = await sessionProvider.GetSessionAsync();
        if (post.TargetId != session.AccountId
            || string.IsNullOrWhiteSpace(post.TopicId)
            || string.IsNullOrWhiteSpace(post.FeedsKey)
            || post.CreatedAtUnixSeconds is null)
            throw new InvalidOperationException("qzone_delete_metadata_unavailable");

        await SendFormAsync(session, DeleteUrl,
        [
            ("uin", session.AccountId.ToString()),
            ("topicId", post.TopicId),
            ("feedsKey", post.FeedsKey),
            ("feedsTime", post.CreatedAtUnixSeconds.Value.ToString()),
            ("feedsType", "0"),
            ("feedsFlag", "0"),
            ("feedsAppid", "311"),
            ("format", "json"),
        ]);
    }

    public async Task<QZonePostSnapshot?> GetLatestPost(long targetId)
    {
        QZoneSession session = await sessionProvider.GetSessionAsync();
        using HttpRequestMessage request = CreateRequest(
            session,
            HttpMethod.Get,
            BuildFeedListUrl(targetId, session.Bkn));
        JsonElement root = await SendQZoneRequestAsync(request);
        return TryReadPost(root);
    }

    public async Task<IReadOnlyList<QZoneCommentSnapshot>> GetLatestComments(long targetId, string postId, int count)
    {
        QZoneSession session = await sessionProvider.GetSessionAsync();
        using HttpRequestMessage request = CreateRequest(
            session,
            HttpMethod.Get,
            BuildFeedListUrl(targetId, session.Bkn));
        JsonElement root = await SendQZoneRequestAsync(request);
        return TryReadComments(root, postId, count);
    }

    public async Task<JsonElement> SendQZoneRequestAsync(
        HttpRequestMessage request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        using HttpResponseMessage response = await httpClient.SendAsync(request, cancellationToken);
        string body = response.Content is null
            ? string.Empty
            : await response.Content.ReadAsStringAsync(cancellationToken);
        if (!response.IsSuccessStatusCode)
            throw new QZoneHttpException($"qzone_http_{(int)response.StatusCode}");

        try
        {
            using JsonDocument document = JsonDocument.Parse(UnwrapJsonp(body));
            JsonElement root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object
                || !root.TryGetProperty("code", out JsonElement code)
                || !code.TryGetInt32(out int resultCode))
                throw new QZoneHttpException("qzone_api_invalid_response");
            if (resultCode != 0)
                throw new QZoneHttpException($"qzone_api_{resultCode}");

            return root.Clone();
        }
        catch (JsonException)
        {
            throw new QZoneHttpException("qzone_api_invalid_response");
        }
    }

    async Task SendFormAsync(
        QZoneSession session,
        string url,
        IReadOnlyList<(string Key, string Value)> fields)
    {
        List<KeyValuePair<string, string>> form = fields
            .Select(field => new KeyValuePair<string, string>(field.Key, field.Value))
            .ToList();
        form.Add(new KeyValuePair<string, string>("g_tk", session.Bkn));

        using HttpRequestMessage request = CreateRequest(session, HttpMethod.Post, url);
        request.Content = new FormUrlEncodedContent(form);
        await SendQZoneRequestAsync(request);
    }

    static HttpRequestMessage CreateRequest(QZoneSession session, HttpMethod method, string url)
    {
        HttpRequestMessage request = new(method, url);
        request.Headers.TryAddWithoutValidation("Cookie", session.Cookies);
        return request;
    }

    static string BuildFeedListUrl(long targetId, string bkn)
    {
        return $"{FeedListUrl}?uin={Uri.EscapeDataString(targetId.ToString())}" +
            "&pos=0&num=1&replynum=20&format=json" +
            $"&g_tk={Uri.EscapeDataString(bkn)}";
    }

    static string UnwrapJsonp(string body)
    {
        string payload = body.Trim();
        if (payload.StartsWith("{", StringComparison.Ordinal) || payload.StartsWith("[", StringComparison.Ordinal))
            return payload;

        int openingParenthesis = payload.IndexOf('(');
        int closingParenthesis = payload.LastIndexOf(')');
        return openingParenthesis >= 0 && closingParenthesis > openingParenthesis
            ? payload[(openingParenthesis + 1)..closingParenthesis]
            : payload;
    }

    static QZonePostSnapshot? TryReadPost(JsonElement root)
    {
        if (!TryGetFirstMessage(root, out JsonElement message)
            || !TryGetString(message, ["tid"], out string? postId)
            || !TryGetInt64(message, ["uin"], out long targetId)
            || !TryGetString(message, ["content"], out string? content))
            return null;

        TryGetString(message, ["topicId", "topic_id"], out string? topicId);
        TryGetString(message, ["feedsKey", "feeds_key"], out string? feedsKey);
        long? createdAt = TryGetInt64(message, ["created_time", "createdTime"], out long createdTime)
            ? createdTime
            : null;

        return new QZonePostSnapshot(postId, targetId, content, topicId, feedsKey, createdAt);
    }

    static IReadOnlyList<QZoneCommentSnapshot> TryReadComments(JsonElement root, string postId, int count)
    {
        if (!TryGetMessage(root, postId, out JsonElement message)
            || !TryGetArray(message, "commentlist", out JsonElement comments))
            return [];

        List<QZoneCommentSnapshot> snapshots = [];
        foreach (JsonElement comment in comments.EnumerateArray())
        {
            if (!TryGetString(comment, ["id", "commentId", "comment_id"], out string? commentId)
                || !TryGetInt64(comment, ["uin"], out long userId)
                || !TryGetString(comment, ["content"], out string? content))
                continue;

            TryGetString(comment, ["topicId", "topic_id"], out string? topicId);
            TryGetString(comment, ["parentCommentId", "parent_comment_id", "parentId", "parent_id"], out string? parentCommentId);
            snapshots.Add(new QZoneCommentSnapshot(commentId, userId, content, topicId, parentCommentId));
        }

        return snapshots.Take(Math.Max(count, 0)).ToArray();
    }

    static bool TryGetFirstMessage(JsonElement root, out JsonElement message)
    {
        message = default;
        return TryGetMessageList(root, out JsonElement messages)
            && messages.GetArrayLength() > 0
            && (message = messages[0]).ValueKind == JsonValueKind.Object;
    }

    static bool TryGetMessage(JsonElement root, string postId, out JsonElement message)
    {
        message = default;
        if (!TryGetMessageList(root, out JsonElement messages))
            return false;

        foreach (JsonElement candidate in messages.EnumerateArray())
        {
            if (candidate.ValueKind == JsonValueKind.Object
                && TryGetString(candidate, ["tid"], out string? candidatePostId)
                && string.Equals(candidatePostId, postId, StringComparison.Ordinal))
            {
                message = candidate;
                return true;
            }
        }

        return false;
    }

    static bool TryGetMessageList(JsonElement root, out JsonElement messages)
    {
        messages = default;
        return root.ValueKind == JsonValueKind.Object
            && root.TryGetProperty("data", out JsonElement data)
            && data.ValueKind == JsonValueKind.Object
            && data.TryGetProperty("msglist", out messages)
            && messages.ValueKind == JsonValueKind.Array;
    }

    static bool TryGetArray(JsonElement element, string propertyName, out JsonElement values)
    {
        values = default;
        return element.ValueKind == JsonValueKind.Object
            && element.TryGetProperty(propertyName, out values)
            && values.ValueKind == JsonValueKind.Array;
    }

    static bool TryGetString(
        JsonElement element,
        IReadOnlyList<string> propertyNames,
        [NotNullWhen(true)] out string? value)
    {
        value = null;
        foreach (string propertyName in propertyNames)
        {
            if (element.ValueKind == JsonValueKind.Object
                && element.TryGetProperty(propertyName, out JsonElement property)
                && property.ValueKind == JsonValueKind.String
                && (value = property.GetString()) is not null)
                return true;
        }

        return false;
    }

    static bool TryGetInt64(JsonElement element, IReadOnlyList<string> propertyNames, out long value)
    {
        value = default;
        foreach (string propertyName in propertyNames)
        {
            if (element.ValueKind == JsonValueKind.Object
                && element.TryGetProperty(propertyName, out JsonElement property)
                && property.ValueKind == JsonValueKind.Number
                && property.TryGetInt64(out value))
                return true;
        }

        return false;
    }
}
