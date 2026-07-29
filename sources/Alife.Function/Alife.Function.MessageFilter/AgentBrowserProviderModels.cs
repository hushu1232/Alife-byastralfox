using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Alife.Framework;

namespace Alife.Function.Agent;

public sealed record AgentBrowserSnapshotRequest(
    string Url,
    int Page = 1,
    int MaxTextChars = 8000,
    int MaxElements = 50);

public sealed record AgentBrowserElement(
    string Id,
    string Type,
    string Text,
    string Href);

public sealed record AgentBrowserSnapshot(
    bool Success,
    string Reason,
    string Url,
    string Title,
    string Text,
    IReadOnlyList<AgentBrowserElement> Elements,
    AgentBrowserSnapshotDiagnostics? Diagnostics = null);

public sealed record AgentBrowserSnapshotDiagnostics(
    bool LoginWallDetected,
    bool AntiBotDetected,
    bool TextTruncated,
    int OriginalTextChars,
    int LinkCount);

public interface IAgentBrowserProvider
{
    Task<AgentBrowserSnapshot> CaptureSnapshotAsync(
        AgentBrowserSnapshotRequest request,
        CancellationToken cancellationToken = default);
}

public static class AgentBrowserSnapshotFormatter
{
    public static string Format(
        AgentBrowserSnapshot snapshot,
        int maxTextChars = 8000,
        int maxElements = 50)
    {
        ArgumentNullException.ThrowIfNull(snapshot);

        if (snapshot.Success == false)
            return $"browser_snapshot_failed: {snapshot.Reason}";

        int textLimit = Math.Clamp(maxTextChars, 0, 50000);
        int elementLimit = Math.Clamp(maxElements, 0, 500);
        string text = snapshot.Text ?? "";
        int originalTextChars = snapshot.Diagnostics?.OriginalTextChars ?? text.Length;
        if (text.Length > textLimit)
            text = text[..textLimit];
        bool textTruncated = snapshot.Diagnostics?.TextTruncated == true
                             || originalTextChars > text.Length;

        StringBuilder builder = new();
        builder.AppendLine($"url={snapshot.Url}");
        builder.AppendLine($"title={snapshot.Title}");
        builder.AppendLine($"snapshot_risk={FormatRisk(snapshot.Diagnostics)}");
        if (textTruncated)
            builder.AppendLine($"text_truncated=true original_chars={originalTextChars} emitted_chars={text.Length}");
        int totalLinks = snapshot.Diagnostics?.LinkCount ?? (snapshot.Elements ?? []).Count(element => string.Equals(element.Type, "link", StringComparison.OrdinalIgnoreCase));
        if (totalLinks > 0)
            builder.AppendLine($"links_total={totalLinks} emitted={Math.Min(totalLinks, elementLimit)}");
        if (text.Length > 0)
            builder.AppendLine(text);

        foreach (AgentBrowserElement element in (snapshot.Elements ?? []).Take(elementLimit))
        {
            builder.AppendLine(FormatElement(element));
        }

        return ExternalContextFormatter.WrapUntrusted(
            "browser-snapshot",
            builder.ToString().TrimEnd());
    }

    public static string FormatForUser(AgentBrowserSnapshot snapshot, int maxTextChars = 600)
    {
        ArgumentNullException.ThrowIfNull(snapshot);

        if (snapshot.Success == false)
            return "这个网页暂时没读出来，请稍后再试。";

        string title = Compact(snapshot.Title);
        if (title.Length > 160)
            title = $"{title[..160].TrimEnd()}…";
        string text = Compact(snapshot.Text);
        int textLimit = Math.Clamp(maxTextChars, 0, 2000);
        if (text.Length > textLimit)
            text = $"{text[..textLimit].TrimEnd()}…";

        StringBuilder builder = new();
        if (title.Length > 0)
            builder.AppendLine($"网页标题：{title}");
        if (text.Length > 0)
            builder.AppendLine($"主要内容：{text}");
        string sourceUrl = FormatSourceUrl(snapshot.Url);
        if (sourceUrl.Length > 0)
            builder.AppendLine($"来源：{sourceUrl}");

        return builder.Length == 0
            ? "网页已经打开，但没有读取到可用正文。"
            : builder.ToString().TrimEnd();
    }

    static string FormatRisk(AgentBrowserSnapshotDiagnostics? diagnostics)
    {
        if (diagnostics == null)
            return "unknown";

        List<string> risks = [];
        if (diagnostics.LoginWallDetected)
            risks.Add("login_wall");
        if (diagnostics.AntiBotDetected)
            risks.Add("anti_bot");
        return risks.Count == 0 ? "none" : string.Join(",", risks);
    }

    static string FormatElement(AgentBrowserElement element)
    {
        string line = $"element {element.Id} type={element.Type} text={element.Text}";
        if (string.IsNullOrWhiteSpace(element.Href) == false)
            line += $" href={element.Href}";
        return line;
    }

    public static string FormatSourceUrl(string? value)
    {
        if (Uri.TryCreate(value, UriKind.Absolute, out Uri? uri) == false ||
            (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps))
        {
            return "";
        }

        string source = uri.GetComponents(
            UriComponents.SchemeAndServer | UriComponents.Path,
            UriFormat.UriEscaped);
        return source.Length <= 300
            ? source
            : uri.GetComponents(UriComponents.SchemeAndServer, UriFormat.UriEscaped);
    }

    static string Compact(string? value) =>
        string.Join(' ', (value ?? "").Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));
}
