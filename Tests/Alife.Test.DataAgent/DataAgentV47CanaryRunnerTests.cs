using Alife.Tools.DataAgentV47Canary;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;

namespace Alife.Test.DataAgent;

public sealed class DataAgentV47CanaryRunnerTests
{
    [Test]
    public async Task RunnerDrivesTwentyRealGovernedLoopbackHandshakesWithStableHealth()
    {
        await using CanaryResponder responder = new();
        DataAgentV47CanaryArguments arguments = new(
            responder.Endpoint, "Outputs/dataagent-v4.7-live-canary", 20, 2000, 0);

        DataAgentV47CanaryRunResult result =
            await new DataAgentV47CanaryRunner().RunAsync(arguments);

        Assert.Multiple(() =>
        {
            Assert.That(result.Accepted, Is.True);
            Assert.That(result.AcceptedCount, Is.EqualTo(20));
            Assert.That(result.NetworkAttemptCount, Is.EqualTo(20));
            Assert.That(result.ObservationSnapshot!.ObservationCount, Is.EqualTo(20));
            Assert.That(result.ObservationSnapshot.FallbackCount, Is.Zero);
            Assert.That(result.RuntimeIdentity!.StableAcrossWindow, Is.True);
            Assert.That(responder.HandshakeCount, Is.EqualTo(20));
            Assert.That(responder.HealthCount, Is.EqualTo(2));
        });
    }

    [Test]
    public void ArgumentsRequireBoundedLoopbackCanaryConfiguration()
    {
        string[] valid =
        [
            "--endpoint", "http://127.0.0.1:8765",
            "--output", "Outputs/dataagent-v4.7-live-canary",
            "--request-count", "20",
            "--timeout-ms", "800",
            "--runtime-restart-count", "0"
        ];

        DataAgentV47CanaryArgumentResult parsed = DataAgentV47CanaryArguments.Parse(valid);

        Assert.Multiple(() =>
        {
            Assert.That(parsed.Accepted, Is.True);
            Assert.That(parsed.Value!.Endpoint.IsLoopback, Is.True);
            Assert.That(parsed.Value.RequestCount, Is.EqualTo(20));
            Assert.That(parsed.Value.TimeoutMs, Is.EqualTo(800));
            Assert.That(parsed.Value.RuntimeRestartCount, Is.Zero);
        });
    }

    [TestCase("--endpoint", "https://example.com")]
    [TestCase("--request-count", "19")]
    [TestCase("--request-count", "257")]
    [TestCase("--timeout-ms", "99")]
    [TestCase("--timeout-ms", "10001")]
    [TestCase("--runtime-restart-count", "-1")]
    [TestCase("--runtime-restart-count", "2")]
    public void ArgumentsRejectUnsafeOrOutOfRangeValues(string key, string value)
    {
        string[] args =
        [
            "--endpoint", "http://127.0.0.1:8765",
            "--output", "Outputs/dataagent-v4.7-live-canary",
            "--request-count", "20",
            "--timeout-ms", "800",
            "--runtime-restart-count", "0"
        ];
        int index = Array.IndexOf(args, key);
        args[index + 1] = value;

        DataAgentV47CanaryArgumentResult parsed = DataAgentV47CanaryArguments.Parse(args);

        Assert.That(parsed.Accepted, Is.False);
        Assert.That(parsed.ReasonCode, Does.Match("^[a-z0-9_]+$"));
    }

    [Test]
    public void ArgumentsRejectMissingRequiredValuesAndUnknownKeys()
    {
        Assert.Multiple(() =>
        {
            Assert.That(DataAgentV47CanaryArguments.Parse([]).Accepted, Is.False);
            Assert.That(DataAgentV47CanaryArguments.Parse(["--unknown", "value"]).Accepted, Is.False);
        });
    }

    sealed class CanaryResponder : IAsyncDisposable
    {
        const string InstanceId = "12345678-1234-5678-9234-567812345678";
        readonly TcpListener listener = new(IPAddress.Loopback, 0);
        readonly CancellationTokenSource cancellation = new();
        readonly Task serverTask;
        int handshakeCount;
        int healthCount;

        public CanaryResponder()
        {
            listener.Start();
            Endpoint = new Uri($"http://127.0.0.1:{((IPEndPoint)listener.LocalEndpoint).Port}");
            serverTask = ServeAsync();
        }

