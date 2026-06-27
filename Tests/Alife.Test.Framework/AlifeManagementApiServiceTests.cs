using System.Net;
using System.Net.Http.Headers;
using System.Net.Sockets;
using System.Text.Json;
using Alife.Framework;
using Alife.Function.WebBridge;
using Microsoft.SemanticKernel;

namespace Alife.Test.Framework;

public class AlifeManagementApiServiceTests
{
    [Test]
    public void HealthResponseSerializesStableContract()
    {
        AlifeHealthResponse response = new(
            Status: "healthy",
            Service: "Alife",
            Version: "local",
            TimestampUtc: DateTimeOffset.Parse("2026-06-22T00:00:00Z"));

        string json = JsonSerializer.Serialize(response, AlifeManagementJson.Options);

        Assert.That(json, Does.Contain("\"status\":\"healthy\""));
        Assert.That(json, Does.Contain("\"service\":\"Alife\""));
        Assert.That(json, Does.Contain("\"version\":\"local\""));
        Assert.That(json, Does.Contain("\"timestampUtc\""));
    }

    [Test]
    public void ManagementApiOptionsDefaultToLocalhostAndDisabled()
    {
        AlifeManagementApiOptions options = new();

        Assert.That(options.Enabled, Is.False);
        Assert.That(options.BindUrl, Is.EqualTo("http://127.0.0.1:8787/"));
        Assert.That(options.RequireBearerToken, Is.True);
        Assert.That(options.RequestTimeoutSeconds, Is.EqualTo(10));
    }

    [Test]
    public void WebBridgeServiceConfigCarriesDisabledManagementApiDefaults()
    {
        WebBridgeServiceConfig config = new();

        Assert.That(config.ManagementApi.Enabled, Is.False);
        Assert.That(config.ManagementStatus.Agent, Is.EqualTo("local"));
        Assert.That(config.ManagementStatus.QChatEnabled, Is.False);
        Assert.That(config.ManagementStatus.VisionEnabled, Is.False);
        Assert.That(config.ManagementStatus.VisionStatus, Is.EqualTo("disabled"));
        Assert.That(config.ManagementStatus.VisionReason, Is.EqualTo("vision_disabled"));
        Assert.That(config.ManagementStatus.TtsEnabled, Is.False);
        Assert.That(config.ManagementStatus.TtsStatus, Is.EqualTo("disabled"));
        Assert.That(config.ManagementStatus.TtsReason, Is.EqualTo("tts_disabled"));
        Assert.That(config.ManagementStatus.OutboxEnabled, Is.False);
    }

    [Test]
    public void ManagementApiServiceBuildsReadOnlyStatus()
    {
        AlifeManagementApiService service = new(
            agent: "xiayu",
            ownerId: "3045846738",
            botId: "2905391496",
            qchatEnabled: true,
            visionEnabled: true,
            ttsEnabled: true,
            outboxEnabled: true);

        AlifeRuntimeStatusResponse status = service.GetStatus();

        Assert.That(status.Status, Is.EqualTo("healthy"));
        Assert.That(status.Agent, Is.EqualTo("xiayu"));
        Assert.That(status.OwnerId, Is.EqualTo("3045846738"));
        Assert.That(status.BotId, Is.EqualTo("2905391496"));
        Assert.That(status.QChatEnabled, Is.True);
        Assert.That(status.OutboxEnabled, Is.True);
        Assert.That(status.VisionStatus, Is.EqualTo("ready"));
        Assert.That(status.TtsStatus, Is.EqualTo("ready"));
    }

