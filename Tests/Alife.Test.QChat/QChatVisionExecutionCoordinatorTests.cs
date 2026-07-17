using System.Collections.Concurrent;
using Alife.Function.QChat;
using NUnit.Framework;

namespace Alife.Test.QChat;

[TestFixture]
public sealed class QChatVisionExecutionCoordinatorTests
{
    [Test]
    public async Task ExecutesAgnesThenGrokExactlyOnceAfterRetryableFailure()
    {
        RecordingClient agnes = RecordingClient.Fail("agnes", QChatImageRecognitionFailureKind.Timeout);
        RecordingClient grok = RecordingClient.Success("grok", "ocr result");
        QChatVisionExecutionCoordinator coordinator = new(new Dictionary<string, IQChatImageRecognitionClient>
        {
            ["agnes"] = agnes,
            ["grok"] = grok
        });

        QChatImageRecognitionProviderResult result = await coordinator.AnalyzeAsync(
            botId: 1,
            ownerPriority: false,
            imageKey: "image-a",
            new QChatVisionRoutePlan("agnes", "grok", "default_image", TimeSpan.FromSeconds(12)),
            Request("image-a"));

        Assert.Multiple(() =>
        {
            Assert.That(result.Success, Is.True);
            Assert.That(result.ProviderName, Is.EqualTo("grok"));
            Assert.That(agnes.Calls, Is.EqualTo(1));
            Assert.That(grok.Calls, Is.EqualTo(1));
        });
    }

    [Test]
    public async Task DeduplicatesSameImageWithinTtl()
    {
        TaskCompletionSource<QChatImageRecognitionProviderResult> completion = new();
        RecordingClient agnes = RecordingClient.Waiting("agnes", completion);
        QChatVisionExecutionCoordinator coordinator = new(new Dictionary<string, IQChatImageRecognitionClient>
        {
            ["agnes"] = agnes
        }, duplicateTtl: TimeSpan.FromMinutes(1));
        QChatVisionRoutePlan route = new("agnes", null, "default_image", TimeSpan.FromSeconds(12));

        Task<QChatImageRecognitionProviderResult> first = coordinator.AnalyzeAsync(1, false, "image-a", route, Request("image-a"));
        Task<QChatImageRecognitionProviderResult> duplicate = coordinator.AnalyzeAsync(1, false, "image-a", route, Request("image-a"));

        await agnes.WaitForCallAsync();
        completion.SetResult(QChatImageRecognitionProviderResult.Ok("agnes", "agnes-2.0-flash", "cat"));
        QChatImageRecognitionProviderResult[] results = await Task.WhenAll(first, duplicate);

        Assert.Multiple(() =>
        {
            Assert.That(agnes.Calls, Is.EqualTo(1));
            Assert.That(results.Select(result => result.Success), Is.All.True);
        });
    }

    [Test]
    public async Task OwnerItemRunsBeforeWaitingGuestItemForTheSameBot()
    {
        TaskCompletionSource<QChatImageRecognitionProviderResult> firstCompletion = new();
        RecordingClient agnes = RecordingClient.WaitingFirstThenSuccess("agnes", firstCompletion);
        QChatVisionExecutionCoordinator coordinator = new(new Dictionary<string, IQChatImageRecognitionClient>
        {
            ["agnes"] = agnes
        });
        QChatVisionRoutePlan route = new("agnes", null, "default_image", TimeSpan.FromSeconds(12));

        Task<QChatImageRecognitionProviderResult> active = coordinator.AnalyzeAsync(1, false, "active", route, Request("active"));
        await agnes.WaitForCallAsync();
        Task<QChatImageRecognitionProviderResult> guest = coordinator.AnalyzeAsync(1, false, "guest", route, Request("guest"));
        Task<QChatImageRecognitionProviderResult> owner = coordinator.AnalyzeAsync(1, true, "owner", route, Request("owner"));
        firstCompletion.SetResult(QChatImageRecognitionProviderResult.Ok("agnes", "agnes-2.0-flash", "active"));

        await Task.WhenAll(active, owner, guest);
        Assert.That(agnes.RequestLabels, Is.EqualTo(new[] { "active", "owner", "guest" }));
    }

