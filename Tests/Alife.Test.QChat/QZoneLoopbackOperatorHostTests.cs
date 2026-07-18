using System.Net;
using System.Net.Http;
using System.Net.Http.Json;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using Alife.Function.QChat;
using NUnit.Framework;

namespace Alife.Test.QChat;

[TestFixture]
public sealed class QZoneLoopbackOperatorHostTests
{
    [Test]
    public async Task HostedInstance_DispatchesOnlyToInjectedQZoneService_AndStopsWhenDisposed()
    {
        RecordingRuntime hostedRuntime = new();
        RecordingRuntime otherRuntime = new();
        await using QZoneLoopbackOperatorHost host = new(
            CreateEndpoint(),
            CreateLiveService(hostedRuntime));
        using HttpClient client = new();

        await host.StartAsync();

        using HttpResponseMessage response = await client.PostAsJsonAsync(
            host.Endpoint.Uri,
            new QZoneLoopbackOperatorRequest {
                Operation = QZoneLoopbackOperatorOperation.Read,
                TargetId = 10001,
            });
        string body = await response.Content.ReadAsStringAsync();

        Assert.Multiple(() =>
        {
            Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK));
            Assert.That(JsonSerializer.Deserialize<QZoneLoopbackOperatorResult>(body), Is.EqualTo(
                QZoneLoopbackOperatorResult.Completed()));
            Assert.That(hostedRuntime.LatestPostTargets, Is.EqualTo(new[] { 10001L }));
            Assert.That(otherRuntime.LatestPostTargets, Is.Empty);
            Assert.That(host.IsRunning, Is.True);
        });

        await host.DisposeAsync();

        Assert.That(host.IsRunning, Is.False);
        Assert.ThrowsAsync<HttpRequestException>(async () => await client.PostAsJsonAsync(
            host.Endpoint.Uri,
            new QZoneLoopbackOperatorRequest {
                Operation = QZoneLoopbackOperatorOperation.Read,
                TargetId = 10001,
            }));
    }

    [Test]
    public async Task HostedInstance_ReturnsOnlyCompactSafeFailureWhenQZoneServiceThrows()
    {
        await using QZoneLoopbackOperatorHost host = new(
            CreateEndpoint(),
            CreateLiveService(new RecordingRuntime(throwOnRead: true)));
        using HttpClient client = new();

        await host.StartAsync();

        using HttpResponseMessage response = await client.PostAsJsonAsync(
            host.Endpoint.Uri,
            new QZoneLoopbackOperatorRequest {
                Operation = QZoneLoopbackOperatorOperation.Read,
                TargetId = 10001,
            });
        string body = await response.Content.ReadAsStringAsync();
        using JsonDocument document = JsonDocument.Parse(body);

        Assert.Multiple(() =>
        {
            Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK));
            Assert.That(document.RootElement.EnumerateObject().Select(property => property.Name),
                Is.EquivalentTo(new[] { "succeeded", "code" }));
            Assert.That(JsonSerializer.Deserialize<QZoneLoopbackOperatorResult>(body), Is.EqualTo(
                QZoneLoopbackOperatorResult.OperationFailed()));
            Assert.That(body, Does.Not.Contain("cookie").IgnoreCase);
            Assert.That(body, Does.Not.Contain("bkn").IgnoreCase);
            Assert.That(body, Does.Not.Contain("token").IgnoreCase);
            Assert.That(body, Does.Not.Contain("raw exception").IgnoreCase);
        });
    }

    [Test]
    public async Task DisposeAsync_CompletesWhenTheCallerUsesANonPumpingSynchronizationContext()
    {
        QZoneLoopbackOperatorHost host = new(CreateEndpoint(), CreateLiveService(new RecordingRuntime()));
        TaskCompletionSource<Exception?> completion = new(TaskCreationOptions.RunContinuationsAsynchronously);
        Thread caller = new(() => {
            SynchronizationContext.SetSynchronizationContext(new NonPumpingSynchronizationContext());
            try
            {
                host.StartAsync().GetAwaiter().GetResult();
                host.DisposeAsync().AsTask().GetAwaiter().GetResult();
                completion.TrySetResult(null);
            }
            catch (Exception exception)
            {
                completion.TrySetResult(exception);
            }
        }) {
            IsBackground = true,
        };

        caller.Start();

        Task completed = await Task.WhenAny(completion.Task, Task.Delay(TimeSpan.FromSeconds(2)));

        Assert.That(completed, Is.SameAs(completion.Task));
        Assert.That(await completion.Task, Is.Null);
    }

    [Test]
    public async Task HostedInstance_RejectsOversizedChunkedJsonWithoutDispatching()
    {
        RecordingRuntime runtime = new();
        await using QZoneLoopbackOperatorHost host = new(CreateEndpoint(), CreateLiveService(runtime));
        using HttpClient client = new();
        using HttpRequestMessage request = new(HttpMethod.Post, host.Endpoint.Uri) {
            Content = new StringContent(
                $"{{\"operation\":\"Read\",\"target_id\":10001,\"padding\":\"{new string('x', 64 * 1024)}\"}}",
                Encoding.UTF8,
                "application/json"),
        };
        request.Headers.TransferEncodingChunked = true;

        await host.StartAsync();

        using HttpResponseMessage response = await client.SendAsync(request);
        string body = await response.Content.ReadAsStringAsync();

        Assert.Multiple(() =>
        {
            Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK));
            Assert.That(JsonSerializer.Deserialize<QZoneLoopbackOperatorResult>(body), Is.EqualTo(
                QZoneLoopbackOperatorResult.Rejected(QZoneLoopbackOperatorResultCode.InvalidRequest)));
            Assert.That(runtime.LatestPostTargets, Is.Empty);
        });
    }

    static QZoneLoopbackOperatorEndpoint CreateEndpoint()
    {
        string value = $"http://127.0.0.1:{FindAvailableLoopbackPort()}/qzone/";
        bool created = QZoneLoopbackOperatorEndpoint.TryCreate(
            value,
            out QZoneLoopbackOperatorEndpoint? endpoint,
            out QZoneLoopbackOperatorResultCode code);

        Assert.Multiple(() =>
        {
            Assert.That(created, Is.True);
            Assert.That(endpoint, Is.Not.Null);
            Assert.That(code, Is.EqualTo(QZoneLoopbackOperatorResultCode.Accepted));
        });
        return endpoint!;
    }

    static int FindAvailableLoopbackPort()
    {
        using TcpListener listener = new(IPAddress.Loopback, 0);
        listener.Start();
        return ((IPEndPoint)listener.LocalEndpoint).Port;
    }

    static QZoneService CreateLiveService(IQZoneRuntime runtime)
    {
        return new QZoneService(runtime) {
            Configuration = new QZoneServiceConfig {
                EnableQZone = true,
                DryRunExternalActions = false,
            },
        };
    }

    sealed class RecordingRuntime(bool throwOnRead = false) : IQZoneRuntime
    {
        public List<long> LatestPostTargets { get; } = [];

        public Task PublishPost(string content) => Task.CompletedTask;
        public Task Comment(long targetId, string postId, string content) => Task.CompletedTask;
        public Task ReplyComment(long targetId, string postId, string commentId, string content) => Task.CompletedTask;
        public Task LikePost(long targetId, string postId) => Task.CompletedTask;
        public Task<QZoneUploadedImage> UploadImage(QZoneImageUpload upload) => throw new NotSupportedException();
        public Task PublishImagePost(string content, IReadOnlyList<QZoneUploadedImage> images) => Task.CompletedTask;
        public Task DeletePost(QZonePostSnapshot post) => Task.CompletedTask;
        public Task DeleteComment(long targetId, string postId, string commentId) => Task.CompletedTask;
        public Task DeleteReply(long targetId, string postId, string commentId, string replyId) => Task.CompletedTask;

        public Task<QZonePostSnapshot?> GetLatestPost(long targetId)
        {
            if (throwOnRead)
                throw new InvalidOperationException("raw exception: Cookie=secret; Bkn=secret; Token=secret");

            LatestPostTargets.Add(targetId);
            return Task.FromResult<QZonePostSnapshot?>(new QZonePostSnapshot(
                "post",
                targetId,
                "synthetic test post"));
        }

        public Task<IReadOnlyList<QZoneCommentSnapshot>> GetLatestComments(long targetId, string postId, int count) =>
            Task.FromResult<IReadOnlyList<QZoneCommentSnapshot>>([]);
    }

    sealed class NonPumpingSynchronizationContext : SynchronizationContext
    {
        public override void Post(SendOrPostCallback callback, object? state)
        {
        }
    }
}