    [Test]
    public void VisionStatusCarriesReadinessReasonAndModel()
    {
        AlifeManagementApiService service = new(
            agent: "xiayu",
            ownerId: "3045846738",
            botId: "2905391496",
            qchatEnabled: true,
            visionEnabled: true,
            ttsEnabled: false,
            outboxEnabled: true,
            visionStatus: "missing_api_key",
            visionReason: "agnes_api_key_missing",
            visionModel: "agnes-2.0-flash",
            visionMaxImagesPerMessage: 2);

        AlifeVisionStatusResponse status = service.GetVisionStatus(apiKeyConfigured: false);

        Assert.Multiple(() =>
        {
            Assert.That(status.Enabled, Is.True);
            Assert.That(status.Ready, Is.False);
            Assert.That(status.Status, Is.EqualTo("missing_api_key"));
            Assert.That(status.Reason, Is.EqualTo("agnes_api_key_missing"));
            Assert.That(status.Provider, Is.EqualTo("agnes"));
            Assert.That(status.Model, Is.EqualTo("agnes-2.0-flash"));
            Assert.That(status.ApiKeyConfigured, Is.False);
            Assert.That(status.MaxImagesPerMessage, Is.EqualTo(2));
        });
    }

    [Test]
    public void TtsStatusCarriesReadinessReason()
    {
        AlifeManagementApiService service = new(
            agent: "xiayu",
            ownerId: "3045846738",
            botId: "2905391496",
            qchatEnabled: true,
            visionEnabled: false,
            ttsEnabled: true,
            outboxEnabled: true,
            ttsStatus: "endpoint_unreachable",
            ttsReason: "gpt_sovits_endpoint_unreachable");

        AlifeTtsStatusResponse status = service.GetTtsStatus();

        Assert.Multiple(() =>
        {
            Assert.That(status.Enabled, Is.True);
            Assert.That(status.Ready, Is.False);
            Assert.That(status.Status, Is.EqualTo("endpoint_unreachable"));
            Assert.That(status.Reason, Is.EqualTo("gpt_sovits_endpoint_unreachable"));
            Assert.That(status.Provider, Is.EqualTo("gpt-sovits"));
        });
    }

    [Test]
    public async Task ManagementApiHostServesHealthAndStatusWithBearerToken()
    {
        int port = GetFreeTcpPort();
        string token = "secret-token";
        string bindUrl = $"http://127.0.0.1:{port}/";
        using IDisposable tokenScope = new TestEnvironmentVariableScope("ALIFE_WEB_MANAGEMENT_TOKEN", token);

        AlifeManagementApiService service = new(
            agent: "xiayu",
            ownerId: "3045846738",
            botId: "2905391496",
            qchatEnabled: true,
            visionEnabled: true,
            ttsEnabled: false,
            outboxEnabled: true);

        await using AlifeManagementApiHost host = new(
            service,
            new AlifeManagementApiOptions
            {
                Enabled = true,
                BindUrl = bindUrl,
                RequireBearerToken = true
            });

        await host.StartAsync();

        using HttpClient client = new()
        {
            BaseAddress = new Uri(bindUrl)
        };

        using HttpResponseMessage unauthorized = await client.GetAsync("/api/alife/health");
        Assert.That(unauthorized.StatusCode, Is.EqualTo(HttpStatusCode.Unauthorized));

        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

        string health = await client.GetStringAsync("/api/alife/health");
        string status = await client.GetStringAsync("/api/alife/status");

        Assert.Multiple(() =>
        {
            Assert.That(health, Does.Contain("\"status\":\"healthy\""));
            Assert.That(health, Does.Contain("\"service\":\"Alife\""));
            Assert.That(status, Does.Contain("\"agent\":\"xiayu\""));
            Assert.That(status, Does.Contain("\"qchatEnabled\":true"));
            Assert.That(status, Does.Contain("\"outboxEnabled\":true"));
        });
    }

