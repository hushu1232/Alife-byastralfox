using System;
using System.IO;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Alife.Framework;
using Alife.Platform;
using Microsoft.SemanticKernel;

namespace Alife.Function.WebBridge;

[Module("FOXD WebBridge", "同步 FOXD Web 端角色配置与 Alife 本地角色状态", defaultCategory: "Alife 官方/生态集成")]
public class WebBridgeService : InteractiveModule<WebBridgeService>, IConfigurable<WebBridgeServiceConfig>, IAsyncDisposable
{
    public WebBridgeService() {}

    public WebBridgeService(WebApiClient webApiClient, ICharacterBridgeStore characterStore, WebAssetSync? assetSync = null)
    {
        this.webApiClient = webApiClient;
        this.characterStore = characterStore;
        this.assetSync = assetSync;
    }

    public WebBridgeServiceConfig? Configuration { get; set; }

    public async Task<Character> PullConfig(CancellationToken cancellationToken = default)
    {
        WebAvatarConfig avatarConfig = await GetClient().PullConfig(cancellationToken);
        if (characterStore != null)
            return characterStore.UpsertCharacter(avatarConfig);

        return CharacterSync.ToCharacter(avatarConfig);
    }

    public Task PushState(Character character, CancellationToken cancellationToken = default)
    {
        WebAvatarConfig avatarConfig = CharacterSync.ToAvatarConfig(character);
        return GetClient().PushState(avatarConfig, cancellationToken);
    }

    public async Task<WebBridgeSyncResult> SyncOnce(CancellationToken cancellationToken = default)
    {
        Character character = await PullConfig(cancellationToken);
        bool assetsSynced = false;
        if (Configuration?.SyncAssetsEnabled != false)
        {
            await PullAssets(cancellationToken);
            assetsSynced = true;
        }

        await PushState(character, cancellationToken);
        return new WebBridgeSyncResult(character, assetsSynced);
    }

    public Task SetAvatar(string avatarId, CancellationToken cancellationToken = default)
    {
        return GetClient().SetAvatar(avatarId, cancellationToken);
    }

    public async Task PullAssets(CancellationToken cancellationToken = default)
    {
        WebAssetManifest manifest = await GetClient().PullAssets(cancellationToken);
        await GetAssetSync().SyncAssets(manifest, cancellationToken);
    }

    public override async Task AwakeAsync(AwakeContext context)
    {
        await base.AwakeAsync(context);
        if (context.Services.GetService(typeof(CharacterSystem)) is CharacterSystem characterSystem)
            characterStore ??= new CharacterSystemBridgeStore(characterSystem);

        Prompt("""
               此服务用于和 FOXD Web 端同步角色配置。
               当前提供配置拉取与本地状态推送能力，后续会接入角色切换和素材同步。
               """);
    }

    public override async Task StartAsync(Kernel kernel, ChatActivity chatActivity)
    {
        await base.StartAsync(kernel, chatActivity);
        if (Configuration?.AutoSyncEnabled == true)
            StartSyncLoop();
    }

    public override async Task DestroyAsync()
    {
        await StopSyncLoop();
        await base.DestroyAsync();
    }

    public async ValueTask DisposeAsync()
    {
        await StopSyncLoop();
        httpClient?.Dispose();
    }

    WebApiClient GetClient()
    {
        if (webApiClient != null)
            return webApiClient;

        WebBridgeServiceConfig activeConfig = Configuration ?? new WebBridgeServiceConfig();
        httpClient = new HttpClient();
        webApiClient = new WebApiClient(httpClient, activeConfig);
        return webApiClient;
    }

    WebAssetSync GetAssetSync()
    {
        assetSync ??= new WebAssetSync(Configuration?.AssetRootPath ?? Path.Combine(AlifePath.StorageFolderPath, "WebBridge", "Assets"));
        return assetSync;
    }

    void StartSyncLoop()
    {
        syncCancellation?.Cancel();
        syncCancellation?.Dispose();
        syncCancellation = new CancellationTokenSource();
        syncTask = RunSyncLoop(syncCancellation.Token);
    }

    async Task StopSyncLoop()
    {
        syncCancellation?.Cancel();
        if (syncTask != null)
            await syncTask;
        syncCancellation?.Dispose();
        syncCancellation = null;
        syncTask = null;
    }

    async Task RunSyncLoop(CancellationToken cancellationToken)
    {
        int intervalMilliseconds = Math.Max(1000, Configuration?.SyncIntervalMilliseconds ?? 30000);
        try
        {
            while (cancellationToken.IsCancellationRequested == false)
            {
                await SyncOnce(cancellationToken);
                await Task.Delay(intervalMilliseconds, cancellationToken);
            }
        }
        catch (OperationCanceledException) {}
        catch (Exception e)
        {
            AlifeTerminal.LogError(e.ToString());
        }
    }

    HttpClient? httpClient;
    WebApiClient? webApiClient;
    ICharacterBridgeStore? characterStore;
    WebAssetSync? assetSync;
    CancellationTokenSource? syncCancellation;
    Task? syncTask;
}
