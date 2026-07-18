using Alife.Framework;
using Alife.Function.Agent;
using Alife.Function.FunctionCaller;
using Alife.Function.QChat;
using Microsoft.SemanticKernel;
using Microsoft.SemanticKernel.Agents;
using NUnit.Framework;
using System.Net;
using System.Net.Http;
using System.IO;
using System.Reflection;
using System.Text.Json;

namespace Alife.Test.QChat;

[TestFixture]
public class QZoneServiceTests
{
    readonly List<string> directoriesToDelete = [];

    [TearDown]
    public void TearDown()
    {
        foreach (string directory in directoriesToDelete)
        {
            if (Directory.Exists(directory))
                Directory.Delete(directory, recursive: true);
        }
    }

    [Test]
    public async Task QZonePost_DryRunDoesNotCallRuntime()
    {
        FakeQZoneRuntime runtime = new();
        QZoneService service = new(runtime)
        {
            Configuration = new QZoneServiceConfig
            {
                EnableQZone = true,
                DryRunExternalActions = true
            }
        };

        QZoneActionResult result = await service.QZonePost("hello qzone");

        Assert.That(result.Executed, Is.False);
        Assert.That(result.Action, Is.EqualTo("post"));
        Assert.That(runtime.Posts, Is.Empty);
    }

    [Test]
    public async Task QZoneDeleteOwnPost_DryRunDoesNotCallRuntime()
    {
        FakeQZoneRuntime runtime = new();
        QZoneService service = new(runtime)
        {
            Configuration = new QZoneServiceConfig
            {
                EnableQZone = true,
                DryRunExternalActions = true
            }
        };
        QZonePostSnapshot post = new("post-a", 10001, "test post", "10001_post-a", "feed-key", 42);

        QZoneActionResult result = await service.QZoneDeleteOwnPost(post);

        Assert.Multiple(() =>
        {
            Assert.That(result.Executed, Is.False);
            Assert.That(result.Action, Is.EqualTo("delete_own_post"));
            Assert.That(result.Reason, Is.EqualTo("dry-run: would delete QQ Zone post"));
            Assert.That(runtime.DeletedPosts, Is.Empty);
        });
    }

    [Test]
    public async Task QZoneDeleteOwnPost_LiveDispatchesCompleteOwnPostSnapshot()
    {
        FakeQZoneRuntime runtime = new();
        QZoneService service = new(runtime)
        {
            Configuration = new QZoneServiceConfig
            {
                EnableQZone = true,
                DryRunExternalActions = false
            }
        };
        QZonePostSnapshot post = new("post-a", 10001, "test post", "10001_post-a", "feed-key", 42);

        QZoneActionResult result = await service.QZoneDeleteOwnPost(post);

        Assert.Multiple(() =>
        {
            Assert.That(result, Is.EqualTo(new QZoneActionResult("delete_own_post", true, "deleted own QQ Zone post")));
            Assert.That(runtime.DeletedPosts, Is.EqualTo(new[] { post }));
        });
    }

    [Test]
    public async Task QZoneDeleteOwnPost_LiveRejectsIncompleteMetadataWithoutCallingRuntime()
    {
        FakeQZoneRuntime runtime = new();
        QZoneService service = new(runtime)
        {
            Configuration = new QZoneServiceConfig
            {
                EnableQZone = true,
                DryRunExternalActions = false
            }
        };
        QZonePostSnapshot[] incompletePosts =
        [
            new("post-a", 10001, "test post", null, "feed-key", 42),
            new("post-a", 10001, "test post", "10001_post-a", null, 42),
            new("post-a", 10001, "test post", "10001_post-a", "feed-key", null),
        ];

        QZoneActionResult[] results = await Task.WhenAll(incompletePosts.Select(service.QZoneDeleteOwnPost));

        Assert.Multiple(() =>
        {
            Assert.That(results, Is.All.EqualTo(new QZoneActionResult("delete_own_post", false, "qzone_delete_metadata_unavailable")));
            Assert.That(runtime.DeletedPosts, Is.Empty);
        });
    }

    [Test]
    public void QZoneDeleteOwnPost_PropagatesRuntimeOwnershipRejection()
    {
        FakeQZoneRuntime runtime = new()
        {
            DeleteException = new InvalidOperationException("qzone_delete_metadata_unavailable")
        };
        QZoneService service = new(runtime)
        {
            Configuration = new QZoneServiceConfig
            {
                EnableQZone = true,
                DryRunExternalActions = false
            }
        };
        QZonePostSnapshot post = new("post-a", 20002, "other account post", "20002_post-a", "feed-key", 42);

        InvalidOperationException exception = Assert.ThrowsAsync<InvalidOperationException>(async () =>
            await service.QZoneDeleteOwnPost(post))!;

        Assert.Multiple(() =>
        {
            Assert.That(exception.Message, Is.EqualTo("qzone_delete_metadata_unavailable"));
            Assert.That(runtime.DeletedPosts, Is.Empty);
        });
    }

    [Test]
    public async Task QZonePostImage_DryRunDoesNotResolveOrCallRuntime()
    {
        FakeQZoneRuntime runtime = new();
        QZoneService service = QZoneService.CreateForImagePosting(runtime, () => throw new AssertionException("Resolver must not be created in dry-run."));
        service.Configuration = new QZoneServiceConfig
        {
            EnableQZone = true,
            DryRunExternalActions = true
        };

        QZoneActionResult result = await service.QZonePostImage("image post", "owner_url", "https://example.invalid/owner-image.jpg");

        Assert.Multiple(() =>
        {
            Assert.That(result.Executed, Is.False);
            Assert.That(result.Action, Is.EqualTo("post_image"));
            Assert.That(result.Reason, Is.EqualTo("dry-run: would publish QQ Zone image post"));
            Assert.That(runtime.ImageUploads, Is.Empty);
            Assert.That(runtime.ImagePosts, Is.Empty);
        });
    }

    [TestCase("owner_file")]
    [TestCase("generated_file")]
    [TestCase("owner_url")]
    public async Task QZonePostImage_LiveAcceptsSupportedSourceKindsAndPublishesOneImage(string sourceKind)
    {
        string directory = CreateTemporaryDirectory();
        Directory.CreateDirectory(directory);
        string localPath = Path.Combine(directory, sourceKind == "owner_file" ? "owner.png" : "generated.png");
        await File.WriteAllBytesAsync(localPath, [1, 2, 3]);
        RecordingImageHandler handler = new();
        int resolverFactoryCalls = 0;
        FakeQZoneRuntime runtime = new();
        QZoneService service = QZoneService.CreateForImagePosting(runtime, () =>
        {
            resolverFactoryCalls++;
            return new QZoneImageSourceResolver(new HttpClient(handler, disposeHandler: false));
        });
        service.Configuration = new QZoneServiceConfig
        {
            EnableQZone = true,
            DryRunExternalActions = false,
            MaxQZoneImageBytes = 8,
            MaxQZoneImagesPerPost = 1
        };
        string sourceValue = sourceKind == "owner_url"
            ? "https://example.invalid/owner-image.jpg"
            : localPath;

        QZoneActionResult result = await service.QZonePostImage("image post", sourceKind, sourceValue);

        Assert.Multiple(() =>
        {
            Assert.That(result, Is.EqualTo(new QZoneActionResult("post_image", true, "published QQ Zone image post")));
            Assert.That(resolverFactoryCalls, Is.EqualTo(1));
            Assert.That(handler.Requests, Has.Count.EqualTo(sourceKind == "owner_url" ? 1 : 0));
            Assert.That(runtime.ImageUploads, Has.Count.EqualTo(1));
            Assert.That(runtime.ImageUploads[0].Origin, Is.EqualTo(sourceKind == "generated_file"
                ? QZoneImageOrigin.Generated
                : QZoneImageOrigin.OwnerProvided));
            Assert.That(runtime.ImagePosts, Has.Count.EqualTo(1));
            Assert.That(runtime.ImagePosts[0].Content, Is.EqualTo("image post"));
            Assert.That(runtime.ImagePosts[0].Images, Has.Count.EqualTo(1));
            Assert.That(runtime.ImageOperations, Is.EqualTo(new[] { "upload", "publish" }));
        });
    }

