using System;
using System.Collections.Generic;
using System.Net;
using System.Text.RegularExpressions;

namespace Alife.Function.QChat;

public static class QChatImageSegmentParser
{
    static readonly Regex ImageSegmentPattern = new(
        @"\[CQ:image(?<body>[^\]]*)\]",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    public static IReadOnlyList<QChatImageCandidate> Extract(string? rawMessage)
    {
        if (string.IsNullOrWhiteSpace(rawMessage))
            return [];

        List<QChatImageCandidate> images = [];
        foreach (Match match in ImageSegmentPattern.Matches(rawMessage))
        {
            string segment = match.Value;
            string body = match.Groups["body"].Value;
            images.Add(new QChatImageCandidate(
                Segment: segment,
                Url: GetCqValue(body, "url"),
                File: GetCqValue(body, "file"),
                Summary: GetCqValue(body, "summary")));
        }

        return images;
    }

    public static IReadOnlyList<string> ExtractTrustedHttpsUrls(string? rawMessage)
    {
        List<string> urls = [];
        foreach (QChatImageCandidate image in Extract(rawMessage))
        {
            if (Uri.TryCreate(image.Url, UriKind.Absolute, out Uri? uri) &&
                uri.Scheme == Uri.UriSchemeHttps &&
                string.IsNullOrWhiteSpace(uri.UserInfo))
            {
                urls.Add(uri.AbsoluteUri);
            }
        }

        return urls;
    }

    public static string AppendFirstImageUrlForLocalToolRouting(
        string modelInput,
        IReadOnlyList<string>? trustedImageUrls)
    {
        ArgumentNullException.ThrowIfNull(modelInput);
        return trustedImageUrls is { Count: > 0 }
            ? string.Concat(modelInput, Environment.NewLine, trustedImageUrls[0])
            : modelInput;
    }

    static string? GetCqValue(string body, string key)
    {
        Match match = Regex.Match(
            body,
            $@"(?:^|,){Regex.Escape(key)}=(?<value>[^,\]]*)",
            RegexOptions.CultureInvariant);
        if (match.Success == false)
            return null;

        string value = WebUtility.HtmlDecode(match.Groups["value"].Value.Trim());
        return string.IsNullOrWhiteSpace(value) ? null : value;
    }
}
