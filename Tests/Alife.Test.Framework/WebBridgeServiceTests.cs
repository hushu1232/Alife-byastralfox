using System.Net;
using System.Net.Http;
using System.Text.Json;
using Alife.Framework;
using Alife.Function.WebBridge;

namespace Alife.Test.Framework;

public class WebBridgeServiceTests
{
    [Test]
    public void CharacterSyncMapsAvatarConfigToAlifeCharacter()
    {
        WebAvatarConfig avatar = new()
        {
            Id = "avatar-mao",
            Name = "星狐",
            Description = "来自 FOXD 的角色",
            Prompt = "你是一只活泼的狐狸伙伴。",
            Modules = ["Alife.Function.Emotion.PADEmotionEngine", "Alife.Function.DeskPet.DeskPetService"]
        };

        Character character = CharacterSync.ToCharacter(avatar);
        WebAvatarConfig roundTrip = CharacterSync.ToAvatarConfig(character);

        Assert.That(character.Name, Is.EqualTo("星狐"));
        Assert.That(character.Description, Is.EqualTo("来自 FOXD 的角色"));
        Assert.That(character.Prompt, Is.EqualTo("你是一只活泼的狐狸伙伴。"));
        Assert.That(character.Modules, Does.Contain("Alife.Function.Emotion.PADEmotionEngine"));
        Assert.That(roundTrip.Id, Is.EqualTo(character.StorageKey));
        Assert.That(roundTrip.Modules, Does.Contain("Alife.Function.DeskPet.DeskPetService"));
    }

    [Test]
    public async Task WebApiClientPullsConfigAndPushesStateWithBearerToken()
    {
        RecordingHandler handler = new();
        HttpClient httpClient = new(handler)
        {
            BaseAddress = new Uri("https://foxd.example/")
        };
        WebApiClient client = new(httpClient, new WebBridgeServiceConfig
        {
            ApiToken = "secret-token"
        });
        WebAvatarConfig state = new()
        {
            Id = "Character\\Mao",
            Name = "Mao",
            Description = "desk pet",
            Prompt = "hello",
            Modules = ["module.a"]
        };

        WebAvatarConfig pulled = await client.PullConfig(CancellationToken.None);
        await client.PushState(state, CancellationToken.None);

        Assert.That(pulled.Name, Is.EqualTo("远端角色"));
        Assert.That(handler.Requests, Has.Count.EqualTo(2));
        Assert.That(handler.Requests[0].RequestUri?.PathAndQuery, Is.EqualTo("/api/pet/config"));
        Assert.That(handler.Requests[1].RequestUri?.PathAndQuery, Is.EqualTo("/api/pet/sync"));
        Assert.That(handler.Requests.All(request => request.Headers.Authorization?.Scheme == "Bearer"));
        Assert.That(handler.Requests.All(request => request.Headers.Authorization?.Parameter == "secret-token"));
        Assert.That(handler.PostedJson, Does.Contain("\"name\":\"Mao\""));
    }

    sealed class RecordingHandler : HttpMessageHandler
    {
        public List<HttpRequestMessage> Requests { get; } = new();
        public string? PostedJson { get; private set; }

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Requests.Add(request);
            if (request.Content != null)
                PostedJson = await request.Content.ReadAsStringAsync(cancellationToken);

            string responseJson = JsonSerializer.Serialize(new WebAvatarConfig
            {
                Id = "avatar-remote",
                Name = "远端角色",
                Description = "from web",
                Prompt = "remote prompt",
                Modules = ["module.remote"]
            });

            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(responseJson)
            };
        }
    }
}
