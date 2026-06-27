using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Alife.Function.Agent;

namespace Alife.Function.Browser;

public sealed class AgentBrowserRuntimeProvider(IBrowserRuntime runtime) : IAgentBrowserProvider
{
    readonly IBrowserRuntime runtime = runtime;

    public async Task<AgentBrowserSnapshot> CaptureSnapshotAsync(
        AgentBrowserSnapshotRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            await runtime.NavigateAsync(request.Url);
            cancellationToken.ThrowIfCancellationRequested();

            string scriptResult = await runtime.ExecuteScriptAsync(SnapshotExtractionScript);
            ExtractedSnapshot extracted = DecodeExtractedSnapshot(scriptResult);
            string title = extracted.Title;
            string text = extracted.BodyText;
            if (string.IsNullOrWhiteSpace(text))
                text = await runtime.ObserveAsync(Math.Max(request.Page, 1));
            int originalTextChars = text.Length;
            bool truncated = false;
            if (request.MaxTextChars >= 0 && text.Length > request.MaxTextChars)
            {
                text = text[..request.MaxTextChars];
                truncated = true;
            }
            AgentBrowserSnapshotDiagnostics diagnostics = AnalyzeSnapshot(
                title,
                text,
                originalTextChars,
                truncated,
                extracted.Links.Count);
            if (diagnostics.LoginWallDetected)
                return new AgentBrowserSnapshot(
                    false,
                    "login_required",
                    request.Url,
                    title,
                    text,
                    extracted.Links,
                    diagnostics);
            if (diagnostics.AntiBotDetected)
                return new AgentBrowserSnapshot(
                    false,
                    "anti_bot_challenge",
                    request.Url,
                    title,
                    text,
                    extracted.Links,
                    diagnostics);

            return new AgentBrowserSnapshot(
                true,
                "ok",
                request.Url,
                title,
                text,
                extracted.Links.Take(Math.Max(request.MaxElements, 0)).ToArray(),
                diagnostics);
        }
        catch (Exception ex) when (ex is not OperationCanceledException || cancellationToken.IsCancellationRequested == false)
        {
            return new AgentBrowserSnapshot(
                false,
                "browser_snapshot_failed",
                request.Url,
                "",
                "",
                []);
        }
    }

    const string SnapshotExtractionScript = """
        (() => {
          const clean = value => String(value || '').replace(/\s+/g, ' ').trim();
          const links = Array.from(document.querySelectorAll('a[href]')).slice(0, 80).map((a, index) => ({
            id: `link-${index + 1}`,
            type: 'link',
            text: clean(a.innerText || a.textContent || a.getAttribute('aria-label') || a.href).slice(0, 160),
            href: a.href
          })).filter(link => link.href && link.text);
          return JSON.stringify({
            title: clean(document.title),
            bodyText: clean(document.body ? document.body.innerText : ''),
            links
          });
        })()
        """;

    static ExtractedSnapshot DecodeExtractedSnapshot(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return new ExtractedSnapshot("", "", []);

        try
        {
            string json = DecodeScriptString(value);
            ExtractedSnapshotDto? dto = JsonSerializer.Deserialize<ExtractedSnapshotDto>(
                json,
                new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
            if (dto != null)
            {
                AgentBrowserElement[] links = (dto.Links ?? [])
                    .Where(link => string.IsNullOrWhiteSpace(link.Href) == false)
                    .Select((link, index) => new AgentBrowserElement(
                        string.IsNullOrWhiteSpace(link.Id) ? $"link-{index + 1}" : CleanOneLine(link.Id),
                        string.IsNullOrWhiteSpace(link.Type) ? "link" : CleanOneLine(link.Type),
                        CleanOneLine(link.Text),
                        CleanOneLine(link.Href)))
                    .Where(link => string.IsNullOrWhiteSpace(link.Text) == false)
                    .ToArray();
                return new ExtractedSnapshot(
                    CleanOneLine(dto.Title),
                    CleanBodyText(dto.BodyText),
                    links);
            }
        }
        catch (JsonException)
        {
        }

        return new ExtractedSnapshot(DecodeScriptString(value), "", []);
    }

    static AgentBrowserSnapshotDiagnostics AnalyzeSnapshot(
        string title,
        string text,
        int originalTextChars,
        bool textTruncated,
        int linkCount)
    {
        string combined = $"{title}\n{text}";
        bool loginWall = ContainsAny(
            combined,
            "sign in",
            "log in",
            "login required",
            "please login",
            "please sign in",
            "account required",
            "authentication required");
        bool antiBot = ContainsAny(
            combined,
            "captcha",
            "cloudflare",
            "checking your browser",
            "just a moment",
            "verify you are human",
            "human verification",
            "access denied");
        return new AgentBrowserSnapshotDiagnostics(
            loginWall,
            antiBot,
            textTruncated,
            originalTextChars,
            linkCount);
    }

    static string DecodeScriptString(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return "";

        try
        {
            string? decoded = JsonSerializer.Deserialize<string>(value);
            return decoded ?? "";
        }
        catch (JsonException)
        {
            return value.Trim();
        }
    }

    static bool ContainsAny(string value, params string[] terms)
    {
        return terms.Any(term => value.Contains(term, StringComparison.OrdinalIgnoreCase));
    }

    static string CleanOneLine(string? value)
    {
        string text = Regex.Replace(value ?? "", @"\s+", " ").Trim();
        return text.Length <= 300 ? text : text[..300].TrimEnd();
    }

    static string CleanBodyText(string? value)
    {
        string text = Regex.Replace(value ?? "", @"\s+", " ").Trim();
        return text;
    }

    sealed record ExtractedSnapshot(
        string Title,
        string BodyText,
        IReadOnlyList<AgentBrowserElement> Links);

    sealed class ExtractedSnapshotDto
    {
        public string? Title { get; set; }
        public string? BodyText { get; set; }
        public List<ExtractedLinkDto>? Links { get; set; }
    }

    sealed class ExtractedLinkDto
    {
        public string? Id { get; set; }
        public string? Type { get; set; }
        public string? Text { get; set; }
        public string? Href { get; set; }
    }
}