    [Test]
    public async Task QZonePostImage_RejectsUnsupportedSourceKindWithoutResolvingOrCallingRuntime()
    {
        int resolverFactoryCalls = 0;
        FakeQZoneRuntime runtime = new();
        QZoneService service = QZoneService.CreateForImagePosting(runtime, () =>
        {
            resolverFactoryCalls++;
            throw new AssertionException("Unsupported source kind must not create a resolver.");
        });
        service.Configuration = new QZoneServiceConfig
        {
            EnableQZone = true,
            DryRunExternalActions = false
        };

        QZoneActionResult result = await service.QZonePostImage("image post", "web_search", "private source value");

        Assert.Multiple(() =>
        {
            Assert.That(result, Is.EqualTo(new QZoneActionResult("post_image", false, "qzone_image_source_invalid")));
            Assert.That(result.Reason, Does.Not.Contain("private source value"));
            Assert.That(resolverFactoryCalls, Is.Zero);
            Assert.That(runtime.ImageUploads, Is.Empty);
            Assert.That(runtime.ImagePosts, Is.Empty);
        });
    }

    [Test]
    public async Task QZonePostImage_RespectsConfiguredImageByteAndPostCountLimits()
    {
        string directory = CreateTemporaryDirectory();
        Directory.CreateDirectory(directory);
        string path = Path.Combine(directory, "generated.png");
        await File.WriteAllBytesAsync(path, [1, 2, 3]);
        int resolverFactoryCalls = 0;
        FakeQZoneRuntime runtime = new();
        QZoneService service = QZoneService.CreateForImagePosting(runtime, () =>
        {
            resolverFactoryCalls++;
            return new QZoneImageSourceResolver(new HttpClient(new RecordingImageHandler(), disposeHandler: false));
        });
        service.Configuration = new QZoneServiceConfig
        {
            EnableQZone = true,
            DryRunExternalActions = false,
            MaxQZoneImageBytes = 2,
            MaxQZoneImagesPerPost = 1
        };

        QZoneImageSourceException bytesException = Assert.ThrowsAsync<QZoneImageSourceException>(async () =>
            await service.QZonePostImage("image post", "generated_file", path))!;
        service.Configuration.MaxQZoneImageBytes = 8;
        service.Configuration.MaxQZoneImagesPerPost = 0;
        QZoneActionResult countResult = await service.QZonePostImage("image post", "generated_file", path);

        Assert.Multiple(() =>
        {
            Assert.That(bytesException.Code, Is.EqualTo("qzone_image_too_large"));
            Assert.That(resolverFactoryCalls, Is.EqualTo(1));
            Assert.That(countResult, Is.EqualTo(new QZoneActionResult("post_image", false, "qzone_image_upload_unavailable")));
            Assert.That(runtime.ImageUploads, Is.Empty);
            Assert.That(runtime.ImagePosts, Is.Empty);
        });
    }

    [Test]
    public async Task AutonomyServiceRecordsDryRunWithoutCallingRuntimeWrite()
    {
        DateTimeOffset now = new(2026, 7, 17, 12, 0, 0, TimeSpan.Zero);
        QZoneAutonomyAgentKey agentKey = QZoneAutonomyAgentKey.Create("mixu", 10001);
        QZoneAutonomyScheduler scheduler = CreateDueAutonomyScheduler(now, agentKey);
        FakeQZoneRuntime runtime = new();
        AgentAuditLogService audit = CreateAutonomyAudit();
        QZoneService service = QZoneService.CreateForAutonomy(
            runtime,
            scheduler,
            new QZoneAutonomyPersonaPolicy(),
            audit,
            () => now);
        service.Configuration = new QZoneServiceConfig
        {
            EnableQZone = true,
            EnableQZoneAutonomy = true,
            QZoneAutonomyDryRunOnly = true,
            DryRunExternalActions = true
        };

        QZoneAutonomyRunResult result = await service.RunAutonomyOnceAsync(MixuAutonomyRequest());

        Assert.Multiple(() =>
        {
            Assert.That(result.Executed, Is.False);
            Assert.That(result.ReasonCode, Is.EqualTo("dry_run"));
            Assert.That(runtime.Posts, Is.Empty);
            Assert.That(runtime.Comments, Is.Empty);
            Assert.That(runtime.Replies, Is.Empty);
            Assert.That(runtime.Likes, Is.Empty);
        });
        AssertSingleSafeAutonomyAudit(audit, "post", "dry_run", "mixu", scheduler.GetState(agentKey).LastAuditId);
    }

    [Test]
    public async Task AutonomyServiceDisabledGateBeatsPostPolicy()
    {
        DateTimeOffset now = new(2026, 7, 17, 12, 0, 0, TimeSpan.Zero);
        QZoneAutonomyAgentKey agentKey = QZoneAutonomyAgentKey.Create("mixu", 10001);
        AlwaysPostPersonaPolicy policy = new();
        AgentAuditLogService audit = CreateAutonomyAudit();
        QZoneService service = QZoneService.CreateForAutonomy(
            new FakeQZoneRuntime(),
            CreateDueAutonomyScheduler(now, agentKey),
            policy,
            audit,
            () => now);
        service.Configuration = new QZoneServiceConfig
        {
            EnableQZone = false,
            EnableQZoneAutonomy = true,
            DryRunExternalActions = true
        };

        QZoneAutonomyRunResult result = await service.RunAutonomyOnceAsync(MixuAutonomyRequest());

        Assert.Multiple(() =>
        {
            Assert.That(result.Executed, Is.False);
            Assert.That(result.ReasonCode, Is.EqualTo("disabled"));
            Assert.That(policy.EvaluateCalls, Is.Zero);
        });
        AssertSingleSafeAutonomyAudit(audit, "skip", "disabled", "mixu");
    }