        public Uri Endpoint { get; }
        public int HandshakeCount => Volatile.Read(ref handshakeCount);
        public int HealthCount => Volatile.Read(ref healthCount);

        async Task ServeAsync()
        {
            try
            {
                while (cancellation.IsCancellationRequested == false)
                {
                    using TcpClient client = await listener.AcceptTcpClientAsync(cancellation.Token);
                    await HandleAsync(client, cancellation.Token);
                }
            }
            catch (OperationCanceledException) when (cancellation.IsCancellationRequested)
            {
            }
        }

        async Task HandleAsync(TcpClient client, CancellationToken token)
        {
            NetworkStream stream = client.GetStream();
            using StreamReader reader = new(stream, Encoding.ASCII, false, 4096, leaveOpen: true);
            string requestLine = await reader.ReadLineAsync(token) ?? string.Empty;
            int contentLength = 0;
            bool chunked = false;
            while (true)
            {
                string line = await reader.ReadLineAsync(token) ?? string.Empty;
                if (line.Length == 0)
                    break;
                if (line.StartsWith("Content-Length:", StringComparison.OrdinalIgnoreCase))
                    contentLength = int.Parse(line[15..].Trim());
                if (line.Equals("Transfer-Encoding: chunked", StringComparison.OrdinalIgnoreCase))
                    chunked = true;
            }
            string requestBody;
            if (chunked)
            {
                StringBuilder buffer = new();
                while (true)
                {
                    string sizeLine = await reader.ReadLineAsync(token) ?? "0";
                    int size = Convert.ToInt32(sizeLine.Split(';')[0], 16);
                    if (size == 0)
                    {
                        await reader.ReadLineAsync(token);
                        break;
                    }
                    char[] chunk = new char[size];
                    int chunkOffset = 0;
                    while (chunkOffset < size)
                        chunkOffset += await reader.ReadAsync(chunk.AsMemory(chunkOffset), token);
                    buffer.Append(chunk);
                    await reader.ReadLineAsync(token);
                }
                requestBody = buffer.ToString();
            }
            else
            {
                char[] chars = new char[contentLength];
                int offset = 0;
                while (offset < chars.Length)
                    offset += await reader.ReadAsync(chars.AsMemory(offset), token);
                requestBody = new string(chars);
            }

            string body;
            if (requestLine.Contains(" /health ", StringComparison.Ordinal))
            {
                Interlocked.Increment(ref healthCount);
                body = JsonSerializer.Serialize(new
                {
                    ok = true, ready = true, runtimeMode = "langgraph", langGraphLoaded = true,
                    langGraphVersion = "0.3.34", graphCompiled = true, contractVersion = "v4.7",
                    graphVersion = "dataagent-advisory-v1", runtimeInstanceId = InstanceId,
                    configurationFingerprint = new string('a', 64), startedAtUnixSeconds = 1_783_820_000
                });
            }
            else
            {
                Interlocked.Increment(ref handshakeCount);
                using JsonDocument request = JsonDocument.Parse(requestBody);
                string requestId = request.RootElement.GetProperty("RequestId").GetString()!;
                body = JsonSerializer.Serialize(new
                {
                    RequestId = requestId, Accepted = true, ReasonCode = "langgraph_advisory_accepted",
                    SelectedNodes = new[] { "query_planner" }, NodeProgress = Array.Empty<object>(),
                    TraceSummary = "advisory complete", ContextContribution = "authority=csharp",
                    FallbackRequired = false, NoSqlAuthority = true, ReadOnly = true,
                    RequestedToolNames = Array.Empty<string>(), RequestsCheckpointMutation = false,
                    RequestsVisibleText = false
                });
            }
            byte[] payload = Encoding.UTF8.GetBytes(body);
            byte[] header = Encoding.ASCII.GetBytes(
                $"HTTP/1.1 200 OK\r\nContent-Type: application/json\r\nContent-Length: {payload.Length}\r\nConnection: close\r\n\r\n");
            await stream.WriteAsync(header, token);
            await stream.WriteAsync(payload, token);
        }

        public async ValueTask DisposeAsync()
        {
            cancellation.Cancel();
            listener.Stop();
            await serverTask;
            cancellation.Dispose();
        }
    }
}
