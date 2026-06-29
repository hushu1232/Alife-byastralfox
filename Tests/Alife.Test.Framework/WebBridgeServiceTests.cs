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
    public void WebBridgePackageManifestCarriesInstallOnlyActivationPolicy()
    {
        WebBridgePackageManifest manifest = new()
        {
            SchemaVersion = 1,
            PackageId = "xiayu-character-bundle",
            PackageType = "characterBundle",
            DisplayName = "夏雨角色包",
            Version = "1.0.0",
            Files =
            [
                new WebBridgePackageFile
                {
                    Kind = "characterCard",
                    Url = "https://foxd.example/downloads/xiayu/card.json",
                    RelativePath = "characters/xiayu/card.json",
                    Sha256 = "sha256-placeholder",
                    Size = 12
                }
            ],
            ConfigDraft = new WebBridgeConfigDraft
            {
                CharacterName = "夏雨",
                CharacterCardPath = "characters/xiayu/card.json",
                Live2DModelPath = "live2d/xiayu/model3.json"
            },
            ActivationPolicy = new WebBridgeActivationPolicy
            {
                AutoApply = false,
                RequiresLocalConfirmation = true
            }
        };

        Assert.That(manifest.PackageType, Is.EqualTo("characterBundle"));
        Assert.That(manifest.ActivationPolicy.AutoApply, Is.False);
        Assert.That(manifest.ActivationPolicy.RequiresLocalConfirmation, Is.True);
    }

    [Test]
    public void WebBridgePackageInstallerRejectsPathTraversal()
    {
        string root = Path.Combine(Path.GetTempPath(), "alife-webbridge-installer", Guid.NewGuid().ToString("N"));
        WebBridgePackageInstaller installer = new(root, _ => Task.FromResult(Array.Empty<byte>()));
        WebBridgePackageManifest manifest = new()
        {
            PackageId = "unsafe-package",
            PackageType = "live2d",
            Version = "1.0.0",
            Files =
            [
                new WebBridgePackageFile
                {
                    Kind = "live2d",
                    Url = "https://foxd.example/unsafe",
                    RelativePath = "../escape.txt",
                    Sha256 = "",
                    Size = 0
                }
            ]
        };

        Assert.ThrowsAsync<InvalidOperationException>(() => installer.Install(manifest, CancellationToken.None));
    }

    [Test]
    public async Task WebBridgePackageInstallerWritesFilesManifestAndConfigDraftWithoutActivating()
    {
        string root = Path.Combine(Path.GetTempPath(), "alife-webbridge-installer", Guid.NewGuid().ToString("N"));
        byte[] content = [1, 2, 3];
        string hash = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(content)).ToLowerInvariant();
        WebBridgePackageInstaller installer = new(root, _ => Task.FromResult(content));
        WebBridgePackageManifest manifest = new()
        {
            PackageId = "xiayu-character-bundle",
            PackageType = "characterBundle",
            Version = "1.0.0",
            Files =
            [
                new WebBridgePackageFile
                {
                    Kind = "live2d",
                    Url = "https://foxd.example/live2d/model3.json",
                    RelativePath = "live2d/xiayu/model3.json",
                    Sha256 = hash,
                    Size = content.Length
                }
            ],
            ConfigDraft = new WebBridgeConfigDraft
            {
                CharacterName = "xiayu",
                CharacterCardPath = "characters/xiayu/card.json",
                Live2DModelPath = "live2d/xiayu/model3.json"
            },
            ActivationPolicy = new WebBridgeActivationPolicy
            {
                AutoApply = false,
                RequiresLocalConfirmation = true
            }
        };

        WebBridgeInstallResult result = await installer.Install(manifest, CancellationToken.None);

        Assert.That(result.Status, Is.EqualTo(WebBridgePackageStatus.PendingActivation));
        Assert.That(result.InstalledFiles, Is.EqualTo(1));
        Assert.That(File.Exists(Path.Combine(result.PackageRootPath, "live2d", "xiayu", "model3.json")), Is.True);
        Assert.That(File.Exists(result.ManifestPath), Is.True);
        Assert.That(File.Exists(result.ConfigDraftPath), Is.True);
        Assert.That(File.ReadAllText(result.ConfigDraftPath), Does.Contain("xiayu"));
    }

    [Test]
    public void WebBridgePackageInstallerRejectsHashMismatch()
    {
        string root = Path.Combine(Path.GetTempPath(), "alife-webbridge-installer", Guid.NewGuid().ToString("N"));
        WebBridgePackageInstaller installer = new(root, _ => Task.FromResult(new byte[] { 1, 2, 3 }));
        WebBridgePackageManifest manifest = new()
        {
            PackageId = "hash-package",
            PackageType = "live2d",
            Version = "1.0.0",
            Files =
            [
                new WebBridgePackageFile
                {
                    Kind = "live2d",
                    Url = "https://foxd.example/model3.json",
                    RelativePath = "live2d/model3.json",
                    Sha256 = "0000000000000000000000000000000000000000000000000000000000000000",
                    Size = 3
                }
            ]
        };

        Assert.ThrowsAsync<InvalidOperationException>(() => installer.Install(manifest, CancellationToken.None));
    }

    [Test]
    public async Task WebBridgePackageInstallerRecordsPendingPackageInCatalog()
    {
        string root = Path.Combine(Path.GetTempPath(), "alife-webbridge-installer", Guid.NewGuid().ToString("N"));
        byte[] content = [7, 8, 9];
        WebBridgePackageInstaller installer = new(root, _ => Task.FromResult(content));
        WebBridgePackageManifest manifest = new()
        {
            PackageId = "catalog-package",
            PackageType = "characterCard",
            Version = "1.2.3",
            Files =
            [
                new WebBridgePackageFile
                {
                    Kind = "characterCard",
                    Url = "https://foxd.example/card.json",
                    RelativePath = "characters/card.json",
                    Size = content.Length
                }
            ]
        };

        await installer.Install(manifest, CancellationToken.None);

        string catalogPath = Path.Combine(root, "catalog.json");
        Assert.That(File.Exists(catalogPath), Is.True);
        string catalog = File.ReadAllText(catalogPath);
        Assert.That(catalog, Does.Contain("catalog-package"));
        Assert.That(catalog, Does.Contain("pendingActivation"));
    }

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
        Assert.That(handler.Requests[0].RequestUri?.PathAndQuery, Is.EqualTo("/api/pet/sync"));
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
    public async Task WebBridgeServiceInstallsPackageAsPendingActivation()
    {
        RecordingHandler handler = new() { UsePackageManifestEnvelope = true };
        string root = Path.Combine(Path.GetTempPath(), "alife-webbridge-service-install", Guid.NewGuid().ToString("N"));
        WebApiClient client = new(new HttpClient(handler)
        {
            BaseAddress = new Uri("https://foxd.example/")
        }, new WebBridgeServiceConfig());
        WebBridgePackageInstaller installer = new(root, _ => Task.FromResult(new byte[] { 1 }));
        WebBridgeService service = new(client, new MemoryCharacterBridgeStore(), assetSync: null, packageInstaller: installer);

        WebBridgeInstallResult result = await service.InstallPackage("xiayu-character-bundle", CancellationToken.None);

        Assert.That(result.PackageId, Is.EqualTo("xiayu-character-bundle"));
        Assert.That(result.Status, Is.EqualTo(WebBridgePackageStatus.PendingActivation));
        Assert.That(handler.Requests.Select(request => request.RequestUri?.PathAndQuery), Is.EqualTo(new[]
        {
            "/api/webbridge/packages/xiayu-character-bundle/manifest",
            "/api/pet/sync/status",
            "/api/pet/sync/status",
            "/api/pet/sync/status"
        }));
    }

    [Test]
    public async Task WebBridgeServiceDownloadsPackageFilesWithBearerToken()
    {
        RecordingHandler handler = new() { UsePackageManifestEnvelope = true, IncludePackageFile = true };
        string root = Path.Combine(Path.GetTempPath(), "alife-webbridge-service-download", Guid.NewGuid().ToString("N"));
        WebApiClient client = new(new HttpClient(handler)
        {
            BaseAddress = new Uri("https://foxd.example/")
        }, new WebBridgeServiceConfig { ApiToken = "secret-token" });
        WebBridgeService service = new(client, new MemoryCharacterBridgeStore())
        {
            Configuration = new WebBridgeServiceConfig
            {
                PackageRootPath = root
            }
        };

        WebBridgeInstallResult result = await service.InstallPackage("xiayu-character-bundle", CancellationToken.None);

        Assert.That(result.InstalledFiles, Is.EqualTo(1));
        Assert.That(File.Exists(Path.Combine(result.PackageRootPath, "characters", "xiayu", "card.json")), Is.True);
        Assert.That(handler.Requests.Select(request => request.RequestUri?.PathAndQuery), Is.EqualTo(new[]
        {
            "/api/webbridge/packages/xiayu-character-bundle/manifest",
            "/api/pet/sync/status",
            "/api/webbridge/packages/xiayu-character-bundle/files/character-card",
            "/api/pet/sync/status",
            "/api/pet/sync/status",
            "/api/pet/sync/status",
            "/api/pet/sync/status"
        }));
        Assert.That(handler.Requests.All(request => request.Headers.Authorization?.Scheme == "Bearer"), Is.True);
        Assert.That(handler.Requests.All(request => request.Headers.Authorization?.Parameter == "secret-token"), Is.True);
    }

    [Test]
    public async Task WebBridgeServiceReportsPackageInstallMilestones()
    {
        RecordingHandler handler = new() { UsePackageManifestEnvelope = true, IncludePackageFile = true };
        string root = Path.Combine(Path.GetTempPath(), "alife-webbridge-service-milestones", Guid.NewGuid().ToString("N"));
        WebApiClient client = new(new HttpClient(handler)
        {
            BaseAddress = new Uri("https://foxd.example/")
        }, new WebBridgeServiceConfig { ApiToken = "secret-token" });
        WebBridgeService service = new(client, new MemoryCharacterBridgeStore())
        {
            Configuration = new WebBridgeServiceConfig
            {
                PackageRootPath = root
            }
        };

        await service.InstallPackage("xiayu-character-bundle", CancellationToken.None);

        Assert.That(handler.Requests.Select(request => request.RequestUri?.PathAndQuery), Is.EqualTo(new[]
        {
            "/api/webbridge/packages/xiayu-character-bundle/manifest",
            "/api/pet/sync/status",
            "/api/webbridge/packages/xiayu-character-bundle/files/character-card",
            "/api/pet/sync/status",
            "/api/pet/sync/status",
            "/api/pet/sync/status",
            "/api/pet/sync/status"
        }));
        Assert.That(handler.PostedJsonBodies.Where(body => body.Contains("\"milestone\"")).Select(GetPostedMilestone), Is.EqualTo(new[]
        {
            "manifestFetched",
            "filesDownloaded",
            "hashValidated",
            "packageStaged",
            "confirmationRequested"
        }));
        Assert.That(handler.Requests.All(request => request.Headers.Authorization?.Scheme == "Bearer"), Is.True);
        Assert.That(handler.Requests.All(request => request.Headers.Authorization?.Parameter == "secret-token"), Is.True);
    }

    [Test]
    public async Task WebApiClientPullsConfigFromWebPetSyncEnvelope()
    {
        RecordingHandler handler = new() { UseWebPetSyncEnvelope = true };
        WebApiClient client = new(new HttpClient(handler)
        {
            BaseAddress = new Uri("https://foxd.example/")
        }, new WebBridgeServiceConfig());

        WebAvatarConfig pulled = await client.PullConfig(CancellationToken.None);

        Assert.That(handler.Requests[0].RequestUri?.PathAndQuery, Is.EqualTo("/api/pet/sync"));
        Assert.That(pulled.Id, Is.EqualTo("avatar-live2d"));
        Assert.That(pulled.Name, Is.EqualTo("星尘"));
        Assert.That(pulled.Description, Does.Contain("来自 Web 后台"));
        Assert.That(pulled.Prompt, Does.Contain("温柔"));
        Assert.That(pulled.Prompt, Does.Contain("额外设定"));
    }

    [Test]
    public async Task WebApiClientPullsPackageManifestEnvelope()
    {
        RecordingHandler handler = new() { UsePackageManifestEnvelope = true };
        WebApiClient client = new(new HttpClient(handler)
        {
            BaseAddress = new Uri("https://foxd.example/")
        }, new WebBridgeServiceConfig { ApiToken = "secret-token" });

        WebBridgePackageManifest manifest = await client.PullPackageManifest("xiayu-character-bundle", CancellationToken.None);

        Assert.That(handler.Requests[0].RequestUri?.PathAndQuery, Is.EqualTo("/api/webbridge/packages/xiayu-character-bundle/manifest"));
        Assert.That(handler.Requests[0].Headers.Authorization?.Parameter, Is.EqualTo("secret-token"));
        Assert.That(manifest.PackageId, Is.EqualTo("xiayu-character-bundle"));
        Assert.That(manifest.ActivationPolicy.AutoApply, Is.False);
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
    public async Task WebApiClientPullsAssetsFromWebEnvelope()
    {
        RecordingHandler handler = new() { UseAssetManifestEnvelope = true };
        WebApiClient client = new(new HttpClient(handler)
        {
            BaseAddress = new Uri("https://foxd.example/")
        }, new WebBridgeServiceConfig());

        WebAssetManifest manifest = await client.PullAssets(CancellationToken.None);

        Assert.That(handler.Requests[0].RequestUri?.PathAndQuery, Is.EqualTo("/api/pet/assets"));
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
            "/api/pet/sync",
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
        public List<string> PostedJsonBodies { get; } = new();
        public string? PostedJson { get; private set; }
        public bool UseWebPetSyncEnvelope { get; init; }
        public bool UsePackageManifestEnvelope { get; init; }
        public bool IncludePackageFile { get; init; }
        public bool UseAssetManifestEnvelope { get; init; }

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Requests.Add(request);
            if (request.Content != null)
            {
                PostedJson = await request.Content.ReadAsStringAsync(cancellationToken);
                PostedJsonBodies.Add(PostedJson);
            }

            if (request.RequestUri?.AbsolutePath == "/api/webbridge/packages/xiayu-character-bundle/files/character-card")
            {
                return new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new ByteArrayContent([1, 2, 3])
                };
            }

            if (UsePackageManifestEnvelope)
            {
                byte[] content = [1, 2, 3];
                object webResponse = new
                {
                    success = true,
                    data = new WebBridgePackageManifest
                    {
                        PackageId = "xiayu-character-bundle",
                        PackageType = "characterBundle",
                        Version = "1.0.0",
                        Files = IncludePackageFile
                            ?
                            [
                                new WebBridgePackageFile
                                {
                                    Kind = "characterCard",
                                    Url = "https://foxd.example/api/webbridge/packages/xiayu-character-bundle/files/character-card",
                                    RelativePath = "characters/xiayu/card.json",
                                    Sha256 = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(content)).ToLowerInvariant(),
                                    Size = content.Length
                                }
                            ]
                            : [],
                        ActivationPolicy = new WebBridgeActivationPolicy
                        {
                            AutoApply = false,
                            RequiresLocalConfirmation = true
                        }
                    }
                };

                return new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent(JsonSerializer.Serialize(webResponse))
                };
            }

            if (UseWebPetSyncEnvelope)
            {
                object webResponse = new
                {
                    success = true,
                    data = new
                    {
                        version = 1,
                        petName = "星尘",
                        personality = "温柔",
                        backstory = "来自 Web 后台",
                        characterExtra = "额外设定",
                        animationModel = "live2d",
                        avatarId = "avatar-live2d",
                        mappedAssets = Array.Empty<object>()
                    }
                };

                return new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent(JsonSerializer.Serialize(webResponse))
                };
            }

            object response = request.RequestUri?.AbsolutePath switch
            {
                "/api/pet/assets" => BuildAssetResponse(UseAssetManifestEnvelope),
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

        static object BuildAssetResponse(bool envelope)
        {
            WebAssetManifest manifest = new()
            {
                Files =
                [
                    new WebAssetFile
                    {
                        RelativePath = "model/Mao/texture.png",
                        ContentBase64 = Convert.ToBase64String([1, 2, 3])
                    }
                ]
            };

            if (envelope == false)
                return manifest;

            return new
            {
                success = true,
                data = manifest
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

    static string GetPostedMilestone(string json)
    {
        using JsonDocument document = JsonDocument.Parse(json);
        return document.RootElement.GetProperty("milestone").GetString() ?? string.Empty;
    }
}