    [Test]
    public async Task AutonomyServicePausedGateBeatsPostPolicy()
    {
        DateTimeOffset now = new(2026, 7, 17, 12, 0, 0, TimeSpan.Zero);
        QZoneAutonomyAgentKey agentKey = QZoneAutonomyAgentKey.Create("mixu", 10001);
        AlwaysPostPersonaPolicy policy = new();
        AgentAuditLogService audit = CreateAutonomyAudit();
        QZoneService service = QZoneService.CreateForAutonomy(
            new FakeQZoneRuntime(),
            CreateDueAutonomyScheduler(now, agentKey),
            policy,
            audit,
            () => now);
        service.Configuration = new QZoneServiceConfig
        {
            EnableQZone = true,
            EnableQZoneAutonomy = true,
            QZoneAutonomyPaused = true,
            DryRunExternalActions = true
        };

        QZoneAutonomyRunResult result = await service.RunAutonomyOnceAsync(MixuAutonomyRequest());

        Assert.Multiple(() =>
        {
            Assert.That(result.Executed, Is.False);
            Assert.That(result.ReasonCode, Is.EqualTo("paused"));
            Assert.That(policy.EvaluateCalls, Is.Zero);
        });
        AssertSingleSafeAutonomyAudit(audit, "skip", "paused", "mixu");
    }

    [Test]
    public async Task LiveAutonomyRequiresQZoneAutonomyDryRunOnlyToBeDisabled()
    {
        DateTimeOffset now = new(2026, 7, 17, 12, 0, 0, TimeSpan.Zero);
        QZoneAutonomyAgentKey agentKey = QZoneAutonomyAgentKey.Create("mixu", 10001);
        QZoneAutonomyScheduler scheduler = CreateDueAutonomyScheduler(now, agentKey);
        QZoneAutonomyState before = scheduler.GetState(agentKey);
        AlwaysPostPersonaPolicy policy = new();
        FakeQZoneRuntime runtime = new();
        AgentAuditLogService audit = CreateAutonomyAudit();
        QZoneService service = QZoneService.CreateForAutonomy(runtime, scheduler, policy, audit, () => now);
        service.Configuration = new QZoneServiceConfig
        {
            EnableQZone = true,
            EnableQZoneAutonomy = true,
            DryRunExternalActions = false
        };

        QZoneAutonomyRunResult result = await service.RunAutonomyOnceAsync(MixuAutonomyRequest());
        QZoneAutonomyState after = scheduler.GetState(agentKey);

        Assert.Multiple(() =>
        {
            Assert.That(result.Executed, Is.False);
            Assert.That(result.ReasonCode, Is.EqualTo("dry_run_disabled"));
            Assert.That(policy.EvaluateCalls, Is.Zero);
            Assert.That(after.LastSuccessfulPostAt, Is.EqualTo(before.LastSuccessfulPostAt));
            Assert.That(after.NextPostCandidateAt, Is.EqualTo(before.NextPostCandidateAt));
            Assert.That(after.LastAuditId, Is.EqualTo(before.LastAuditId));
            Assert.That(after.LastFailureKind, Is.EqualTo(before.LastFailureKind));
            Assert.That(after.CooldownUntil, Is.EqualTo(before.CooldownUntil));
            Assert.That(runtime.Posts, Is.Empty);
            Assert.That(runtime.Comments, Is.Empty);
            Assert.That(runtime.Replies, Is.Empty);
            Assert.That(runtime.Likes, Is.Empty);
        });
        AssertSingleSafeAutonomyAudit(audit, "skip", "dry_run_disabled", "mixu");
    }

    [Test]
    public async Task LiveAutonomyDisabled_DoesNotGenerateDraftOrCallRuntime()
    {
        DateTimeOffset now = new(2026, 7, 17, 12, 0, 0, TimeSpan.Zero);
        QZoneAutonomyAgentKey agentKey = QZoneAutonomyAgentKey.Create("mixu", 10001);
        FixedDraftGenerator generator = new("draft must not be used");
        FakeQZoneRuntime runtime = new();
        QZoneService service = QZoneService.CreateForAutonomy(
            runtime,
            CreateDueAutonomyScheduler(now, agentKey),
            new QZoneAutonomyPersonaPolicy(),
            generator,
            clock: () => now);
        service.Configuration = new QZoneServiceConfig
        {
            EnableQZone = true,
            EnableQZoneAutonomy = true,
            QZoneAutonomyDryRunOnly = false,
            DryRunExternalActions = false,
            EnableQZoneAutonomyLivePosting = false
        };

        QZoneAutonomyRunResult result = await service.RunAutonomyOnceAsync(MixuAutonomyRequest());

        Assert.Multiple(() =>
        {
            Assert.That(result.Executed, Is.False);
            Assert.That(result.ReasonCode, Is.EqualTo("live_autonomy_disabled"));
            Assert.That(generator.Calls, Is.Zero);
            Assert.That(runtime.Posts, Is.Empty);
        });
    }

    [Test]
    public async Task LiveAutonomyEnabled_GeneratesOneNormalizedDraftAndPublishesOnce()
    {
        DateTimeOffset now = new(2026, 7, 17, 12, 0, 0, TimeSpan.Zero);
        QZoneAutonomyAgentKey agentKey = QZoneAutonomyAgentKey.Create("mixu", 10001);
        QZoneAutonomyScheduler scheduler = CreateDueAutonomyScheduler(now, agentKey);
        FixedDraftGenerator generator = new("  晚风轻轻，今天也值得微笑。  ");
        FakeQZoneRuntime runtime = new();
        QZoneService service = QZoneService.CreateForAutonomy(
            runtime,
            scheduler,
            new QZoneAutonomyPersonaPolicy(),
            generator,
            clock: () => now);
        service.Configuration = new QZoneServiceConfig
        {
            EnableQZone = true,
            EnableQZoneAutonomy = true,
            QZoneAutonomyDryRunOnly = false,
            DryRunExternalActions = false,
            EnableQZoneAutonomyLivePosting = true
        };

        QZoneAutonomyRunResult result = await service.RunAutonomyOnceAsync(MixuAutonomyRequest());
        QZoneAutonomyState state = scheduler.GetState(agentKey);

        Assert.Multiple(() =>
        {
            Assert.That(result.Executed, Is.True);
            Assert.That(result.ReasonCode, Is.EqualTo("live_published"));
            Assert.That(generator.Calls, Is.EqualTo(1));
            Assert.That(runtime.Posts, Is.EqualTo(new[] { "晚风轻轻，今天也值得微笑。" }));
            Assert.That(state.LastSuccessfulPostAt, Is.EqualTo(now));
            Assert.That(state.PostsToday, Is.EqualTo(1));
            Assert.That(state.CooldownUntil, Is.Null);
            Assert.That(state.LastFailureKind, Is.Null);
        });
    }

