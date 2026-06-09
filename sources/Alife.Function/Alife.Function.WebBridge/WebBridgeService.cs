using System;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Alife.Framework;

namespace Alife.Function.WebBridge;

[Module("FOXD WebBridge", "同步 FOXD Web 端角色配置与 Alife 本地角色状态", defaultCategory: "Alife 官方/生态集成")]
public class WebBridgeService : InteractiveModule<WebBridgeService>, IConfigurable<WebBridgeServiceConfig>, IAsyncDisposable
{
    public WebBridgeServiceConfig? Configuration { get; set; }

    public async Task<Character> PullConfig(CancellationToken cancellationToken = default)
    {
        WebAvatarConfig avatarConfig = await GetClient().PullConfig(cancellationToken);
        return CharacterSync.ToCharacter(avatarConfig);
    }

    public Task PushState(Character character, CancellationToken cancellationToken = default)
    {
        WebAvatarConfig avatarConfig = CharacterSync.ToAvatarConfig(character);
        return GetClient().PushState(avatarConfig, cancellationToken);
    }

    public override async Task AwakeAsync(AwakeContext context)
    {
        await base.AwakeAsync(context);
        Prompt("""
               此服务用于和 FOXD Web 端同步角色配置。
               当前提供配置拉取与本地状态推送能力，后续会接入角色切换和素材同步。
               """);
    }

    public ValueTask DisposeAsync()
    {
        httpClient?.Dispose();
        return ValueTask.CompletedTask;
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

    HttpClient? httpClient;
    WebApiClient? webApiClient;
}
