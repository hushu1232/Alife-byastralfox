using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace Alife.Function.QChat;

public interface IQZoneEphemeralCookieProvider
{
    Task<string> GetCookieAsync(CancellationToken cancellationToken = default);
}

public sealed class QZoneCookieRuntime : IQZoneRuntime
{
    const string WritesUnavailableMessage = "QZone writes are unavailable in first phase.";

    readonly IQZoneEphemeralCookieProvider cookieProvider;
    readonly HttpClient httpClient;

    public QZoneCookieRuntime(IQZoneEphemeralCookieProvider cookieProvider, HttpMessageHandler handler)
    {
        ArgumentNullException.ThrowIfNull(cookieProvider);
        ArgumentNullException.ThrowIfNull(handler);

        this.cookieProvider = cookieProvider;
        httpClient = new HttpClient(handler, disposeHandler: false)
        {
            BaseAddress = new Uri("https://qzone.invalid/"),
        };
    }

    public async Task<QZonePostSnapshot?> GetLatestPost(long targetId)
    {
        using HttpRequestMessage request = await CreateRequestAsync($"latest-post?targetId={targetId}");
        using HttpResponseMessage response = await httpClient.SendAsync(request);
        if (!response.IsSuccessStatusCode)
            return null;

        try
        {
            await using Stream payload = await response.Content.ReadAsStreamAsync();
            using JsonDocument document = await JsonDocument.ParseAsync(payload);
            return TryReadPost(document.RootElement);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    public async Task<IReadOnlyList<QZoneCommentSnapshot>> GetLatestComments(long targetId, string postId, int count)
    {
        string escapedPostId = Uri.EscapeDataString(postId);
        using HttpRequestMessage request = await CreateRequestAsync(
            $"comments?targetId={targetId}&postId={escapedPostId}&count={count}");
        using HttpResponseMessage response = await httpClient.SendAsync(request);
        if (!response.IsSuccessStatusCode)
            return [];

        try
        {
            await using Stream payload = await response.Content.ReadAsStreamAsync();
            using JsonDocument document = await JsonDocument.ParseAsync(payload);
            return TryReadComments(document.RootElement, count);
        }
        catch (JsonException)
        {
            return [];
        }
    }

    public Task PublishPost(string content) => throw WriteUnavailable();

    public Task Comment(long targetId, string postId, string content) => throw WriteUnavailable();

    public Task ReplyComment(long targetId, string postId, string commentId, string content) => throw WriteUnavailable();

    public Task LikePost(long targetId, string postId) => throw WriteUnavailable();

    public string GetAuditSafeState() => "QZoneCookieRuntime: read-only first phase.";

    async Task<HttpRequestMessage> CreateRequestAsync(string relativeUri)
    {
        string cookie = await cookieProvider.GetCookieAsync();
        HttpRequestMessage request = new(HttpMethod.Get, relativeUri);
        request.Headers.TryAddWithoutValidation("Cookie", cookie);
        return request;
    }

    static InvalidOperationException WriteUnavailable() => new(WritesUnavailableMessage);

    static QZonePostSnapshot? TryReadPost(JsonElement root)
    {
        if (!TryGetSuccessfulData(root, out JsonElement data)
            || !TryGetString(data, "tid", out string? postId)
            || !TryGetInt64(data, "uin", out long targetId)
            || !TryGetString(data, "content", out string? content))
            return null;

        return new QZonePostSnapshot(postId, targetId, content);
    }

    static IReadOnlyList<QZoneCommentSnapshot> TryReadComments(JsonElement root, int count)
    {
        if (!TryGetSuccessfulData(root, out JsonElement data)
            || !data.TryGetProperty("comments", out JsonElement comments)
            || comments.ValueKind != JsonValueKind.Array)
            return [];

        List<QZoneCommentSnapshot> snapshots = [];
        foreach (JsonElement comment in comments.EnumerateArray())
        {
            if (!TryGetString(comment, "id", out string? commentId)
                || !TryGetInt64(comment, "uin", out long userId)
                || !TryGetString(comment, "content", out string? content))
                continue;

            snapshots.Add(new QZoneCommentSnapshot(commentId, userId, content));
        }

        return snapshots.Take(Math.Max(count, 0)).ToArray();
    }

    static bool TryGetSuccessfulData(JsonElement root, out JsonElement data)
    {
        data = default;
        return root.ValueKind == JsonValueKind.Object
            && root.TryGetProperty("code", out JsonElement code)
            && code.TryGetInt32(out int status)
            && status == 0
            && root.TryGetProperty("data", out data)
            && data.ValueKind == JsonValueKind.Object;
    }

    static bool TryGetString(
        JsonElement element,
        string propertyName,
        [NotNullWhen(true)] out string? value)
    {
        value = null;
        return element.TryGetProperty(propertyName, out JsonElement property)
            && property.ValueKind == JsonValueKind.String
            && (value = property.GetString()) is not null;
    }

    static bool TryGetInt64(JsonElement element, string propertyName, out long value)
    {
        value = default;
        return element.TryGetProperty(propertyName, out JsonElement property)
            && property.TryGetInt64(out value);
    }
}