    [TestCase("draft")]
    [TestCase("publish")]
    public async Task LiveAutonomyFailure_RecordsSafeReasonCooldownWithoutDraftText(string failureStage)
    {
        DateTimeOffset now = new(2026, 7, 17, 12, 0, 0, TimeSpan.Zero);
        string stateDirectory = CreateTemporaryDirectory();
        QZoneAutonomyAgentKey agentKey = QZoneAutonomyAgentKey.Create("mixu", 10001);
        QZoneAutonomyStateStore stateStore = new(stateDirectory);
        QZoneAutonomyScheduler scheduler = new(() => now, () => 0d, stateStore);
        scheduler.RecordPostSucceeded(agentKey, now.AddHours(-24).AddMinutes(-1));
        const string draftText = "private draft text must never persist";
        const string failureText = "private failure detail must never persist";
        FixedDraftGenerator generator = failureStage == "draft"
            ? new FixedDraftGenerator(new InvalidOperationException(failureText))
            : new FixedDraftGenerator(draftText);
        FakeQZoneRuntime runtime = new()
        {
            PublishException = failureStage == "publish"
                ? new InvalidOperationException(failureText)
                : null
        };
        QZoneService service = QZoneService.CreateForAutonomy(
            runtime,
            scheduler,
            new QZoneAutonomyPersonaPolicy(),
            generator,
            clock: () => now);
        service.Configuration = new QZoneServiceConfig
        {
            EnableQZone = true,
            EnableQZoneAutonomy = true,
            QZoneAutonomyDryRunOnly = false,
            DryRunExternalActions = false,
            EnableQZoneAutonomyLivePosting = true
        };

        QZoneAutonomyRunResult result = await service.RunAutonomyOnceAsync(MixuAutonomyRequest());
        QZoneAutonomyState state = scheduler.GetState(agentKey);
        string persistedJson = File.ReadAllText(Directory.GetFiles(stateDirectory, "*.json").Single());
        string expectedReason = failureStage == "draft" ? "draft_failed" : "publish_failed";

        Assert.Multiple(() =>
        {
            Assert.That(result.Executed, Is.False);
            Assert.That(result.ReasonCode, Is.EqualTo(expectedReason));
            Assert.That(state.LastFailureKind, Is.EqualTo(expectedReason));
            Assert.That(state.CooldownUntil, Is.EqualTo(now.AddMinutes(30)));
            Assert.That(state.ContentHashes, Is.Empty);
            Assert.That(persistedJson, Does.Not.Contain(draftText));
            Assert.That(persistedJson, Does.Not.Contain(failureText));
            Assert.That(persistedJson, Does.Not.Contain("Cookie").IgnoreCase);
            Assert.That(runtime.Posts, Has.Count.EqualTo(failureStage == "draft" ? 0 : 0));
        });
    }

    [Test]
    public async Task AutonomyServiceFailsClosedWhenPostingPolicyHasNoEnvelope()
    {
        DateTimeOffset now = new(2026, 7, 17, 12, 0, 0, TimeSpan.Zero);
        QZoneAutonomyAgentKey agentKey = QZoneAutonomyAgentKey.Create("mixu", 10001);
        QZoneAutonomyScheduler scheduler = CreateDueAutonomyScheduler(now, agentKey);
        QZoneAutonomyState before = scheduler.GetState(agentKey);
        AgentAuditLogService audit = CreateAutonomyAudit();
        QZoneService service = QZoneService.CreateForAutonomy(
            new FakeQZoneRuntime(),
            scheduler,
            new PostWithoutEnvelopePersonaPolicy(),
            audit,
            () => now);
        service.Configuration = new QZoneServiceConfig
        {
            EnableQZone = true,
            EnableQZoneAutonomy = true,
            DryRunExternalActions = true
        };

        QZoneAutonomyRunResult result = await service.RunAutonomyOnceAsync(MixuAutonomyRequest());
        QZoneAutonomyState after = scheduler.GetState(agentKey);

        Assert.Multiple(() =>
        {
            Assert.That(result.Executed, Is.False);
            Assert.That(result.ReasonCode, Is.EqualTo("persona_content_envelope_unavailable"));
            Assert.That(after.LastAuditId, Is.EqualTo(before.LastAuditId));
            Assert.That(after.LastFailureKind, Is.EqualTo(before.LastFailureKind));
            Assert.That(after.CooldownUntil, Is.EqualTo(before.CooldownUntil));
        });
        AssertSingleSafeAutonomyAudit(audit, "skip", "persona_content_envelope_unavailable", "mixu");
    }

    [Test]
    public void QZoneServiceRetainsLegacyEightParameterConstructorAndProvidesAutonomyFactory()
    {
        Type[] legacyParameterTypes =
        [
            typeof(IQZoneRuntime),
            typeof(IOneBotActionInvoker),
            typeof(IOneBotActionConnection),
            typeof(XmlFunctionCaller),
            typeof(ILifeEventPublisher),
            typeof(AgentProactiveBehaviorService),
            typeof(AgentAuditLogService),
            typeof(Func<DateTimeOffset>)
        ];

        ConstructorInfo? legacyConstructor = typeof(QZoneService).GetConstructor(legacyParameterTypes);

        Assert.Multiple(() =>
        {
            Assert.That(legacyConstructor, Is.Not.Null);
            Assert.That(new QZoneService(new FakeQZoneRuntime(), null, null, null, null, null, null, () => DateTimeOffset.UtcNow), Is.Not.Null);
            Assert.That(QZoneService.CreateForAutonomy(
                new FakeQZoneRuntime(),
                new QZoneAutonomyScheduler(() => DateTimeOffset.UtcNow, () => 0d),
                new QZoneAutonomyPersonaPolicy()),
                Is.Not.Null);
        });
    }

    [Test]
    public void AutonomyModelsRetainLegacyConstructorsAndDeconstructors()
    {
        ConstructorInfo? contextConstructor = typeof(QZoneAutonomyContext).GetConstructor(
        [
            typeof(QZoneAutonomyAgentKey),
            typeof(QZoneAutonomySettings),
            typeof(bool),
            typeof(bool)
        ]);
        ConstructorInfo? decisionConstructor = typeof(QZoneAutonomyDecision).GetConstructor(
        [
            typeof(QZoneAutonomyAction),
            typeof(QZoneAutonomyReasonCode)
        ]);
        MethodInfo? contextDeconstruct = typeof(QZoneAutonomyContext).GetMethods()
            .SingleOrDefault(method => method.Name == "Deconstruct" && method.GetParameters().Length == 4);
        MethodInfo? decisionDeconstruct = typeof(QZoneAutonomyDecision).GetMethods()
            .SingleOrDefault(method => method.Name == "Deconstruct" && method.GetParameters().Length == 2);

        Assert.Multiple(() =>
        {
            Assert.That(contextConstructor, Is.Not.Null);
            Assert.That(decisionConstructor, Is.Not.Null);
            Assert.That(contextDeconstruct?.GetParameters().All(parameter => parameter.IsOut), Is.True);
            Assert.That(decisionDeconstruct?.GetParameters().All(parameter => parameter.IsOut), Is.True);
        });
    }