    [Test]
    public async Task WebBridgeServiceStartsManagementApiWhenConfigured()
    {
        int port = GetFreeTcpPort();
        string bindUrl = $"http://127.0.0.1:{port}/";
        WebBridgeService service = new()
        {
            Configuration = new WebBridgeServiceConfig
            {
                ManagementApi = new AlifeManagementApiOptions
                {
                    Enabled = true,
                    BindUrl = bindUrl,
                    RequireBearerToken = false
                },
                ManagementStatus = new AlifeManagementStatusOptions
                {
                    Agent = "xiayu",
                    OwnerId = "3045846738",
                    BotId = "2905391496",
                    QChatEnabled = true,
                    VisionEnabled = true,
                    TtsEnabled = true,
                    OutboxEnabled = true,
                    PersonaMode = "owner_extreme"
                }
            }
        };
        Kernel kernel = Kernel.CreateBuilder().Build();
        ChatActivity chatActivity = new(new Character { Name = "WebBridge" }, kernel, null!, null!, []);

        try
        {
            await service.StartAsync(kernel, chatActivity);

            using HttpClient client = new()
            {
                BaseAddress = new Uri(bindUrl)
            };

            string health = await client.GetStringAsync("/api/alife/health");
            string status = await client.GetStringAsync("/api/alife/status");
            string qchat = await client.GetStringAsync("/api/alife/qchat/status");
            string vision = await client.GetStringAsync("/api/alife/vision/status");
            string tts = await client.GetStringAsync("/api/alife/tts/status");

            Assert.Multiple(() =>
            {
                Assert.That(health, Does.Contain("\"status\":\"healthy\""));
                Assert.That(status, Does.Contain("\"agent\":\"xiayu\""));
                Assert.That(status, Does.Contain("\"qchatEnabled\":true"));
                Assert.That(status, Does.Contain("\"ttsEnabled\":true"));
                Assert.That(qchat, Does.Contain("\"personaMode\":\"owner_extreme\""));
                Assert.That(vision, Does.Contain("\"enabled\":true"));
                Assert.That(tts, Does.Contain("\"enabled\":true"));
            });
        }
        finally
        {
            await service.DestroyAsync();
        }
    }

    [Test]
    public async Task WebBridgeServiceCanRetryManagementApiStartAfterMissingTokenIsFixed()
    {
        int port = GetFreeTcpPort();
        string tokenName = $"ALIFE_WEB_MANAGEMENT_TOKEN_{Guid.NewGuid():N}";
        string bindUrl = $"http://127.0.0.1:{port}/";
        using TestEnvironmentVariableScope tokenScope = new(tokenName, null);
        WebBridgeService service = new()
        {
            Configuration = new WebBridgeServiceConfig
            {
                ManagementApi = new AlifeManagementApiOptions
                {
                    Enabled = true,
                    BindUrl = bindUrl,
                    RequireBearerToken = true,
                    BearerTokenEnvironmentVariable = tokenName
                },
                ManagementStatus = new AlifeManagementStatusOptions
                {
                    Agent = "xiayu",
                    QChatEnabled = true
                }
            }
        };
        Kernel kernel = Kernel.CreateBuilder().Build();
        ChatActivity chatActivity = new(new Character { Name = "WebBridge" }, kernel, null!, null!, []);

        Assert.ThrowsAsync<InvalidOperationException>(() => service.StartAsync(kernel, chatActivity));

        Environment.SetEnvironmentVariable(tokenName, "retry-token");

        try
        {
            await service.StartAsync(kernel, chatActivity);

            using HttpClient client = new()
            {
                BaseAddress = new Uri(bindUrl)
            };
            client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", "retry-token");

            string status = await client.GetStringAsync("/api/alife/status");

            Assert.That(status, Does.Contain("\"agent\":\"xiayu\""));
        }
        finally
        {
            await service.DestroyAsync();
        }
    }

    static int GetFreeTcpPort()
    {
        TcpListener listener = new(IPAddress.Loopback, 0);
        listener.Start();
        int port = ((IPEndPoint)listener.LocalEndpoint).Port;
        listener.Stop();
        return port;
    }

    sealed class TestEnvironmentVariableScope : IDisposable
    {
        readonly string name;
        readonly string? originalValue;

        public TestEnvironmentVariableScope(string name, string? value)
        {
            this.name = name;
            originalValue = Environment.GetEnvironmentVariable(name);
            Environment.SetEnvironmentVariable(name, value);
        }

        public void Dispose()
        {
            Environment.SetEnvironmentVariable(name, originalValue);
        }
    }
}
