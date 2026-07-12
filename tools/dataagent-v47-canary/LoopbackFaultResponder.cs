using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;

namespace Alife.Tools.DataAgentV47Canary;

public enum LoopbackFaultBehavior
{
    Valid,
    Delayed,
    MalformedJson,
    UnsafeAuthority,
    FailFirstThenAccept
}

public sealed class LoopbackFaultResponder : IAsyncDisposable
{
    readonly TcpListener listener;
    readonly CancellationTokenSource cancellation = new();
    readonly LoopbackFaultBehavior behavior;
    readonly TimeSpan delay;
    readonly Task serverTask;
    int requestCount;
    int stopped;

    LoopbackFaultResponder(LoopbackFaultBehavior behavior, TimeSpan delay)
    {
        this.behavior = behavior;
        this.delay = delay;
        listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        Endpoint = new Uri($"http://127.0.0.1:{((IPEndPoint)listener.LocalEndpoint).Port}");
        serverTask = ServeAsync();
    }

    public Uri Endpoint { get; }
    public int RequestCount => Volatile.Read(ref requestCount);
    public bool IsStopped => Volatile.Read(ref stopped) == 1;

    public static Task<LoopbackFaultResponder> StartAsync(
        LoopbackFaultBehavior behavior, TimeSpan delay)
    {
        if (delay < TimeSpan.Zero || delay > TimeSpan.FromSeconds(10))
            throw new ArgumentOutOfRangeException(nameof(delay));
        return Task.FromResult(new LoopbackFaultResponder(behavior, delay));
    }

    public async Task WaitForRequestAsync(TimeSpan timeout)
    {
        DateTimeOffset deadline = DateTimeOffset.UtcNow.Add(timeout);
        while (RequestCount == 0 && DateTimeOffset.UtcNow < deadline)
            await Task.Delay(5);
        if (RequestCount == 0)
            throw new TimeoutException("loopback_request_not_observed");
    }

    async Task ServeAsync()
    {
        try
        {
            while (cancellation.IsCancellationRequested == false)
            {
                TcpClient client = await listener.AcceptTcpClientAsync(cancellation.Token);
                _ = HandleAsync(client, cancellation.Token);
            }
        }
        catch (OperationCanceledException) when (cancellation.IsCancellationRequested)
        {
        }
        catch (SocketException) when (cancellation.IsCancellationRequested)
        {
        }
    }

    async Task HandleAsync(TcpClient client, CancellationToken token)
    {
        using (client)
        {
            int sequence = Interlocked.Increment(ref requestCount);
            NetworkStream stream = client.GetStream();
            string body = await ReadBodyAsync(stream, token);
            if (behavior == LoopbackFaultBehavior.FailFirstThenAccept && sequence == 1)
                return;
            if (behavior == LoopbackFaultBehavior.Delayed)
                await Task.Delay(delay, token);

            string responseBody = behavior == LoopbackFaultBehavior.MalformedJson
                ? "{"
                : BuildResponse(body, behavior == LoopbackFaultBehavior.UnsafeAuthority);
            byte[] payload = Encoding.UTF8.GetBytes(responseBody);
            byte[] header = Encoding.ASCII.GetBytes(
                $"HTTP/1.1 200 OK\r\nContent-Type: application/json\r\nContent-Length: {payload.Length}\r\nConnection: close\r\n\r\n");
            await stream.WriteAsync(header, token);
            await stream.WriteAsync(payload, token);
        }
    }

    static async Task<string> ReadBodyAsync(NetworkStream stream, CancellationToken token)
    {
        using StreamReader reader = new(stream, Encoding.ASCII, false, 4096, true);
        await reader.ReadLineAsync(token);
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
        StringBuilder body = new();
        if (chunked)
        {
            while (true)
            {
                int size = Convert.ToInt32((await reader.ReadLineAsync(token) ?? "0").Split(';')[0], 16);
                if (size == 0)
                {
                    await reader.ReadLineAsync(token);
                    break;
                }
                char[] chunk = new char[size];
                int read = 0;
                while (read < size)
                    read += await reader.ReadAsync(chunk.AsMemory(read), token);
                body.Append(chunk);
                await reader.ReadLineAsync(token);
            }
        }
        else
        {
            char[] chars = new char[contentLength];
            int read = 0;
            while (read < contentLength)
                read += await reader.ReadAsync(chars.AsMemory(read), token);
            body.Append(chars);
        }
        return body.ToString();
    }

    static string BuildResponse(string requestBody, bool unsafeAuthority)
    {
        using JsonDocument request = JsonDocument.Parse(requestBody);
        string requestId = request.RootElement.GetProperty("RequestId").GetString()!;
        return JsonSerializer.Serialize(new
        {
            RequestId = requestId,
            Accepted = true,
            ReasonCode = "langgraph_advisory_accepted",
            SelectedNodes = new[] { "query_planner" },
            NodeProgress = Array.Empty<object>(),
            TraceSummary = "advisory complete",
            ContextContribution = "authority=csharp",
            FallbackRequired = false,
            NoSqlAuthority = unsafeAuthority == false,
            ReadOnly = true,
            RequestedToolNames = Array.Empty<string>(),
            RequestsCheckpointMutation = false,
            RequestsVisibleText = false
        });
    }

    public async ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref stopped, 1) == 1)
            return;
        cancellation.Cancel();
        listener.Stop();
        await serverTask;
        cancellation.Dispose();
    }
}