    [Test]
    public async Task AutonomyServiceLazilySeedsPersistentDefaultSchedulerThenReachesDryRun()
    {
        DateTimeOffset now = new(2026, 7, 17, 12, 0, 0, TimeSpan.Zero);
        string stateDirectory = CreateTemporaryDirectory();
        QZoneAutonomyStateStore stateStore = new(stateDirectory);
        AlwaysPostPersonaPolicy policy = new();
        FakeQZoneRuntime runtime = new();
        AgentAuditLogService audit = CreateAutonomyAudit();
        QZoneService service = QZoneService.CreateForAutonomyWithStateStore(
            runtime,
            policy,
            audit,
            () => now,
            stateStore,
            () => 0d);
        service.Configuration = new QZoneServiceConfig
        {
            EnableQZone = true,
            EnableQZoneAutonomy = true,
            DryRunExternalActions = true
        };

        QZoneAutonomyRunResult first = await service.RunAutonomyOnceAsync(MixuAutonomyRequest());
        QZoneAutonomyAgentKey agentKey = QZoneAutonomyAgentKey.Create("mixu", 10001);
        QZoneAutonomyState initial = new QZoneAutonomyScheduler(() => now, () => 0d, stateStore).GetState(agentKey);
        now = initial.NextPostCandidateAt!.Value;
        QZoneAutonomyRunResult second = await service.RunAutonomyOnceAsync(MixuAutonomyRequest());

        Assert.Multiple(() =>
        {
            Assert.That(first.Executed, Is.False);
            Assert.That(first.ReasonCode, Is.EqualTo("initial_scheduled"));
            Assert.That(policy.EvaluateCalls, Is.EqualTo(1));
            Assert.That(second.Executed, Is.False);
            Assert.That(second.ReasonCode, Is.EqualTo("dry_run"));
            Assert.That(runtime.Posts, Is.Empty);
            Assert.That(runtime.Comments, Is.Empty);
            Assert.That(runtime.Replies, Is.Empty);
            Assert.That(runtime.Likes, Is.Empty);
        });
        Assert.That(audit.GetRecentEntries(10), Has.Count.EqualTo(2));
    }

    [Test]
    public async Task AutonomyServiceDisabledGateDoesNotCreateDefaultStateStore()
    {
        string stateDirectory = CreateTemporaryDirectory();
        AlwaysPostPersonaPolicy policy = new();
        QZoneService service = QZoneService.CreateForAutonomyWithStateStore(
            new FakeQZoneRuntime(),
            policy,
            null,
            () => DateTimeOffset.UtcNow,
            new QZoneAutonomyStateStore(stateDirectory),
            () => 0d);
        service.Configuration = new QZoneServiceConfig
        {
            EnableQZone = false,
            EnableQZoneAutonomy = true,
            DryRunExternalActions = true
        };

        QZoneAutonomyRunResult result = await service.RunAutonomyOnceAsync(MixuAutonomyRequest());

        Assert.Multiple(() =>
        {
            Assert.That(result.ReasonCode, Is.EqualTo("disabled"));
            Assert.That(policy.EvaluateCalls, Is.Zero);
            Assert.That(Directory.Exists(stateDirectory), Is.False);
        });
    }

    [TestCase(false, true, "disabled")]
    [TestCase(true, false, "autonomy_disabled")]
    public async Task AutonomyServicePausedGateCancelsPersistedCandidateBeforeEarlyDisabledReturn(
        bool enableQZone,
        bool enableAutonomy,
        string expectedReason)
    {
        DateTimeOffset now = new(2026, 7, 17, 12, 0, 0, TimeSpan.Zero);
        string stateDirectory = CreateTemporaryDirectory();
        QZoneAutonomyStateStore stateStore = new(stateDirectory);
        QZoneAutonomyAgentKey agentKey = QZoneAutonomyAgentKey.Create("mixu", 10001);
        stateStore.Save(QZoneAutonomyState.Create(agentKey) with
        {
            DailyCountDate = DateOnly.FromDateTime(now.DateTime),
            NextPostCandidateAt = now.AddMinutes(-1)
        });
        AlwaysPostPersonaPolicy policy = new();
        FakeQZoneRuntime runtime = new();
        QZoneService pausedService = QZoneService.CreateForAutonomyWithStateStore(
            runtime,
            policy,
            null,
            () => now,
            stateStore,
            () => 0d);
        pausedService.Configuration = new QZoneServiceConfig
        {
            EnableQZone = enableQZone,
            EnableQZoneAutonomy = enableAutonomy,
            QZoneAutonomyPaused = true,
            DryRunExternalActions = true
        };

        QZoneAutonomyRunResult pausedResult = await pausedService.RunAutonomyOnceAsync(MixuAutonomyRequest());
        QZoneAutonomyState cancelled = new QZoneAutonomyScheduler(() => now, () => 0d, stateStore).GetState(agentKey);
        QZoneService resumedService = QZoneService.CreateForAutonomyWithStateStore(
            runtime,
            policy,
            null,
            () => now,
            stateStore,
            () => 0d);
        resumedService.Configuration = new QZoneServiceConfig
        {
            EnableQZone = true,
            EnableQZoneAutonomy = true,
            QZoneAutonomyPaused = false,
            DryRunExternalActions = true
        };

        QZoneAutonomyRunResult resumedResult = await resumedService.RunAutonomyOnceAsync(MixuAutonomyRequest());

        Assert.Multiple(() =>
        {
            Assert.That(pausedResult.Executed, Is.False);
            Assert.That(pausedResult.ReasonCode, Is.EqualTo(expectedReason));
            Assert.That(cancelled.NextPostCandidateAt, Is.Null);
            Assert.That(resumedResult.Executed, Is.False);
            Assert.That(resumedResult.ReasonCode, Is.EqualTo("initial_scheduled"));
            Assert.That(policy.EvaluateCalls, Is.Zero);
            Assert.That(runtime.Posts, Is.Empty);
            Assert.That(runtime.Comments, Is.Empty);
            Assert.That(runtime.Replies, Is.Empty);
            Assert.That(runtime.Likes, Is.Empty);
        });
    }

    [Test]
    public async Task AutonomyServicePausedDisabledGateWithoutCandidateDoesNotCreateStateStore()
    {
        string stateDirectory = CreateTemporaryDirectory();
        AlwaysPostPersonaPolicy policy = new();
        FakeQZoneRuntime runtime = new();
        QZoneService service = QZoneService.CreateForAutonomyWithStateStore(
            runtime,
            policy,
            null,
            () => DateTimeOffset.UtcNow,
            new QZoneAutonomyStateStore(stateDirectory),
            () => 0d);
        service.Configuration = new QZoneServiceConfig
        {
            EnableQZone = false,
            EnableQZoneAutonomy = true,
            QZoneAutonomyPaused = true,
            DryRunExternalActions = true
        };

        QZoneAutonomyRunResult result = await service.RunAutonomyOnceAsync(MixuAutonomyRequest());

        Assert.Multiple(() =>
        {
            Assert.That(result.Executed, Is.False);
            Assert.That(result.ReasonCode, Is.EqualTo("disabled"));
            Assert.That(policy.EvaluateCalls, Is.Zero);
            Assert.That(Directory.Exists(stateDirectory), Is.False);
            Assert.That(runtime.Posts, Is.Empty);
            Assert.That(runtime.Comments, Is.Empty);
            Assert.That(runtime.Replies, Is.Empty);
            Assert.That(runtime.Likes, Is.Empty);
        });
    }

    [Test]
    public async Task QZoneComment_CallsRuntimeForAllowedTarget()
    {
        FakeQZoneRuntime runtime = new();
        QZoneService service = new(runtime)
        {
            Configuration = new QZoneServiceConfig
            {
                EnableQZone = true,
                AllowedQZoneTargetIds = "1001",
                DryRunExternalActions = false
            }
        };

        QZoneActionResult result = await service.QZoneComment(1001, "post-a", "nice");

        Assert.That(result.Executed, Is.True);
        Assert.That(runtime.Comments, Is.EqualTo(new[] { (1001L, "post-a", "nice") }));
    }

