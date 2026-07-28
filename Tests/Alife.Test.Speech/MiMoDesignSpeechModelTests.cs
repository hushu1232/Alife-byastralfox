using System.Net;
using System.Text;
using System.Text.Json;
using Alife.Function.Speech;

namespace Alife.Test.Speech;

public sealed class MiMoDesignSpeechModelTests
{
    [Test]
    public async Task RequestUsesOnlyVoiceDescriptionAndApprovedReply()
    {
        var handler = new RecordingHandler();
        var model = new MiMoDesignSpeechModel(httpClient: new HttpClient(handler))
        {
            Configuration = new MiMoDesignSpeechModelConfig
            {
                ApiKey = "test-key",
                DefaultVoiceDesignDescription = "fixed role voice"
            }
        };

        string? file = await model.GenerateSpeechFileAsync("approved reply");

        using JsonDocument payload = JsonDocument.Parse(handler.Body);
        JsonElement messages = payload.RootElement.GetProperty("messages");
        Assert.Multiple(() =>
        {
            Assert.That(file, Is.Not.Null);
            Assert.That(handler.RequestUri, Is.EqualTo("https://api.xiaomimimo.com/v1/chat/completions"));
            Assert.That(handler.ApiKey, Is.EqualTo("test-key"));
            Assert.That(payload.RootElement.GetProperty("model").GetString(), Is.EqualTo("mimo-v2.5-tts-voicedesign"));
            Assert.That(payload.RootElement.GetProperty("audio").GetProperty("format").GetString(), Is.EqualTo("wav"));
            Assert.That(messages[0].GetProperty("role").GetString(), Is.EqualTo("user"));
            Assert.That(messages[0].GetProperty("content").GetString(), Is.EqualTo("fixed role voice"));
            Assert.That(messages[1].GetProperty("role").GetString(), Is.EqualTo("assistant"));
            Assert.That(messages[1].GetProperty("content").GetString(), Is.EqualTo("approved reply"));
            Assert.That(handler.Body, Does.Not.Contain("secret"));
            Assert.That(handler.Body, Does.Not.Contain("optimize_text_preview"));
        });
        File.Delete(file!);
    }

    sealed class RecordingHandler : HttpMessageHandler
    {
        public string Body { get; private set; } = "";
        public string RequestUri { get; private set; } = "";
        public string? ApiKey { get; private set; }

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            RequestUri = request.RequestUri?.ToString() ?? "";
            Body = request.Content is null ? "" : await request.Content.ReadAsStringAsync(cancellationToken);
            ApiKey = request.Headers.TryGetValues("api-key", out IEnumerable<string>? values) ? values.Single() : null;
            byte[] wav = Encoding.ASCII.GetBytes("RIFF\0\0\0\0WAVEfmt ");
            string json = JsonSerializer.Serialize(new
            {
                choices = new[]
                {
                    new { message = new { audio = new { data = Convert.ToBase64String(wav) } } }
                }
            });
            return new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(json, Encoding.UTF8, "application/json") };
        }
    }
}
