using System;
using System.ComponentModel;
using System.Net.Http;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using Alife.Framework;
using Alife.Function.FunctionCaller;
using Alife.Function.Interpreter;

namespace Alife.Function.Agent;

public sealed record AgentInternetFetchResult(bool Success, string Reason, string Content);

[Module("Agent Internet Access", "Controlled public web lookup for agents. Disabled by default.",
    defaultCategory: "astralfox-alife/Agent",
    LaunchOrder = -58)]
public class AgentInternetService(
    AgentInternetConfig? config = null,
    AgentAuditLogService? auditLog = null,
    HttpClient? httpClient = null)
    : InteractiveModule<AgentInternetService>, IConfigurable<AgentInternetConfig>, IModuleHealthReporter
{
    readonly HttpClient client = httpClient ?? new HttpClient();
    readonly AgentAuditLogService? audit = auditLog;

    public AgentInternetConfig? Configuration { get; set; } = config ?? AgentInternetConfig.CreateDefault();

    [XmlFunction(FunctionMode.OneShot, name: "internet_fetch_public_page", riskLevel: XmlFunctionRiskLevel.High, budgetCost: 4)]
    [Description("Fetch a public HTTP/HTTPS page and return a short untrusted text extract. Disabled unless AgentInternetConfig.EnableInternetAccess is true.")]
    public async Task FetchPublicPage(string url)
    {
        AgentInternetFetchResult result = await FetchPublicPageAsync(url);
        Poke(result.Content);
    }

    public virtual async Task<AgentInternetFetchResult> FetchPublicPageAsync(
        string url,
        CancellationToken cancellationToken = default)
    {
        AgentInternetConfig config = Configuration ?? AgentInternetConfig.CreateDefault();
        if (config.EnableInternetAccess == false)
            return Denied("internet_access_disabled", url);

        AgentInternetUrlPolicyDecision policy = AgentInternetUrlPolicy.Evaluate(url, config);
        if (policy.Allowed == false || policy.Uri == null)
            return Denied(policy.Reason, url);

        using CancellationTokenSource timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(TimeSpan.FromMilliseconds(Math.Max(1000, config.TimeoutMilliseconds)));

        try
        {
            using HttpRequestMessage request = new(HttpMethod.Get, policy.Uri);
            request.Headers.UserAgent.ParseAdd(config.UserAgent);

            using HttpResponseMessage response = await client.SendAsync(
                request,
                HttpCompletionOption.ResponseHeadersRead,
                timeout.Token);

            if (response.IsSuccessStatusCode == false)
                return Denied($"http_status_{(int)response.StatusCode}", policy.Uri.ToString());

            byte[] bytes = await response.Content.ReadAsByteArrayAsync(timeout.Token);
            if (bytes.Length > Math.Max(1, config.MaxResponseBytes))
                return Denied("response_too_large", policy.Uri.ToString());

            string html = Encoding.UTF8.GetString(bytes);
            string text = ExtractReadableText(html, config.MaxExtractedChars);
            string wrapped = ExternalContextFormatter.WrapUntrusted("internet-page", text);

            audit?.Record(
                "agent.internet.fetch",
                "agent",
                policy.Uri.Host,
                AgentAuditRiskLevel.Medium,
                succeeded: true);

            return new AgentInternetFetchResult(true, "ok", wrapped);
        }
        catch (Exception ex) when (ex is not OperationCanceledException || cancellationToken.IsCancellationRequested == false)
        {
            audit?.Record(
                "agent.internet.fetch",
                "agent",
                url,
                AgentAuditRiskLevel.Medium,
                succeeded: false,
                error: ex.Message);

            return new AgentInternetFetchResult(false, "fetch_failed", $"internet_fetch_failed: {ex.Message}");
        }
    }

    public ModuleHealth GetHealth()
    {
        bool enabled = (Configuration ?? AgentInternetConfig.CreateDefault()).EnableInternetAccess;
        return new ModuleHealth(
            "AgentInternet",
            enabled ? ModuleHealthStatus.Healthy : ModuleHealthStatus.Degraded,
            enabled ? "Agent internet access is enabled." : "Agent internet access is disabled.");
    }

    AgentInternetFetchResult Denied(string reason, string detail)
    {
        audit?.Record(
            "agent.internet.fetch",
            "agent",
            detail,
            AgentAuditRiskLevel.Medium,
            succeeded: false,
            error: reason);

        return new AgentInternetFetchResult(false, reason, $"internet_fetch_denied: {reason}; target={detail}");
    }

    static string ExtractReadableText(string html, int maxChars)
    {
        string withoutScripts = Regex.Replace(
            html,
            "<(script|style)[^>]*>.*?</\\1>",
            " ",
            RegexOptions.IgnoreCase | RegexOptions.Singleline);
        string withoutTags = Regex.Replace(withoutScripts, "<[^>]+>", " ");
        string decoded = System.Net.WebUtility.HtmlDecode(withoutTags);
        string compact = Regex.Replace(decoded, "\\s+", " ").Trim();
        int limit = Math.Clamp(maxChars, 1, 50000);
        return compact.Length <= limit ? compact : compact[..limit];
    }
}