    [Test]
    public async Task QZoneLike_AllowsTargetsOutsidePrivateChatContactPool()
    {
        FakeQZoneRuntime runtime = new();
        QZoneService service = new(runtime)
        {
            Configuration = new QZoneServiceConfig
            {
                EnableQZone = true,
                PrivateChatContactIds = "1001",
                PrivateContactLikeProbability = 1.0,
                DryRunExternalActions = false
            }
        };

        QZoneActionResult result = await service.QZoneLike(2001, "post-a", () => 0.0);

        Assert.That(result.Executed, Is.True);
        Assert.That(runtime.Likes, Is.EqualTo(new[] { (2001L, "post-a") }));
    }

    [Test]
    public async Task QZoneLike_SkipsSameTargetDuringConfiguredCooldown()
    {
        DateTimeOffset now = DateTimeOffset.Parse("2026-06-14T12:00:00Z");
        FakeQZoneRuntime runtime = new();
        QZoneService service = new(runtime, clock: () => now)
        {
            Configuration = new QZoneServiceConfig
            {
                EnableQZone = true,
                PrivateChatContactIds = "1001",
                PrivateContactLikeProbability = 1.0,
                QZoneTargetCooldownMinutes = 30,
                DryRunExternalActions = false
            }
        };

        QZoneActionResult first = await service.QZoneLike(1001, "post-a", () => 0.0);
        QZoneActionResult second = await service.QZoneLike(1001, "post-b", () => 0.0);

        Assert.That(first.Executed, Is.True);
        Assert.That(second.Executed, Is.False);
        Assert.That(second.Reason, Does.Contain("cooldown"));
        Assert.That(runtime.Likes, Is.EqualTo(new[] { (1001L, "post-a") }));
    }

    [Test]
    public async Task QZoneComment_SkipsSameTargetAfterDailyLimit()
    {
        DateTimeOffset now = DateTimeOffset.Parse("2026-06-14T12:00:00Z");
        FakeQZoneRuntime runtime = new();
        QZoneService service = new(runtime, clock: () => now)
        {
            Configuration = new QZoneServiceConfig
            {
                EnableQZone = true,
                MaxQZoneInteractionsPerTargetPerDay = 1,
                DryRunExternalActions = false
            }
        };

        QZoneActionResult first = await service.QZoneComment(1001, "post-a", "nice");
        QZoneActionResult second = await service.QZoneComment(1001, "post-b", "thanks");

        Assert.That(first.Executed, Is.True);
        Assert.That(second.Executed, Is.False);
        Assert.That(second.Reason, Does.Contain("daily limit"));
        Assert.That(runtime.Comments, Is.EqualTo(new[] { (1001L, "post-a", "nice") }));
    }

    [Test]
    public async Task QZoneReplyComment_CallsRuntimeWhenMostlyReplyPolicyAllows()
    {
        FakeQZoneRuntime runtime = new();
        QZoneService service = new(runtime)
        {
            Configuration = new QZoneServiceConfig
            {
                EnableQZone = true,
                CommentReplyProbability = 0.8,
                DryRunExternalActions = false
            }
        };

        QZoneActionResult result = await service.QZoneReplyComment(1001, "post-a", "comment-b", "thanks", () => 0.5);

        Assert.That(result.Executed, Is.True);
        Assert.That(runtime.Replies, Is.EqualTo(new[] { (1001L, "post-a", "comment-b", "thanks") }));
    }

    [Test]
    public async Task QZoneComment_UsesInjectedOneBotActionInvokerWhenRuntimeIsAbsent()
    {
        FakeActionInvoker invoker = new();
        QZoneService service = new(actionInvoker: invoker)
        {
            Configuration = new QZoneServiceConfig
            {
                EnableQZone = true,
                DryRunExternalActions = false
            }
        };

        QZoneActionResult result = await service.QZoneComment(1001, "post-a", "nice");

        Assert.That(result.Executed, Is.True);
        Assert.That(invoker.Calls, Has.Count.EqualTo(1));
        Assert.That(invoker.Calls[0].Action, Is.EqualTo("send_comment"));
        Assert.That(invoker.Calls[0].Json, Does.Contain("\"target_uin\":1001"));
        Assert.That(invoker.Calls[0].Json, Does.Contain("\"target_tid\":\"post-a\""));
    }

    [Test]
    public async Task ConnectAsync_ConfiguresAndConnectsInjectedActionConnection()
    {
        FakeActionConnection connection = new();
        QZoneService service = new(actionConnection: connection)
        {
            Configuration = new QZoneServiceConfig
            {
                EnableQZone = true,
                DryRunExternalActions = false,
                Url = "ws://127.0.0.1:3010",
                Token = "secret"
            }
        };

        await service.ConnectAsync();

        Assert.That(connection.Url, Is.EqualTo("ws://127.0.0.1:3010"));
        Assert.That(connection.Token, Is.EqualTo("secret"));
        Assert.That(connection.ConnectCalls, Is.EqualTo(1));
        Assert.That(connection.IsConnected, Is.True);
        Assert.That(service.GetHealth().Status, Is.EqualTo(ModuleHealthStatus.Healthy));
    }

    [Test]
    public async Task ConnectAsync_UsesNapCatHttpRuntimeOnlyWhenExplicitlyConfigured()
    {
        NapCatActionConnection connection = new();
        RecordingQZoneHandler handler = new();
        HttpClient client = new(handler, disposeHandler: false);
        HttpClientQZoneService service = new(connection, client)
        {
            Configuration = new QZoneServiceConfig
            {
                EnableQZone = true,
                DryRunExternalActions = false,
                UseNapCatQZoneHttpRuntime = true,
                Url = "ws://127.0.0.1:3010",
                Token = "secret"
            }
        };

        await service.ConnectAsync();

        Assert.Multiple(() =>
        {
            Assert.That(connection.ConnectCalls, Is.EqualTo(1));
            Assert.That(connection.IsConnected, Is.True);
            Assert.That(connection.Calls, Is.Empty);
            Assert.That(handler.Requests, Is.Empty);
        });

        QZoneActionResult result = await service.QZonePost("hello qzone");

        Assert.Multiple(() =>
        {
            Assert.That(result, Is.EqualTo(new QZoneActionResult("post", true, "published QQ Zone post")));
            Assert.That(connection.Calls.Select(call => call.Action), Is.EqualTo(new[] { "get_cookies" }));
            Assert.That(handler.Requests, Has.Count.EqualTo(1));
            Assert.That(handler.Requests[0].Uri.AbsoluteUri, Is.EqualTo(QZoneHttpRuntime.PublishUrl));
            Assert.That(handler.Requests[0].Cookie, Is.EqualTo("uin=o10001; p_uin=o10001; p_skey=session-value"));
        });
    }

