using System;
using System.IO;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Alife.Framework;
using Alife.Platform;
using Microsoft.SemanticKernel;

namespace Alife.Function.WebBridge;

[Module("FOXD WebBridge", "同步 FOXD Web 端角色配置与 astralfox-alife 本地角色状态", defaultCategory: "astralfox-alife/生态集成")]
public class WebBridgeService : InteractiveModule<WebBridgeService>, IConfigurable<WebBridgeServiceConfig>, IAsyncDisposable
{
    public WebBridgeService() {}

    public WebBridgeService(
        WebApiClient webApiClient,
        ICharacterBridgeStore characterStore,
        WebAssetSync? assetSync = null,
        WebBridgePackageInstaller? packageInstaller = null)
    {
        this.webApiClient = webApiClient;
        this.characterStore = characterStore;
        this.assetSync = assetSync;
        this.packageInstaller = packageInstaller;
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

    public async Task<WebBridgeInstallResult> InstallPackage(
        string packageId,
        CancellationToken cancellationToken = default)
    {
        WebApiClient client = GetClient();
        WebBridgePackageManifest? manifest = null;
        try
        {
            manifest = await client.PullPackageManifest(packageId, cancellationToken);
            await TryReportMilestone(client, manifest, WebBridgeSyncMilestones.ManifestFetched, error: null, cancellationToken);

            WebBridgeInstallResult result = await CreatePackageInstaller(manifest, client).Install(manifest, cancellationToken);
            if (result.Status == WebBridgePackageStatus.PendingActivation)
            {
                if (packageInstaller != null)
                    await TryReportMilestone(client, manifest, WebBridgeSyncMilestones.PackageStaged, error: null, cancellationToken);

                await TryReportMilestone(client, manifest, WebBridgeSyncMilestones.ConfirmationRequested, error: null, cancellationToken);
            }

            return result;
        }
        catch (Exception exception)
        {
            manifest ??= new WebBridgePackageManifest { PackageId = packageId };
            await TryReportMilestone(client, manifest, WebBridgeSyncMilestones.PackageFailed, ErrorFromException(exception), cancellationToken);
            throw;
        }
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
        if (Configuration?.ManagementApi.Enabled == true)
            await StartManagementApi();

        if (Configuration?.AutoSyncEnabled == true)
            StartSyncLoop();
    }

    public override async Task DestroyAsync()
    {
        await StopManagementApi();
        await StopSyncLoop();
        await base.DestroyAsync();
    }

    public async ValueTask DisposeAsync()
    {
        await StopManagementApi();
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

    WebBridgePackageInstaller CreatePackageInstaller(WebBridgePackageManifest manifest, WebApiClient client)
    {
        if (packageInstaller != null)
            return packageInstaller;

        return new WebBridgePackageInstaller(
            Configuration?.PackageRootPath ?? Path.Combine(AlifePath.StorageFolderPath, "WebBridge"),
            file => client.DownloadPackageFile(file),
            (milestone, cancellationToken) => TryReportMilestone(client, manifest, milestone, error: null, cancellationToken));
    }

    async Task TryReportMilestone(
        WebApiClient client,
        WebBridgePackageManifest manifest,
        string milestone,
        WebBridgeSyncErrorReport? error,
        CancellationToken cancellationToken)
    {
        try
        {
            await client.ReportSyncMilestone(new WebBridgeSyncMilestoneReport
            {
                Milestone = milestone,
                PackageVersion = ParsePackageVersion(manifest.Version),
                ReportedAt = DateTimeOffset.UtcNow.ToString("O"),
                Error = error
            }, cancellationToken);
        }
        catch (Exception exception)
        {
            AlifeTerminal.LogWarning($"WebBridge sync milestone report failed: {milestone} ({exception.Message})");
        }
    }

    static long? ParsePackageVersion(string version)
    {
        return long.TryParse(version, out long parsed) && parsed > 0 && parsed <= 9007199254740991
            ? parsed
            : null;
    }

    static WebBridgeSyncErrorReport ErrorFromException(Exception exception)
    {
        return new WebBridgeSyncErrorReport
        {
            Code = ErrorCodeFromException(exception),
            Message = exception.Message,
            Detail = exception.ToString()
        };
    }

    static string ErrorCodeFromException(Exception exception)
    {
        if (exception is HttpRequestException)
            return WebBridgeSyncErrorCodes.PackageDownloadFailed;

        if (exception.Message.Contains("SHA-256 mismatch", StringComparison.OrdinalIgnoreCase))
            return WebBridgeSyncErrorCodes.PackageHashMismatch;

        if (exception.Message.Contains("escapes install root", StringComparison.OrdinalIgnoreCase) ||
            exception.Message.Contains("RelativePath is required", StringComparison.OrdinalIgnoreCase))
            return WebBridgeSyncErrorCodes.PackageSecurityBlocked;

        return WebBridgeSyncErrorCodes.PackageApplyFailed;
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

    async Task StartManagementApi()
    {
        if (managementApiHost != null)
            return;

        WebBridgeServiceConfig activeConfig = Configuration ?? new WebBridgeServiceConfig();
        AlifeManagementStatusOptions status = activeConfig.ManagementStatus;
        AlifeManagementApiService service = new(
            agent: status.Agent,
            ownerId: status.OwnerId,
            botId: status.BotId,
            qchatEnabled: status.QChatEnabled,
            visionEnabled: status.VisionEnabled,
            ttsEnabled: status.TtsEnabled,
            outboxEnabled: status.OutboxEnabled,
            personaMode: status.PersonaMode,
            visionStatus: status.VisionStatus,
            visionReason: status.VisionReason,
            visionModel: status.VisionModel,
            visionMaxImagesPerMessage: status.VisionMaxImagesPerMessage,
            ttsStatus: status.TtsStatus,
            ttsReason: status.TtsReason);
        AlifeManagementApiHost host = new(service, activeConfig.ManagementApi);
        try
        {
            await host.StartAsync();
            managementApiHost = host;
        }
        catch
        {
            await host.DisposeAsync();
            throw;
        }
    }

    async Task StopManagementApi()
    {
        if (managementApiHost == null)
            return;

        AlifeManagementApiHost host = managementApiHost;
        managementApiHost = null;
        await host.DisposeAsync();
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
    WebBridgePackageInstaller? packageInstaller;
    AlifeManagementApiHost? managementApiHost;
    CancellationTokenSource? syncCancellation;
    Task? syncTask;
}
