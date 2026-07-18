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
            ("syn_tweet_verson", "1"),
            ("paramstr", "1"),
            ("who", "1"),
            ("hostuin", session.AccountId.ToString()),
            ("con", content),
            ("feedversion", "1"),
            ("ver", "1"),
            ("ugc_right", "1"),
            ("to_sign", "0"),
            ("code_version", "1"),
            ("format", "json"),
            ("qzreferrer", $"https://user.qzone.qq.com/{session.AccountId}"),
        ]);
    }

    public async Task<QZoneUploadedImage> UploadImage(QZoneImageUpload upload)
    {
        if (upload is null
            || upload.Bytes is null
            || upload.Bytes.Length == 0
            || string.IsNullOrWhiteSpace(upload.FileName)
            || string.IsNullOrWhiteSpace(upload.ContentType))
            throw new QZoneHttpException("qzone_image_upload_unavailable");

        QZoneSession session = await sessionProvider.GetSessionAsync();
        if (TryGetCookieValue(session.Cookies, "skey", out string? skey) == false
            || TryGetCookieValue(session.Cookies, "p_skey", out string? pSkey) == false)
            throw new QZoneHttpException("qzone_image_upload_session_unavailable");

        string accountId = session.AccountId.ToString();
        List<KeyValuePair<string, string>> form =
        [
            new("filename", upload.FileName),
            new("filetype", upload.ContentType),
            new("uploadtype", "1"),
            new("albumtype", "7"),
            new("skey", skey),
            new("p_skey", pSkey),
            new("uin", accountId),
            new("p_uin", accountId),
            new("zzpaneluin", accountId),
            new("refer", "shuoshuo"),
            new("output_type", "json"),
            new("base64", "1"),
            new("picfile", Convert.ToBase64String(upload.Bytes)),
        ];

        using HttpRequestMessage request = CreateRequest(session, HttpMethod.Post, BuildUploadUrl(session.Bkn));
        request.Content = new FormUrlEncodedContent(form);
        JsonElement root = await SendQZoneRequestAsync(request);
        return TryReadUploadedImage(root, out QZoneUploadedImage? image)
            ? image
            : throw new QZoneHttpException("qzone_api_invalid_response");
    }

    public async Task PublishImagePost(string content, IReadOnlyList<QZoneUploadedImage> images)
    {
        if (images is null || images.Count == 0)
            throw new QZoneHttpException("qzone_image_upload_unavailable");

        string richValue = BuildRichValue(images);
        string pictureBo = BuildPictureBo(images);
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
            ("richtype", "1"),
            ("richflag", "1"),
            ("richval", richValue),
            ("pic_bo", pictureBo),
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
                || code.ValueKind != JsonValueKind.Number
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

    static string BuildUploadUrl(string bkn) => $"{UploadUrl}?g_tk={Uri.EscapeDataString(bkn)}";

    static bool TryGetCookieValue(string? cookies, string name, [NotNullWhen(true)] out string? value)
    {
        value = null;
        if (string.IsNullOrWhiteSpace(cookies))
            return false;

        foreach (string item in cookies.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            int separator = item.IndexOf('=');
            if (separator <= 0 || string.Equals(item[..separator].Trim(), name, StringComparison.Ordinal) == false)
                continue;

            string candidate = item[(separator + 1)..].Trim();
            if (string.IsNullOrWhiteSpace(candidate) == false)
            {
                value = candidate;
                return true;
            }
        }

        return false;
    }

    static bool TryReadUploadedImage(JsonElement root, [NotNullWhen(true)] out QZoneUploadedImage? image)
    {
        image = null;
        if (root.ValueKind != JsonValueKind.Object
            || root.TryGetProperty("data", out JsonElement data) == false
            || data.ValueKind != JsonValueKind.Object
            || TryGetString(data, ["albumid"], out string? albumId) == false
            || TryGetString(data, ["lloc"], out string? lloc) == false
            || TryGetString(data, ["sloc"], out string? sloc) == false
            || TryGetInt32(data, ["width"], out int width) == false
            || TryGetInt32(data, ["height"], out int height) == false
            || TryGetInt32(data, ["type"], out int type) == false
            || TryGetString(data, ["url"], out string? url) == false
            || string.IsNullOrWhiteSpace(albumId)
            || string.IsNullOrWhiteSpace(lloc)
            || string.IsNullOrWhiteSpace(sloc)
            || string.IsNullOrWhiteSpace(url)
            || width <= 0 || height <= 0 || type < 0)
            return false;

        image = new QZoneUploadedImage(albumId, lloc, sloc, width, height, type, url);
        return true;
    }

    static string BuildRichValue(IReadOnlyList<QZoneUploadedImage> images)
    {
        return string.Join('\t', images.Select(image =>
        {
            EnsureUploadedImage(image);
            return $",{image.AlbumId},{image.Lloc},{image.Sloc},{image.Type},{image.Height},{image.Width},,{image.Height},{image.Width}";
        }));
    }

    static string BuildPictureBo(IReadOnlyList<QZoneUploadedImage> images)
    {
        return string.Join('\t', images.Select(image =>
        {
            EnsureUploadedImage(image);
            if (Uri.TryCreate(image.Url, UriKind.Absolute, out Uri? uri) == false)
                throw new QZoneHttpException("qzone_image_upload_unavailable");

            foreach (string pair in uri.Query.TrimStart('?').Split('&', StringSplitOptions.RemoveEmptyEntries))
            {
                int separator = pair.IndexOf('=');
                string key = separator < 0 ? pair : pair[..separator];
                if (string.Equals(Uri.UnescapeDataString(key), "bo", StringComparison.Ordinal) == false)
                    continue;

                string value = separator < 0 ? string.Empty : Uri.UnescapeDataString(pair[(separator + 1)..]);
                if (string.IsNullOrWhiteSpace(value) == false)
                    return value;
            }

            throw new QZoneHttpException("qzone_image_upload_unavailable");
        }));
    }

    static void EnsureUploadedImage(QZoneUploadedImage? image)
    {
        if (image is null
            || string.IsNullOrWhiteSpace(image.AlbumId)
            || string.IsNullOrWhiteSpace(image.Lloc)
            || string.IsNullOrWhiteSpace(image.Sloc)
            || string.IsNullOrWhiteSpace(image.Url)
            || image.Width <= 0 || image.Height <= 0 || image.Type < 0)
            throw new QZoneHttpException("qzone_image_upload_unavailable");
    }

    static string UnwrapJsonp(string body)
    {
        ReadOnlySpan<char> trimmedStart = body.AsSpan().TrimStart();
        if (trimmedStart.StartsWith("{", StringComparison.Ordinal) || trimmedStart.StartsWith("[", StringComparison.Ordinal))
            return body;

        string payload = body.Trim();
        int openingParenthesis = payload.IndexOf('(');
        int closingParenthesis = payload.LastIndexOf(')');
        if (openingParenthesis <= 0
            || closingParenthesis <= openingParenthesis
            || !IsJsonpCallbackPath(payload.AsSpan(0, openingParenthesis)))
            throw new JsonException();

        ReadOnlySpan<char> suffix = payload.AsSpan(closingParenthesis + 1).Trim();
        if (!suffix.IsEmpty && !suffix.SequenceEqual(";"))
            throw new JsonException();

        return payload[(openingParenthesis + 1)..closingParenthesis];
    }

    static bool IsJsonpCallbackPath(ReadOnlySpan<char> path)
    {
        bool requiresIdentifierStart = true;
        foreach (char character in path)
        {
            if (requiresIdentifierStart)
            {
                if (!IsJsonpIdentifierStart(character))
                    return false;

                requiresIdentifierStart = false;
            }
            else if (character == '.')
            {
                requiresIdentifierStart = true;
            }
            else if (!IsJsonpIdentifierPart(character))
            {
                return false;
            }
        }

        return !requiresIdentifierStart;
    }

    static bool IsJsonpIdentifierStart(char character)
    {
        return char.IsLetter(character) || character is '_' or '$';
    }

    static bool IsJsonpIdentifierPart(char character)
    {
        return IsJsonpIdentifierStart(character) || char.IsDigit(character);
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

    static bool TryGetInt32(JsonElement element, IReadOnlyList<string> propertyNames, out int value)
    {
        value = default;
        foreach (string propertyName in propertyNames)
        {
            if (element.ValueKind == JsonValueKind.Object
                && element.TryGetProperty(propertyName, out JsonElement property)
                && property.ValueKind == JsonValueKind.Number
                && property.TryGetInt32(out value))
                return true;
        }

        return false;
    }

}