    [Test]
    public async Task ConnectAsync_PreservesOneBotRuntimeWhenNapCatHttpRuntimeIsNotEnabled()
    {
        NapCatActionConnection connection = new();
        QZoneService service = new(actionConnection: connection)
        {
            Configuration = new QZoneServiceConfig
            {
                EnableQZone = true,
                DryRunExternalActions = false,
                UseNapCatQZoneHttpRuntime = false
            }
        };

        await service.ConnectAsync();
        QZoneActionResult result = await service.QZonePost("hello qzone");

        Assert.Multiple(() =>
        {
            Assert.That(result, Is.EqualTo(new QZoneActionResult("post", true, "published QQ Zone post")));
            Assert.That(connection.ConnectCalls, Is.EqualTo(1));
            Assert.That(connection.Calls.Select(call => call.Action), Is.EqualTo(new[] { "send_msg" }));
        });
    }

    [Test]
    public async Task QZoneLatestPostAndComments_ReadsLatestPostThenComments()
    {
        FakeQZoneRuntime runtime = new()
        {
            LatestPost = new QZonePostSnapshot("post-a", 1001, "latest post"),
            LatestComments =
            [
                new QZoneCommentSnapshot("comment-a", 2001, "first"),
                new QZoneCommentSnapshot("comment-b", 2002, "second")
            ]
        };
        QZoneService service = new(runtime)
        {
            Configuration = new QZoneServiceConfig
            {
                EnableQZone = true,
                AllowedQZoneTargetIds = "1001"
            }
        };

        QZoneQueryResult result = await service.QZoneLatestPostAndComments(1001, 2);

        Assert.That(result.Succeeded, Is.True);
        Assert.That(result.Post?.PostId, Is.EqualTo("post-a"));
        Assert.That(result.Comments.Select(comment => comment.CommentId), Is.EqualTo(new[] { "comment-a", "comment-b" }));
        Assert.That(runtime.LatestPostRequests, Is.EqualTo(new[] { 1001L }));
        Assert.That(runtime.LatestCommentRequests, Is.EqualTo(new[] { (1001L, "post-a", 2) }));
        Assert.That(runtime.Comments, Is.Empty);
        Assert.That(runtime.Likes, Is.Empty);
    }

    [Test]
    public async Task QZoneReportFeedbackDoesNotPokeInternalQqZoneLabel()
    {
        FakeQZoneRuntime runtime = new();
        QZoneService service = new(runtime)
        {
            Configuration = new QZoneServiceConfig
            {
                EnableQZone = true,
                PrivateChatContactIds = "1001",
                PrivateContactLikeProbability = 0.0
            }
        };
        StartService(service);

        QZoneActionResult result = await service.QZoneLike(1001, "post-a", () => 0.5);

        string pending = GetPendingPokeText(service);
        Assert.Multiple(() =>
        {
            Assert.That(result.Executed, Is.False);
            Assert.That(pending, Does.Not.Contain("[QQ Zone"));
            Assert.That(pending, Does.Not.Contain("qzone-"));
            Assert.That(pending, Does.Contain("\u72b6\u6001\u5982\u4e0b\u3002"));
            Assert.That(pending, Does.Contain("QQ\u7a7a\u95f4\u64cd\u4f5c\u6ca1\u6709\u5b8c\u6210\u3002"));
            Assert.That(pending, Does.Not.Contain("skipped by random like probability policy"));
        });
    }

    [Test]
    public async Task QZoneProactiveFeedbackDoesNotPokeInternalQqZoneLabel()
    {
        QZoneService service = new(new FakeQZoneRuntime());
        StartService(service);

        QZoneProactiveExecutionResult result = await service.ExecuteConfirmedProactiveSuggestion("missing-id");

        string pending = GetPendingPokeText(service);
        Assert.Multiple(() =>
        {
            Assert.That(result.Succeeded, Is.False);
            Assert.That(pending, Does.Not.Contain("[QQ Zone"));
            Assert.That(pending, Does.Not.Contain("rejected"));
            Assert.That(pending, Does.Contain("QZone proactive action was not executed"));
            Assert.That(pending, Does.Contain("Proactive behavior service is unavailable"));
        });
    }

    sealed class FakeQZoneRuntime : IQZoneRuntime
    {
        public Exception? PublishException { get; init; }
        public Exception? DeleteException { get; init; }
        public List<string> Posts { get; } = new();
        public List<QZoneImageUpload> ImageUploads { get; } = new();
        public List<(string Content, IReadOnlyList<QZoneUploadedImage> Images)> ImagePosts { get; } = new();
        public List<string> ImageOperations { get; } = new();
        public List<(long TargetId, string PostId, string Content)> Comments { get; } = new();
        public List<(long TargetId, string PostId, string CommentId, string Content)> Replies { get; } = new();
        public List<(long TargetId, string PostId)> Likes { get; } = new();
        public List<QZonePostSnapshot> DeletedPosts { get; } = new();
        public List<long> LatestPostRequests { get; } = new();
        public List<(long TargetId, string PostId, int Count)> LatestCommentRequests { get; } = new();
        public QZonePostSnapshot? LatestPost { get; init; }
        public IReadOnlyList<QZoneCommentSnapshot> LatestComments { get; init; } = [];

        public Task PublishPost(string content)
        {
            if (PublishException != null)
                return Task.FromException(PublishException);

            Posts.Add(content);
            return Task.CompletedTask;
        }

        public Task<QZoneUploadedImage> UploadImage(QZoneImageUpload upload)
        {
            ImageUploads.Add(upload);
            ImageOperations.Add("upload");
            return Task.FromResult(new QZoneUploadedImage("album", "lloc", "sloc", 1, 1, 1, "https://photo.example.invalid/image.jpg?bo=bo"));
        }

        public Task PublishImagePost(string content, IReadOnlyList<QZoneUploadedImage> images)
        {
            ImagePosts.Add((content, images));
            ImageOperations.Add("publish");
            return Task.CompletedTask;
        }

        public Task Comment(long targetId, string postId, string content)
        {
            Comments.Add((targetId, postId, content));
            return Task.CompletedTask;
        }

        public Task ReplyComment(long targetId, string postId, string commentId, string content)
        {
            Replies.Add((targetId, postId, commentId, content));
            return Task.CompletedTask;
        }

        public Task LikePost(long targetId, string postId)
        {
            Likes.Add((targetId, postId));
            return Task.CompletedTask;
        }

        public Task DeletePost(QZonePostSnapshot post)
        {
            if (DeleteException != null)
                return Task.FromException(DeleteException);

            DeletedPosts.Add(post);
            return Task.CompletedTask;
        }

        public Task<QZonePostSnapshot?> GetLatestPost(long targetId)
        {
            LatestPostRequests.Add(targetId);
            return Task.FromResult(LatestPost);
        }

        public Task<IReadOnlyList<QZoneCommentSnapshot>> GetLatestComments(long targetId, string postId, int count)
        {
            LatestCommentRequests.Add((targetId, postId, count));
            return Task.FromResult(LatestComments);
        }
    }

