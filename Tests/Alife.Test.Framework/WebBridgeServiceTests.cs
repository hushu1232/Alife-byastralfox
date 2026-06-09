using System.Net;
using System.Net.Http;
using System.Text;
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

    [Test]
    public async Task WebBridgeServicePullsRemoteAvatarIntoCharacterStore()
    {
        RecordingHandler handler = new();
        WebApiClient client = new(new HttpClient(handler)
        {
            BaseAddress = new Uri("https://foxd.example/")
        }, new WebBridgeServiceConfig());
        MemoryCharacterBridgeStore characterStore = new();
        WebBridgeService service = new(client, characterStore);

        Character character = await service.PullConfig(CancellationToken.None);

        Assert.That(characterStore.SavedCharacters, Has.Count.EqualTo(1));
        Assert.That(characterStore.SavedCharacters[0], Is.SameAs(character));
        Assert.That(character.Name, Is.EqualTo("远端角色"));
        Assert.That(character.Modules, Does.Contain("module.remote"));
    }

    [Test]
    public async Task WebApiClientRequestsAvatarSwitchAndPullsAssets()
    {
        RecordingHandler handler = new();
        WebApiClient client = new(new HttpClient(handler)
        {
            BaseAddress = new Uri("https://foxd.example/")
        }, new WebBridgeServiceConfig());

        await client.SetAvatar("avatar-mao", CancellationToken.None);
        WebAssetManifest manifest = await client.PullAssets(CancellationToken.None);

        Assert.That(handler.Requests[0].RequestUri?.PathAndQuery, Is.EqualTo("/api/pet/set-avatar"));
        Assert.That(handler.Requests[1].RequestUri?.PathAndQuery, Is.EqualTo("/api/pet/assets"));
        Assert.That(handler.PostedJson, Does.Contain("\"avatarId\":\"avatar-mao\""));
        Assert.That(manifest.Files, Has.Count.EqualTo(1));
        Assert.That(manifest.Files[0].RelativePath, Is.EqualTo("model/Mao/texture.png"));
    }

    [Test]
    public async Task WebAssetSyncWritesFilesInsideTargetDirectory()
    {
        string targetDirectory = Path.Combine(Path.GetTempPath(), "alife-webbridge-assets", Guid.NewGuid().ToString("N"));
        WebAssetSync assetSync = new(targetDirectory);
        WebAssetManifest manifest = new()
        {
            Files =
            [
                new WebAssetFile
                {
                    RelativePath = "model/Mao/texture.txt",
                    ContentBase64 = Convert.ToBase64String(Encoding.UTF8.GetBytes("asset-data"))
                }
            ]
        };

        await assetSync.SyncAssets(manifest, CancellationToken.None);

        string filePath = Path.Combine(targetDirectory, "model", "Mao", "texture.txt");
        Assert.That(File.ReadAllText(filePath), Is.EqualTo("asset-data"));
        Assert.ThrowsAsync<InvalidOperationException>(() => assetSync.SyncAssets(new WebAssetManifest
        {
            Files =
            [
                new WebAssetFile
                {
                    RelativePath = "../escape.txt",
                    ContentBase64 = Convert.ToBase64String(Encoding.UTF8.GetBytes("bad"))
                }
            ]
        }, CancellationToken.None));
    }

    [Test]
    public async Task WebBridgeSyncOncePullsAvatarAssetsAndPushesState()
    {
        RecordingHandler handler = new();
        string targetDirectory = Path.Combine(Path.GetTempPath(), "alife-webbridge-sync-once", Guid.NewGuid().ToString("N"));
        WebBridgeService service = CreateService(handler, new MemoryCharacterBridgeStore(), new WebAssetSync(targetDirectory));

        WebBridgeSyncResult result = await service.SyncOnce(CancellationToken.None);

        Assert.That(result.Character.Name, Is.EqualTo("远端角色"));
        Assert.That(result.AssetsSynced, Is.True);
        Assert.That(handler.Requests.Select(request => request.RequestUri?.PathAndQuery), Is.EqualTo(new[]
        {
            "/api/pet/config",
            "/api/pet/assets",
            "/api/pet/sync"
        }));
        Assert.That(File.Exists(Path.Combine(targetDirectory, "model", "Mao", "texture.png")), Is.True);
    }

    [Test]
    public async Task WebBridgePollingStartsAndStopsWithoutLeavingRequestsRunning()
    {
        RecordingHandler handler = new();
        WebBridgeService service = CreateService(handler, new MemoryCharacterBridgeStore(), new WebAssetSync(Path.Combine(Path.GetTempPath(), "alife-webbridge-poll", Guid.NewGuid().ToString("N"))));
        service.Configuration = new WebBridgeServiceConfig
        {
            AutoSyncEnabled = true,
            SyncIntervalMilliseconds = 20
        };

        await service.StartAsync(null!, new ChatActivity(new Character { Name = "PollTest" }, null!, null!, new ChatBot(null!, null!), []));
        await WaitUntil(() => handler.Requests.Count >= 3, TimeSpan.FromSeconds(2));
        await service.DestroyAsync();
        int countAfterDestroy = handler.Requests.Count;
        await Task.Delay(80);

        Assert.That(countAfterDestroy, Is.GreaterThanOrEqualTo(3));
        Assert.That(handler.Requests.Count, Is.EqualTo(countAfterDestroy));
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

            object response = request.RequestUri?.AbsolutePath switch
            {
                "/api/pet/assets" => new WebAssetManifest
                {
                    Files =
                    [
                        new WebAssetFile
                        {
                            RelativePath = "model/Mao/texture.png",
                            ContentBase64 = Convert.ToBase64String([1, 2, 3])
                        }
                    ]
                },
                _ => new WebAvatarConfig
                {
                    Id = "avatar-remote",
                    Name = "远端角色",
                    Description = "from web",
                    Prompt = "remote prompt",
                    Modules = ["module.remote"]
                }
            };
            string responseJson = JsonSerializer.Serialize(response);

            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(responseJson)
            };
        }
    }

    sealed class MemoryCharacterBridgeStore : ICharacterBridgeStore
    {
        public List<Character> SavedCharacters { get; } = new();

        public Character UpsertCharacter(WebAvatarConfig avatarConfig)
        {
            Character character = CharacterSync.ToCharacter(avatarConfig);
            SavedCharacters.Add(character);
            return character;
        }
    }

    static WebBridgeService CreateService(RecordingHandler handler, ICharacterBridgeStore characterStore, WebAssetSync assetSync)
    {
        WebApiClient client = new(new HttpClient(handler)
        {
            BaseAddress = new Uri("https://foxd.example/")
        }, new WebBridgeServiceConfig());
        return new WebBridgeService(client, characterStore, assetSync);
    }

    static async Task WaitUntil(Func<bool> condition, TimeSpan timeout)
    {
        DateTime deadline = DateTime.UtcNow + timeout;
        while (DateTime.UtcNow < deadline)
        {
            if (condition())
                return;

            await Task.Delay(10);
        }

        Assert.Fail("等待条件超时");
    }
}
