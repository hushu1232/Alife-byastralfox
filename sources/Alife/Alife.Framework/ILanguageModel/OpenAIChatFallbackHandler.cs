using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace Alife.Framework;

public sealed record OpenAIChatFallbackOptions(
    string Endpoint,
    string ModelId,
    string ApiKey,
    string ExtraBody,
    string ExtraHeaders)
{
    public bool IsConfigured =>
        !string.IsNullOrWhiteSpace(Endpoint) &&
        !string.IsNullOrWhiteSpace(ModelId) &&
        !string.IsNullOrWhiteSpace(ApiKey);
}

public class OpenAIChatFallbackHandler(HttpMessageHandler innerHandler, OpenAIChatFallbackOptions options) : DelegatingHandler(innerHandler)
{
    static readonly string[] ProviderSpecificBodyKeys = [
        "effort",
        "reasoning_effort",
        "thinking",
        "chat_template_kwargs"
    ];

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        if (!options.IsConfigured)
            return await base.SendAsync(request, cancellationToken);

        RequestSnapshot snapshot = await RequestSnapshot.Create(request, cancellationToken);

        try
        {
            HttpResponseMessage response = await base.SendAsync(request, cancellationToken);
            if (!ShouldFallback(response.StatusCode))
                return response;

            response.Dispose();
        }
        catch (Exception ex) when (IsRetryableException(ex, cancellationToken))
        {
        }

        using HttpRequestMessage fallbackRequest = snapshot.CreateFallbackRequest(options);
        return await base.SendAsync(fallbackRequest, cancellationToken);
    }

    static bool ShouldFallback(HttpStatusCode statusCode)
    {
        int code = (int)statusCode;
        return statusCode == HttpStatusCode.RequestTimeout ||
               statusCode == HttpStatusCode.TooManyRequests ||
               code >= 500;
    }

    static bool IsRetryableException(Exception exception, CancellationToken cancellationToken)
    {
        if (cancellationToken.IsCancellationRequested)
            return false;

        return exception is HttpRequestException or IOException or TaskCanceledException;
    }

    sealed class RequestSnapshot
    {
        RequestSnapshot(
            HttpMethod method,
            Uri? requestUri,
            Version version,
            HttpVersionPolicy versionPolicy,
            IReadOnlyList<KeyValuePair<string, IEnumerable<string>>> headers,
            IReadOnlyList<KeyValuePair<string, IEnumerable<string>>> contentHeaders,
            string? content)
        {
            Method = method;
            RequestUri = requestUri;
            Version = version;
            VersionPolicy = versionPolicy;
            Headers = headers;
            ContentHeaders = contentHeaders;
            Content = content;
        }

        HttpMethod Method { get; }
        Uri? RequestUri { get; }
        Version Version { get; }
        HttpVersionPolicy VersionPolicy { get; }
        IReadOnlyList<KeyValuePair<string, IEnumerable<string>>> Headers { get; }
        IReadOnlyList<KeyValuePair<string, IEnumerable<string>>> ContentHeaders { get; }
        string? Content { get; }

        public static async Task<RequestSnapshot> Create(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            string? content = request.Content == null
                ? null
                : await request.Content.ReadAsStringAsync(cancellationToken);

            return new RequestSnapshot(
                request.Method,
                request.RequestUri,
                request.Version,
                request.VersionPolicy,
                request.Headers.ToArray(),
                request.Content?.Headers.ToArray() ?? [],
                content);
        }

        public HttpRequestMessage CreateFallbackRequest(OpenAIChatFallbackOptions options)
        {
            HttpRequestMessage request = new(Method, BuildFallbackUri(options.Endpoint))
            {
                Version = Version,
                VersionPolicy = VersionPolicy
            };

            foreach (KeyValuePair<string, IEnumerable<string>> header in Headers)
            {
                if (string.Equals(header.Key, "Authorization", StringComparison.OrdinalIgnoreCase))
                    continue;

                request.Headers.TryAddWithoutValidation(header.Key, header.Value);
            }

            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", options.ApiKey);
            AddExtraHeaders(request, options.ExtraHeaders);

            if (Content != null)
            {
                string body = RewriteBody(Content, options.ModelId, options.ExtraBody);
                request.Content = new StringContent(body, Encoding.UTF8, "application/json");
                foreach (KeyValuePair<string, IEnumerable<string>> header in ContentHeaders)
                {
                    if (string.Equals(header.Key, "Content-Length", StringComparison.OrdinalIgnoreCase) ||
                        string.Equals(header.Key, "Content-Type", StringComparison.OrdinalIgnoreCase))
                        continue;

                    request.Content.Headers.TryAddWithoutValidation(header.Key, header.Value);
                }
            }

            return request;
        }

        Uri BuildFallbackUri(string endpoint)
        {
            string trimmedEndpoint = endpoint.TrimEnd('/');
            string fallbackUrl = trimmedEndpoint.EndsWith("/chat/completions", StringComparison.OrdinalIgnoreCase)
                ? trimmedEndpoint
                : $"{trimmedEndpoint}/chat/completions";

            return new Uri(fallbackUrl);
        }

        static void AddExtraHeaders(HttpRequestMessage request, string extraHeaders)
        {
            if (string.IsNullOrWhiteSpace(extraHeaders))
                return;

            Dictionary<string, string>? headers = JsonConvert.DeserializeObject<Dictionary<string, string>>(extraHeaders);
            if (headers == null)
                return;

            foreach (KeyValuePair<string, string> header in headers)
                request.Headers.TryAddWithoutValidation(header.Key, header.Value);
        }

        static string RewriteBody(string content, string modelId, string extraBody)
        {
            JObject body = JObject.Parse(content);
            body["model"] = modelId;

            foreach (string key in ProviderSpecificBodyKeys)
                body.Remove(key);

            if (!string.IsNullOrWhiteSpace(extraBody))
            {
                JObject extra = JObject.Parse(extraBody);
                foreach (JProperty property in extra.Properties())
                    body[property.Name] = property.Value;
            }

            return body.ToString(Formatting.None);
        }
    }
}
