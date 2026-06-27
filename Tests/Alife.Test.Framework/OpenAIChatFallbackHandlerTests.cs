using System.Net;
using System.Net.Http.Headers;
using System.Text;
using Alife.Framework;
using Newtonsoft.Json.Linq;

namespace Alife.Test.Framework;

public class OpenAIChatFallbackHandlerTests
{
    [Test]
    public async Task SendAsync_RetriesWithFallbackEndpointModelAndKey_WhenPrimaryReturnsRetryableFailure()
    {
        RecordingHandler inner = new(
            _ => new HttpResponseMessage(HttpStatusCode.ServiceUnavailable) { Content = new StringContent("primary unavailable") },
            _ => new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent("{\"ok\":true}") });

        using HttpClient client = new(new OpenAIChatFallbackHandler(
            inner,
            new OpenAIChatFallbackOptions(
                "https://fallback.example/v1",
                "deepseek-v4-flash",
                "fallback-key",
                "{\"thinking\":{\"type\":\"enabled\"}}",
                "")));

        using HttpRequestMessage request = CreateChatRequest("https://primary.example/v1/chat/completions", "gpt-5.5", "{\"effort\":\"max\"}");

        HttpResponseMessage response = await client.SendAsync(request);

        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK));
        Assert.That(inner.Requests, Has.Count.EqualTo(2));
        Assert.That(inner.Requests[0].RequestUri!.ToString(), Is.EqualTo("https://primary.example/v1/chat/completions"));
        Assert.That(inner.Requests[1].RequestUri!.ToString(), Is.EqualTo("https://fallback.example/v1/chat/completions"));
        Assert.That(inner.Requests[1].Headers.Authorization?.Parameter, Is.EqualTo("fallback-key"));

        JObject body = JObject.Parse(await inner.Requests[1].Content!.ReadAsStringAsync());
        Assert.That(body.Value<string>("model"), Is.EqualTo("deepseek-v4-flash"));
        Assert.That(body["effort"], Is.Null);
        Assert.That(body["thinking"]?["type"]?.Value<string>(), Is.EqualTo("enabled"));
    }

    [Test]
    public async Task SendAsync_DoesNotRetry_WhenPrimarySucceeds()
    {
        RecordingHandler inner = new(_ => new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent("{\"ok\":true}") });
        using HttpClient client = new(new OpenAIChatFallbackHandler(
            inner,
            new OpenAIChatFallbackOptions("https://fallback.example/v1", "deepseek-v4-flash", "fallback-key", "{}", "")));

        using HttpRequestMessage request = CreateChatRequest("https://primary.example/v1/chat/completions", "gpt-5.5", "{\"effort\":\"max\"}");

        HttpResponseMessage response = await client.SendAsync(request);

        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK));
        Assert.That(inner.Requests, Has.Count.EqualTo(1));
    }

    [Test]
    public async Task SendAsync_DoesNotRetry_WhenPrimaryReturnsNonRetryableFailure()
    {
        RecordingHandler inner = new(_ => new HttpResponseMessage(HttpStatusCode.BadRequest) { Content = new StringContent("bad request") });
        using HttpClient client = new(new OpenAIChatFallbackHandler(
            inner,
            new OpenAIChatFallbackOptions("https://fallback.example/v1", "deepseek-v4-flash", "fallback-key", "{}", "")));

        using HttpRequestMessage request = CreateChatRequest("https://primary.example/v1/chat/completions", "gpt-5.5", "{\"effort\":\"max\"}");

        HttpResponseMessage response = await client.SendAsync(request);

        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.BadRequest));
        Assert.That(inner.Requests, Has.Count.EqualTo(1));
    }

    static HttpRequestMessage CreateChatRequest(string url, string model, string extraBody)
    {
        JObject body = JObject.Parse(extraBody);
        body["model"] = model;
        body["messages"] = new JArray(new JObject
        {
            ["role"] = "user",
            ["content"] = "hello"
        });

        HttpRequestMessage request = new(HttpMethod.Post, url)
        {
            Content = new StringContent(body.ToString(Newtonsoft.Json.Formatting.None), Encoding.UTF8, "application/json")
        };
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", "primary-key");
        request.Headers.Add("x-primary", "kept");
        return request;
    }

    sealed class RecordingHandler(params Func<HttpRequestMessage, HttpResponseMessage>[] responders) : HttpMessageHandler
    {
        int index;

        public List<HttpRequestMessage> Requests { get; } = [];

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Requests.Add(await CloneRequest(request, cancellationToken));
            Func<HttpRequestMessage, HttpResponseMessage> responder = responders[Math.Min(index, responders.Length - 1)];
            index++;
            return responder(request);
        }

        static async Task<HttpRequestMessage> CloneRequest(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            HttpRequestMessage clone = new(request.Method, request.RequestUri);
            foreach (KeyValuePair<string, IEnumerable<string>> header in request.Headers)
                clone.Headers.TryAddWithoutValidation(header.Key, header.Value);

            if (request.Content != null)
            {
                string body = await request.Content.ReadAsStringAsync(cancellationToken);
                clone.Content = new StringContent(body, Encoding.UTF8, request.Content.Headers.ContentType?.MediaType ?? "application/json");
            }

            return clone;
        }
    }
}