    [Test]
    public async Task MissingPublicUrlDoesNotCallAnyProviderOrFallback()
    {
        RecordingClient agnes = RecordingClient.Success("agnes", "unused");
        RecordingClient grok = RecordingClient.Success("grok", "unused");
        QChatVisionExecutionCoordinator coordinator = new(new Dictionary<string, IQChatImageRecognitionClient>
        {
            ["agnes"] = agnes,
            ["grok"] = grok
        });

        QChatImageRecognitionProviderResult result = await coordinator.AnalyzeAsync(
            1, false, "missing", new("agnes", "grok", "default_image", TimeSpan.FromSeconds(12)),
            new QChatImageRecognitionProviderRequest("file://local-only", "missing", "agnes-2.0-flash", 80));

        Assert.Multiple(() =>
        {
            Assert.That(result.FailureKind, Is.EqualTo(QChatImageRecognitionFailureKind.MissingPublicUrl));
            Assert.That(agnes.Calls, Is.Zero);
            Assert.That(grok.Calls, Is.Zero);
        });
    }

    [Test]
    public async Task OpenCircuitSkipsPrimaryUntilItsCooldownExpires()
    {
        DateTimeOffset now = new(2026, 7, 17, 0, 0, 0, TimeSpan.Zero);
        RecordingClient agnes = RecordingClient.Fail("agnes", QChatImageRecognitionFailureKind.Timeout);
        RecordingClient grok = RecordingClient.Success("grok", "fallback");
        QChatVisionExecutionCoordinator coordinator = new(
            new Dictionary<string, IQChatImageRecognitionClient> { ["agnes"] = agnes, ["grok"] = grok },
            retryableFailureThreshold: 1,
            circuitOpenDuration: TimeSpan.FromSeconds(30),
            utcNow: () => now);
        QChatVisionRoutePlan route = new("agnes", "grok", "default_image", TimeSpan.FromSeconds(12));

        await coordinator.AnalyzeAsync(1, false, "first", route, Request("first"));
        await coordinator.AnalyzeAsync(1, false, "second", route, Request("second"));
        now = now.AddSeconds(31);
        await coordinator.AnalyzeAsync(1, false, "third", route, Request("third"));

        Assert.Multiple(() =>
        {
            Assert.That(agnes.Calls, Is.EqualTo(2));
            Assert.That(grok.Calls, Is.EqualTo(3));
        });
    }

    static QChatImageRecognitionProviderRequest Request(string label) => new(
        "https://example.invalid/" + label + ".jpg", label, "agnes-2.0-flash", 80);

    sealed class RecordingClient : IQChatImageRecognitionClient
    {
        readonly Func<QChatImageRecognitionProviderRequest, Task<QChatImageRecognitionProviderResult>> handler;
        readonly TaskCompletionSource<object?> firstCall = new(TaskCreationOptions.RunContinuationsAsynchronously);

        RecordingClient(string providerName, Func<QChatImageRecognitionProviderRequest, Task<QChatImageRecognitionProviderResult>> handler)
        {
            ProviderName = providerName;
            this.handler = handler;
        }

        public string ProviderName { get; }
        public int Calls { get; private set; }
        public ConcurrentQueue<string> RequestLabels { get; } = new();

        public static RecordingClient Success(string providerName, string content) => new(providerName, request =>
            Task.FromResult(QChatImageRecognitionProviderResult.Ok(providerName, request.Model, content)));

        public static RecordingClient Fail(string providerName, QChatImageRecognitionFailureKind failureKind) => new(providerName, request =>
            Task.FromResult(QChatImageRecognitionProviderResult.Fail(providerName, request.Model, failureKind, "test_failure")));

        public static RecordingClient Waiting(string providerName, TaskCompletionSource<QChatImageRecognitionProviderResult> completion) => new(providerName, _ => completion.Task);

        public static RecordingClient WaitingFirstThenSuccess(string providerName, TaskCompletionSource<QChatImageRecognitionProviderResult> firstCompletion)
        {
            int call = 0;
            return new RecordingClient(providerName, request => Interlocked.Increment(ref call) == 1
                ? firstCompletion.Task
                : Task.FromResult(QChatImageRecognitionProviderResult.Ok(providerName, request.Model, request.Prompt)));
        }

        public Task WaitForCallAsync() => firstCall.Task;

        public async Task<QChatImageRecognitionProviderResult> AnalyzeAsync(
            QChatImageRecognitionProviderRequest request,
            CancellationToken cancellationToken = default)
        {
            Calls++;
            RequestLabels.Enqueue(request.Prompt);
            firstCall.TrySetResult(null);
            return await handler(request);
        }
    }
}
