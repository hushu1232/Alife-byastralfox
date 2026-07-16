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
        string output = Path.Combine(Path.GetTempPath(), $"dataagent-v47-runner-{Guid.NewGuid():N}");
        DataAgentV47CanaryArguments arguments = new(
            responder.Endpoint, output, 20, 2000, 0);

        try
        {
            DataAgentV47CanaryRunResult result =
                await new DataAgentV47CanaryRunner().RunAsync(arguments);

            Assert.Multiple(() =>
            {
                Assert.That(result.Accepted, Is.True);
                Assert.That(result.AcceptedCount, Is.EqualTo(20));
                Assert.That(result.NetworkAttemptCount, Is.EqualTo(20));
                Assert.That(result.OutcomeReasonCounts,
                    Is.EqualTo(new Dictionary<string, int> { ["handshake_accepted"] = 20 }));
                Assert.That(result.ObservationSnapshot!.ObservationCount, Is.EqualTo(20));
                Assert.That(result.ObservationSnapshot.FallbackCount, Is.Zero);
                Assert.That(result.RuntimeIdentity!.StableAcrossWindow, Is.True);
                Assert.That(result.FaultDrillResult!.Accepted, Is.True);
                Assert.That(result.FaultDrillResult.Drills, Has.Count.EqualTo(7));
                Assert.That(result.ClosureResult!.Accepted, Is.True);
                Assert.That(result.ArtifactWriteResult!.Written, Is.True);
                Assert.That(File.Exists(result.ArtifactWriteResult.FilePath), Is.True);
                Assert.That(responder.HandshakeCount, Is.EqualTo(20));
                Assert.That(responder.ContentLengthHandshakeCount, Is.EqualTo(20));
                Assert.That(responder.ChunkedHandshakeCount, Is.Zero);
                Assert.That(responder.HealthCount, Is.EqualTo(2));
            });
        }
        finally
        {
            if (Directory.Exists(output))
                Directory.Delete(output, recursive: true);
        }
    }

    [Test]
    [NonParallelizable]
    public async Task CanaryCliRejectedWindowEmitsOnlyFixedSafeFields()
    {
        await using CanaryResponder responder = new(rejectHandshakes: true);
        string output = Path.Combine(Path.GetTempPath(), $"dataagent-v47-cli-{Guid.NewGuid():N}");
        TextWriter originalOutput = Console.Out;
        using StringWriter capturedOutput = new();
        int exitCode;
        try
        {
            Console.SetOut(capturedOutput);
            exitCode = await Program.Main(
            [
                "--endpoint", responder.Endpoint.ToString(),
                "--output", output,
                "--request-count", "20",
                "--timeout-ms", "2000",
                "--runtime-restart-count", "0"
            ]);
        }
        finally
        {
            Console.SetOut(originalOutput);
            if (Directory.Exists(output))
                Directory.Delete(output, recursive: true);
        }

        string[] lines = capturedOutput.ToString().Split(
            ["\r\n", "\n"], StringSplitOptions.RemoveEmptyEntries);
        string[] keys = lines.Select(line => line.Split('=', 2)[0]).ToArray();

        Assert.Multiple(() =>
        {
            Assert.That(exitCode, Is.EqualTo(1));
            Assert.That(keys, Is.EqualTo(new[]
            {
                "canary_accepted", "reason_code", "accepted_count", "network_attempt_count",
                "outcome_reason_counts", "runtime_instance_id"
            }));
            Assert.That(lines, Does.Contain("canary_accepted=false"));
            Assert.That(lines, Does.Contain("reason_code=v4_7_canary_window_rejected"));
            Assert.That(lines, Does.Contain("accepted_count=0"));
            Assert.That(lines, Does.Contain("network_attempt_count=3"));
            Assert.That(lines, Does.Contain(
                "outcome_reason_counts=production_shadow_circuit_open:17,production_shadow_unavailable:3"));
            Assert.That(lines.Single(line => line.StartsWith("runtime_instance_id=", StringComparison.Ordinal)),
                Does.Match("^runtime_instance_id=[a-f0-9-]{36}$"));
            Assert.That(responder.HandshakeCount, Is.EqualTo(3));
            Assert.That(capturedOutput.ToString(), Does.Not.Contain(output));
            Assert.That(capturedOutput.ToString(), Does.Not.Contain("sidecar_http_status"));
            Assert.That(capturedOutput.ToString(), Does.Not.Contain("exception"));
        });
    }

    [Test]
    public async Task RunnerUsesArtifactFailureReasonWhenAcceptedClosureCannotBeWritten()
    {
        await using CanaryResponder responder = new();
        string occupiedPath = Path.Combine(
            Path.GetTempPath(), $"dataagent-v47-runner-occupied-{Guid.NewGuid():N}.txt");
        File.WriteAllText(occupiedPath, "sensitive-runner-payload");
        DataAgentV47CanaryArguments arguments = new(
            responder.Endpoint, occupiedPath, 20, 2000, 0);

        try
        {
            DataAgentV47CanaryRunResult result =
                await new DataAgentV47CanaryRunner().RunAsync(arguments);

            Assert.Multiple(() =>
            {
                Assert.That(result.Accepted, Is.False);
                Assert.That(result.ReasonCode, Is.EqualTo("v4_7_artifact_write_failed"));
                Assert.That(result.AcceptedCount, Is.EqualTo(20));
                Assert.That(result.NetworkAttemptCount, Is.EqualTo(20));
                Assert.That(result.ClosureResult!.Accepted, Is.True);
                Assert.That(result.ArtifactWriteResult!.Written, Is.False);
                Assert.That(result.ArtifactWriteResult.ReasonCode,
                    Is.EqualTo("v4_7_artifact_write_failed"));
                Assert.That(result.ArtifactWriteResult.FileName, Is.EqualTo("redacted"));
                Assert.That(result.ArtifactWriteResult.FilePath, Is.Empty);
                Assert.That(File.ReadAllText(occupiedPath), Is.EqualTo("sensitive-runner-payload"));
            });
        }
        finally
        {
            File.Delete(occupiedPath);
        }
    }

    [Test]
    [NonParallelizable]
    public async Task CanaryCliArtifactFailureEmitsOnlyFixedSafeAggregateFields()
    {
        await using CanaryResponder responder = new();
        string occupiedPath = Path.Combine(
            Path.GetTempPath(), $"dataagent-v47-cli-occupied-{Guid.NewGuid():N}.txt");
        const string payload = "sensitive-cli-payload";
        File.WriteAllText(occupiedPath, payload);
        TextWriter originalOutput = Console.Out;
        TextWriter originalError = Console.Error;
        using StringWriter capturedOutput = new();
        using StringWriter capturedError = new();
        int exitCode;

        try
        {
            Console.SetOut(capturedOutput);
            Console.SetError(capturedError);
            exitCode = await Program.Main(
            [
                "--endpoint", responder.Endpoint.ToString(),
                "--output", occupiedPath,
                "--request-count", "20",
                "--timeout-ms", "2000",
                "--runtime-restart-count", "0"
            ]);
        }
        finally
        {
            Console.SetOut(originalOutput);
            Console.SetError(originalError);
            File.Delete(occupiedPath);
        }

        string[] lines = capturedOutput.ToString().Split(
            ["\r\n", "\n"], StringSplitOptions.RemoveEmptyEntries);
        string[] keys = lines.Select(line => line.Split('=', 2)[0]).ToArray();
        string combined = capturedOutput + Environment.NewLine + capturedError;

        Assert.Multiple(() =>
        {
            Assert.That(exitCode, Is.EqualTo(1));
            Assert.That(keys, Is.EqualTo(new[]
            {
                "canary_accepted", "reason_code", "accepted_count", "network_attempt_count",
                "outcome_reason_counts", "runtime_instance_id"
            }));
            Assert.That(lines, Does.Contain("canary_accepted=false"));
            Assert.That(lines, Does.Contain("reason_code=v4_7_artifact_write_failed"));
            Assert.That(lines, Does.Contain("accepted_count=20"));
            Assert.That(lines, Does.Contain("network_attempt_count=20"));
            Assert.That(lines, Does.Contain("outcome_reason_counts=handshake_accepted:20"));
            Assert.That(capturedError.ToString(), Is.Empty);
            Assert.That(combined, Does.Not.Contain(occupiedPath));
            Assert.That(combined, Does.Not.Contain(payload));
            Assert.That(combined, Does.Not.Contain("Exception"));
            Assert.That(combined, Does.Not.Contain("exception"));
            Assert.That(combined, Does.Not.Contain(" at "));
        });
    }

    [Test]
    public void CanaryCliHasFixedCatchAllErrorBoundaryWithoutExceptionEcho()
    {
        string root = FindRepoRoot(TestContext.CurrentContext.TestDirectory);
        string source = File.ReadAllText(Path.Combine(
            root, "tools", "dataagent-v47-canary", "Program.cs"));

        Assert.Multiple(() =>
        {
            Assert.That(source, Does.Contain("catch (Exception)"));
            Assert.That(source, Does.Contain("reason_code=v4_7_canary_unexpected_failure"));
            Assert.That(source, Does.Not.Contain("Console.Error.WriteLine(exception"));
            Assert.That(source, Does.Not.Contain("Console.WriteLine(exception"));
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
        int contentLengthHandshakeCount;
        int chunkedHandshakeCount;
        readonly bool rejectHandshakes;

        public CanaryResponder(bool rejectHandshakes = false)
        {
            this.rejectHandshakes = rejectHandshakes;
            listener.Start();
            Endpoint = new Uri($"http://127.0.0.1:{((IPEndPoint)listener.LocalEndpoint).Port}");
            serverTask = ServeAsync();
        }

        public Uri Endpoint { get; }
        public int HandshakeCount => Volatile.Read(ref handshakeCount);
        public int HealthCount => Volatile.Read(ref healthCount);
        public int ContentLengthHandshakeCount => Volatile.Read(ref contentLengthHandshakeCount);
        public int ChunkedHandshakeCount => Volatile.Read(ref chunkedHandshakeCount);

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
            int statusCode = 200;
            string statusText = "OK";
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
                if (contentLength > 0)
                    Interlocked.Increment(ref contentLengthHandshakeCount);
                if (chunked)
                    Interlocked.Increment(ref chunkedHandshakeCount);
                if (rejectHandshakes)
                {
                    statusCode = 503;
                    statusText = "Service Unavailable";
                    body = JsonSerializer.Serialize(new { error = "runtime_not_ready" });
                }
                else
                {
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
            }
            byte[] payload = Encoding.UTF8.GetBytes(body);
            byte[] header = Encoding.ASCII.GetBytes(
                $"HTTP/1.1 {statusCode} {statusText}\r\nContent-Type: application/json\r\nContent-Length: {payload.Length}\r\nConnection: close\r\n\r\n");
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

    static string FindRepoRoot(string start)
    {
        DirectoryInfo? current = new(start);
        while (current is not null)
        {
            if (File.Exists(Path.Combine(current.FullName, "Alife.slnx")))
                return current.FullName;
            current = current.Parent;
        }
        throw new DirectoryNotFoundException("repo_root_not_found");
    }

}