    sealed class RecordingImageHandler : HttpMessageHandler
    {
        public List<Uri> Requests { get; } = [];

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Requests.Add(request.RequestUri!);
            ByteArrayContent content = new([9, 8, 7]);
            content.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue("image/jpeg");
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = content });
        }
    }

    sealed class AlwaysPostPersonaPolicy : IQZoneAutonomyPersonaPolicy
    {
        public int EvaluateCalls { get; private set; }

        public QZoneAutonomyDecision Evaluate(QZoneAutonomyContext context)
        {
            EvaluateCalls++;
            return new QZoneAutonomyDecision(
                QZoneAutonomyAction.Post,
                QZoneAutonomyReasonCode.Due,
                QZoneAutonomyContentEnvelope.MixuWarmBright);
        }
    }

    sealed class PostWithoutEnvelopePersonaPolicy : IQZoneAutonomyPersonaPolicy
    {
        public QZoneAutonomyDecision Evaluate(QZoneAutonomyContext context) =>
            new(QZoneAutonomyAction.Post, QZoneAutonomyReasonCode.Due);
    }

    sealed class FixedDraftGenerator : IQZoneDraftGenerator
    {
        readonly string? draft;
        readonly Exception? exception;

        public FixedDraftGenerator(string draft)
        {
            this.draft = draft;
        }

        public FixedDraftGenerator(Exception exception)
        {
            this.exception = exception;
        }

        public int Calls { get; private set; }

        public Task<string> GenerateAsync(QZoneDraftRequest request, CancellationToken ct = default)
        {
            Calls++;
            return exception is null
                ? Task.FromResult(draft!)
                : Task.FromException<string>(exception);
        }
    }

    sealed class FakeActionInvoker : IOneBotActionInvoker
    {
        public List<(string Action, string Json)> Calls { get; } = new();

        public Task<T?> CallActionAsync<T>(string action, object? parameters = null)
        {
            Calls.Add((action, JsonSerializer.Serialize(parameters)));
            return Task.FromResult<T?>(default);
        }
    }

    sealed class FakeActionConnection : IOneBotActionConnection
    {
        public bool IsConnected { get; private set; }
        public string Url { get; set; } = "";
        public string Token { get; set; } = "";
        public int ConnectCalls { get; private set; }

        public Task ConnectAsync()
        {
            ConnectCalls++;
            IsConnected = true;
            return Task.CompletedTask;
        }

        public Task<T?> CallActionAsync<T>(string action, object? parameters = null)
        {
            return Task.FromResult<T?>(default);
        }

        public ValueTask DisposeAsync()
        {
            return ValueTask.CompletedTask;
        }
    }

    sealed class HttpClientQZoneService(IOneBotActionConnection actionConnection, HttpClient client)
        : QZoneService(actionConnection: actionConnection)
    {
        protected override HttpClient CreateQZoneHttpClient() => client;
    }

    sealed class NapCatActionConnection : IOneBotActionConnection
    {
        public bool IsConnected { get; private set; }
        public string Url { get; set; } = "";
        public string Token { get; set; } = "";
        public int ConnectCalls { get; private set; }
        public List<(string Action, string Json)> Calls { get; } = [];

        public Task ConnectAsync()
        {
            ConnectCalls++;
            IsConnected = true;
            return Task.CompletedTask;
        }

        public Task<T?> CallActionAsync<T>(string action, object? parameters = null)
        {
            Calls.Add((action, JsonSerializer.Serialize(parameters)));
            object? response = action == "get_cookies"
                ? new NapCatQZoneCookieResponse("uin=o10001; p_uin=o10001; p_skey=session-value", "701234")
                : null;
            return Task.FromResult((T?)response);
        }

        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }

    sealed class RecordingQZoneHandler : HttpMessageHandler
    {
        public List<(Uri Uri, string Cookie)> Requests { get; } = [];

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Requests.Add((request.RequestUri!, request.Headers.GetValues("Cookie").Single()));
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent("{\"code\":0}")
            });
        }
    }

    static void StartService(QZoneService service)
    {
        Character character = new() { Name = "QZoneTest" };
        ChatHistoryAgentThread thread = new();
        service.AwakeAsync(new AwakeContext
        {
            Character = character,
            ContextBuilder = thread,
            KernelBuilder = Kernel.CreateBuilder(),
        }).GetAwaiter().GetResult();
        ChatBot chatBot = new(null!, thread);
        service.StartAsync(Kernel.CreateBuilder().Build(), new ChatActivity(
            character,
            Kernel.CreateBuilder().Build(),
            null!,
            chatBot,
            [])).GetAwaiter().GetResult();
    }

    static QZoneAutonomyScheduler CreateDueAutonomyScheduler(DateTimeOffset now, QZoneAutonomyAgentKey agentKey)
    {
        QZoneAutonomyScheduler scheduler = new(() => now, () => 0d);
        scheduler.RecordPostSucceeded(agentKey, now.AddHours(-24).AddMinutes(-1));
        return scheduler;
    }

    static QZoneAutonomyRunRequest MixuAutonomyRequest() =>
        new(
            "mixu",
            10001,
            new QZoneAutonomyPersonaSignals(
                QZoneAutonomyPersona.Mixu,
                new QZoneAutonomyXiaYuSignals(),
                new QZoneAutonomyMixuSignals(IsRelationshipSafe: true, PrefersWarmBright: true)));

    AgentAuditLogService CreateAutonomyAudit() =>
        new(Path.Combine(CreateTemporaryDirectory(), "audit.jsonl"));

    static void AssertSingleSafeAutonomyAudit(
        AgentAuditLogService audit,
        string action,
        string reason,
        string persona,
        string? expectedCorrelation = null)
    {
        AgentAuditLogEntry entry = audit.GetRecentEntries(10).Single();
        string correlation = entry.Detail
            .Split("; ", StringSplitOptions.None)[0]
            .Split('=', 2)[1];

        Assert.Multiple(() =>
        {
            Assert.That(entry.Action, Is.EqualTo("qzone.autonomy.run"));
            Assert.That(entry.Actor, Is.EqualTo("agent"));
            Assert.That(Guid.TryParseExact(correlation, "D", out _), Is.True);
            Assert.That(entry.Detail, Is.EqualTo($"correlation={correlation}; action={action}; reason={reason}; persona={persona}"));
            Assert.That(expectedCorrelation is null || correlation == expectedCorrelation, Is.True);
            Assert.That(entry.Detail, Does.Not.Contain("10001"));
            Assert.That(entry.Detail, Does.Not.Contain("ordinary safe"));
            Assert.That(entry.Detail, Does.Not.Contain("cookie"));
            Assert.That(entry.Detail, Does.Not.Contain("prompt"));
            Assert.That(entry.Detail, Does.Not.Contain("draft"));
        });
    }

    string CreateTemporaryDirectory()
    {
        string directory = Path.Combine(Path.GetTempPath(), "Alife.QZoneService.Tests", Guid.NewGuid().ToString("N"));
        directoriesToDelete.Add(directory);
        return directory;
    }

    static string GetPendingPokeText(QZoneService service)
    {
        PropertyInfo chatBotProperty = typeof(InteractiveModule)
            .GetProperty("ChatBot", BindingFlags.Instance | BindingFlags.NonPublic)!;
        ChatBot chatBot = (ChatBot)chatBotProperty.GetValue(service)!;
        FieldInfo messageCacheField = typeof(ChatBot)
            .GetField("messageCache", BindingFlags.Instance | BindingFlags.NonPublic)!;
        IEnumerable<string> messages = (IEnumerable<string>)messageCacheField.GetValue(chatBot)!;
        return string.Join("", messages);
    }
}
