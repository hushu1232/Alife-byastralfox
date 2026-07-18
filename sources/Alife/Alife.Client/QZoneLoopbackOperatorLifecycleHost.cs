using Alife.Framework;
using Alife.Function.QChat;
using Alife.Platform;
using Autofac;

namespace Alife;

public sealed class QZoneLoopbackOperatorLifecycleHost : IAsyncDisposable
{
    readonly ChatActivitySystem chatActivitySystem;
    readonly SemaphoreSlim lifecycleGate = new(1, 1);
    readonly Dictionary<ChatActivity, QZoneLoopbackOperatorHost> hosts = [];
    readonly HashSet<ChatActivity> stoppedActivities = [];
    bool disposed;

    public QZoneLoopbackOperatorLifecycleHost(ChatActivitySystem chatActivitySystem)
    {
        this.chatActivitySystem = chatActivitySystem ?? throw new ArgumentNullException(nameof(chatActivitySystem));
        chatActivitySystem.Activated += OnActivated;
        chatActivitySystem.Destroying += OnDestroying;
    }

    void OnActivated(ChatActivity activity)
    {
        _ = StartForActivityAsync(activity);
    }

    void OnDestroying(ChatActivity activity)
    {
        StopForActivityAsync(activity).GetAwaiter().GetResult();
    }

    async Task StartForActivityAsync(ChatActivity activity)
    {
        await lifecycleGate.WaitAsync().ConfigureAwait(false);
        try
        {
            if (disposed || stoppedActivities.Contains(activity) || hosts.ContainsKey(activity))
                return;
            if (activity.ModuleService.TryResolve(out QZoneService? qzoneService) == false)
                return;

            string configuredUrl = qzoneService.Configuration?.QZoneLoopbackOperatorUrl ?? string.Empty;
            if (string.IsNullOrWhiteSpace(configuredUrl))
                configuredUrl = Environment.GetEnvironmentVariable("ALIFE_QZONE_LOOPBACK_OPERATOR_URL") ?? string.Empty;
            if (string.IsNullOrWhiteSpace(configuredUrl))
                return;
            if (QZoneLoopbackOperatorEndpoint.TryCreate(
                    configuredUrl,
                    out QZoneLoopbackOperatorEndpoint? endpoint,
                    out QZoneLoopbackOperatorResultCode code) == false)
            {
                AlifeTerminal.LogWarning($"QQ Zone loopback operator configuration was rejected: {code}.");
                return;
            }

            QZoneLoopbackOperatorHost? host = null;
            try
            {
                host = new QZoneLoopbackOperatorHost(endpoint!, qzoneService);
                await host.StartAsync().ConfigureAwait(false);
                if (disposed || stoppedActivities.Contains(activity))
                {
                    await host.DisposeAsync().ConfigureAwait(false);
                    return;
                }

                hosts.Add(activity, host);
            }
            catch
            {
                if (host != null)
                    await host.DisposeAsync().ConfigureAwait(false);
                AlifeTerminal.LogWarning("QQ Zone loopback operator did not start.");
            }
        }
        finally
        {
            lifecycleGate.Release();
        }
    }

    async Task StopForActivityAsync(ChatActivity activity)
    {
        await lifecycleGate.WaitAsync().ConfigureAwait(false);
        try
        {
            stoppedActivities.Add(activity);
            if (hosts.Remove(activity, out QZoneLoopbackOperatorHost? host))
                await host.DisposeAsync().ConfigureAwait(false);
        }
        finally
        {
            lifecycleGate.Release();
        }
    }

    public async ValueTask DisposeAsync()
    {
        chatActivitySystem.Activated -= OnActivated;
        chatActivitySystem.Destroying -= OnDestroying;

        await lifecycleGate.WaitAsync().ConfigureAwait(false);
        try
        {
            if (disposed)
                return;

            disposed = true;
            foreach (QZoneLoopbackOperatorHost host in hosts.Values)
                await host.DisposeAsync().ConfigureAwait(false);
            hosts.Clear();
        }
        finally
        {
            lifecycleGate.Release();
        }
    }
}
