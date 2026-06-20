using Alife.Function.DesktopControl;

namespace Alife.Test.DesktopControl;

public sealed class DesktopBusinessTaskQueueTests
{
    [Test]
    public async Task ExecuteAsync_DeniesUnsupportedDraftBeforeQueueing()
    {
        string path = CreateJobPath();
        FakeDesktopApprovedDraftExecutor executor = new();
        FakeDraftController draftController = new();
        DesktopBusinessTaskQueue queue = new(executor, draftController, path);

        DesktopBusinessExecutionResult result = await queue.ExecuteAsync(CreateDraft("open powershell"));

        Assert.Multiple(() =>
        {
            Assert.That(result.Success, Is.False);
            Assert.That(result.Message, Does.Contain("desktop_execution=denied"));
            Assert.That(result.Message, Does.Contain("reason=unsupported_action"));
            Assert.That(result.MarksDraftExecuted, Is.False);
            Assert.That(executor.Calls, Is.Zero);
            Assert.That(queue.GetRecentJobs(10), Is.Empty);
            Assert.That(File.Exists(path), Is.False);
        });
    }

    [Test]
    public async Task ExecuteAsync_DoesNotQueueSameDraftTwiceWhileRunning()
    {
        string path = CreateJobPath();
        TaskCompletionSource<DesktopBusinessExecutionResult> completion = new(TaskCreationOptions.RunContinuationsAsynchronously);
        FakeDesktopApprovedDraftExecutor executor = new()
        {
            ExecuteOverride = _ => completion.Task
        };
        FakeDraftController draftController = new();
        DesktopBusinessTaskQueue queue = new(executor, draftController, path);
        DesktopActionDraftEntry draft = CreateDraft("open notepad");

        DesktopBusinessExecutionResult first = await queue.ExecuteAsync(draft);
        await WaitUntilAsync(() => executor.Calls == 1);
        DesktopBusinessExecutionResult second = await queue.ExecuteAsync(draft);

        completion.SetResult(new DesktopBusinessExecutionResult(true, "desktop_execution=started action=open_notepad"));
        await WaitUntilAsync(() => queue.GetJob(ExtractJobId(first.Message))?.Status == DesktopBusinessJobStatus.Succeeded);

        Assert.Multiple(() =>
        {
            Assert.That(first.Success, Is.True);
            Assert.That(first.Message, Does.Contain("desktop_execution=queued"));
            Assert.That(first.Message, Does.Contain("draft=desktop-draft-test"));
            Assert.That(first.MarksDraftExecuted, Is.False);
            Assert.That(second.Success, Is.True);
            Assert.That(second.Message, Does.Contain("existing=true"));
            Assert.That(second.MarksDraftExecuted, Is.False);
            Assert.That(executor.Calls, Is.EqualTo(1));
            Assert.That(queue.GetRecentJobs(10), Has.Count.EqualTo(1));
            Assert.That(draftController.Updates, Has.Count.EqualTo(1));
            Assert.That(draftController.Updates.Single(), Is.EqualTo(DesktopActionDraftStatus.Executed));
        });
    }

    [Test]
    public async Task ExecuteAsync_SuccessfulJobMarksDraftExecutedAndPersistsStatus()
    {
        string path = CreateJobPath();
        FakeDesktopApprovedDraftExecutor executor = new();
        FakeDraftController draftController = new();
        DesktopBusinessTaskQueue queue = new(executor, draftController, path);

        DesktopBusinessExecutionResult queued = await queue.ExecuteAsync(CreateDraft("open notepad"));
        string jobId = ExtractJobId(queued.Message);

        await WaitUntilAsync(() => queue.GetJob(jobId)?.Status == DesktopBusinessJobStatus.Succeeded);
        DesktopBusinessJobEntry? job = queue.GetJob(jobId);
        DesktopBusinessTaskQueue reloaded = new(executor, draftController, path);

        Assert.Multiple(() =>
        {
            Assert.That(queued.Success, Is.True);
            Assert.That(queued.MarksDraftExecuted, Is.False);
            Assert.That(job, Is.Not.Null);
            Assert.That(job!.Status, Is.EqualTo(DesktopBusinessJobStatus.Succeeded));
            Assert.That(job.Message, Does.Contain("desktop_execution=started"));
            Assert.That(draftController.Updates, Is.EqualTo(new[] { DesktopActionDraftStatus.Executed }));
            Assert.That(File.ReadAllText(path), Does.Contain("\"Status\":\"Succeeded\""));
            Assert.That(reloaded.GetJob(jobId), Is.Not.Null);
            Assert.That(reloaded.GetJob(jobId)!.Status, Is.EqualTo(DesktopBusinessJobStatus.Succeeded));
        });
    }

    [Test]
    public async Task ExecuteAsync_QueuesCalculatorDraftWhenWhitelisted()
    {
        string path = CreateJobPath();
        FakeDesktopApprovedDraftExecutor executor = new()
        {
            Result = new DesktopBusinessExecutionResult(true, "desktop_execution=started action=open_calculator")
        };
        FakeDraftController draftController = new();
        DesktopBusinessTaskQueue queue = new(executor, draftController, path);

        DesktopBusinessExecutionResult queued = await queue.ExecuteAsync(CreateDraft("open calculator"));
        string jobId = ExtractJobId(queued.Message);

        await WaitUntilAsync(() => queue.GetJob(jobId)?.Status == DesktopBusinessJobStatus.Succeeded);
        DesktopBusinessJobEntry? job = queue.GetJob(jobId);

        Assert.Multiple(() =>
        {
            Assert.That(queued.Success, Is.True);
            Assert.That(job, Is.Not.Null);
            Assert.That(job!.RequestedAction, Is.EqualTo("open calculator"));
            Assert.That(job.Message, Does.Contain("action=open_calculator"));
            Assert.That(executor.Calls, Is.EqualTo(1));
            Assert.That(draftController.Updates, Is.EqualTo(new[] { DesktopActionDraftStatus.Executed }));
        });
    }

    [Test]
    public async Task ExecuteAsync_SuccessfulJobNotifiesCompletionSinkOnce()
    {
        string path = CreateJobPath();
        FakeDesktopApprovedDraftExecutor executor = new();
        FakeDraftController draftController = new();
        FakeCompletionSink completionSink = new();
        DesktopBusinessTaskQueue queue = new(
            executor,
            draftController,
            path,
            completionSink: completionSink);

        DesktopBusinessExecutionResult queued = await queue.ExecuteAsync(CreateDraft("open notepad"));
        string jobId = ExtractJobId(queued.Message);

        await WaitUntilAsync(() => completionSink.Notifications.Count == 1);
        DesktopBusinessJobEntry notification = completionSink.Notifications.Single();

        Assert.Multiple(() =>
        {
            Assert.That(notification.JobId, Is.EqualTo(jobId));
            Assert.That(notification.Status, Is.EqualTo(DesktopBusinessJobStatus.Succeeded));
            Assert.That(notification.Message, Does.Contain("desktop_execution=started"));
        });
    }

    [Test]
    public async Task ExecuteAsync_FailedJobLeavesDraftApproved()
    {
        string path = CreateJobPath();
        FakeDesktopApprovedDraftExecutor executor = new()
        {
            Result = new DesktopBusinessExecutionResult(false, "desktop_execution=failed reason=test_failure")
        };
        FakeDraftController draftController = new();
        DesktopBusinessTaskQueue queue = new(executor, draftController, path);

        DesktopBusinessExecutionResult queued = await queue.ExecuteAsync(CreateDraft("open notepad"));
        string jobId = ExtractJobId(queued.Message);

        await WaitUntilAsync(() => queue.GetJob(jobId)?.Status == DesktopBusinessJobStatus.Failed);
        DesktopBusinessJobEntry? job = queue.GetJob(jobId);

        Assert.Multiple(() =>
        {
            Assert.That(queued.Success, Is.True);
            Assert.That(job, Is.Not.Null);
            Assert.That(job!.Status, Is.EqualTo(DesktopBusinessJobStatus.Failed));
            Assert.That(job.Message, Does.Contain("reason=test_failure"));
            Assert.That(draftController.Updates, Is.Empty);
            Assert.That(File.ReadAllText(path), Does.Contain("\"Status\":\"Failed\""));
        });
    }

    static DesktopActionDraftEntry CreateDraft(string requestedAction)
    {
        return new DesktopActionDraftEntry(
            DateTimeOffset.Parse("2026-06-20T12:00:00+08:00"),
            "desktop-draft-test",
            3045846738,
            "xiayu",
            requestedAction,
            DesktopActionDraftStatus.Approved);
    }

    static string CreateJobPath()
    {
        string root = Path.Combine(Path.GetTempPath(), "alife-desktop-business-job-tests", Guid.NewGuid().ToString("N"));
        return Path.Combine(root, "desktop-business-jobs.jsonl");
    }

    static string ExtractJobId(string message)
    {
        string? token = message
            .Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .FirstOrDefault(part => part.StartsWith("job=desktop-job-", StringComparison.Ordinal));
        Assert.That(token, Is.Not.Null);
        return token!["job=".Length..];
    }

    static async Task WaitUntilAsync(Func<bool> condition)
    {
        DateTime deadline = DateTime.UtcNow + TimeSpan.FromSeconds(2);
        while (DateTime.UtcNow < deadline)
        {
            if (condition())
                return;

            await Task.Delay(25);
        }

        Assert.Fail("Condition was not met before timeout.");
    }

    sealed class FakeDesktopApprovedDraftExecutor : IDesktopApprovedDraftExecutor
    {
        public int Calls { get; private set; }
        public DesktopBusinessExecutionResult Result { get; init; } =
            new(true, "desktop_execution=started action=open_notepad");
        public Func<DesktopActionDraftEntry, Task<DesktopBusinessExecutionResult>>? ExecuteOverride { get; init; }

        public Task<DesktopBusinessExecutionResult> ExecuteAsync(
            DesktopActionDraftEntry draft,
            CancellationToken cancellationToken = default)
        {
            Calls++;
            return ExecuteOverride?.Invoke(draft) ?? Task.FromResult(Result);
        }
    }

    sealed class FakeDraftController : IDesktopActionDraftController
    {
        public List<DesktopActionDraftStatus> Updates { get; } = new();

        public DesktopActionDraftUpdateResult UpdateStatus(
            DesktopActionRequest request,
            DesktopActionDraftStatus status)
        {
            Updates.Add(status);
            DesktopActionDraftEntry entry = new(
                DateTimeOffset.Now,
                request.Detail,
                request.ActorUserId,
                request.AgentId,
                "open notepad",
                status);
            return new DesktopActionDraftUpdateResult(
                true,
                entry,
                $"desktop_draft=updated id={request.Detail} status={status} execution=disabled");
        }
    }

    sealed class FakeCompletionSink : IDesktopBusinessJobCompletionSink
    {
        public List<DesktopBusinessJobEntry> Notifications { get; } = new();

        public Task NotifyCompletionAsync(
            DesktopBusinessJobEntry job,
            CancellationToken cancellationToken = default)
        {
            Notifications.Add(job);
            return Task.CompletedTask;
        }
    }
}
