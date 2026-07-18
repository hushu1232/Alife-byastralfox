# NapCat Real QZone Integration Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Enable both local NapCat characters to read and operate their real QQ Spaces, publish role-aware Grok drafts, and upload permitted images without persisting QQ Space sessions.

**Architecture:** `NapCatQZoneSessionProvider` obtains the current account's QZone cookie and `bkn` from OneBot's `get_cookies` action. `QZoneHttpRuntime` uses that short-lived session to call QZone Web endpoints and maps responses into expanded QZone models. `QZoneService` selects this runtime in real mode, resolves image bytes, formats role-specific feedback, and lets the existing randomized scheduler publish a generated candidate only when its live-autonomy switch is enabled.

**Tech Stack:** .NET 9, C#, `HttpClient`, `System.Text.Json`, existing OneBot WebSocket client, Semantic Kernel `IChatCompletionService`, NUnit.

---

## Planned file structure

- Create: `sources/Alife.Function/Alife.Function.QChat/NapCatQZoneSessionProvider.cs` — OneBot `get_cookies` contract and in-memory session parser.
- Create: `sources/Alife.Function/Alife.Function.QChat/QZoneHttpRuntime.cs` — QZone HTTP requests, JSONP parsing and response mapping.
- Create: `sources/Alife.Function/Alife.Function.QChat/QZoneImageSourceResolver.cs` — owner-supplied local/byte/URL image resolution.
- Create: `sources/Alife.Function/Alife.Function.QChat/QZoneDraftGenerator.cs` — Semantic Kernel adapter for candidate text.
- Create: `sources/Alife.Function/Alife.Function.QChat/QZoneFeedbackFormatter.cs` — role-specific QZone outcome text.
- Modify: `sources/Alife.Function/Alife.Function.QChat/QZoneService.cs` — runtime selection, new operations, image and live-autonomy wiring.
- Modify: `sources/Alife.Function/Alife.Function.QChat/QZoneInteractionPolicy.cs` — remove the duplicate private-contact target requirement.
- Modify: `sources/Alife.Function/Alife.Function.QChat/QZoneAutonomyModels.cs` and `QZoneAutonomyScheduler.cs` — explicit live-publish setting and success/failure state transition.
- Modify: `sources/Alife.Function/Alife.Function.QChat/OneBotQZoneRuntime.cs` — retain the legacy adapter but implement the expanded interface as unsupported, so it cannot impersonate a real QZone runtime.
- Modify: `Tests/Alife.Test.QChat/QZoneServiceTests.cs` and `Tests/Alife.Test.QChat/OneBotQZoneRuntimeTests.cs` — preserve legacy constructor and dry-run behavior.
- Create: `Tests/Alife.Test.QChat/NapCatQZoneSessionProviderTests.cs`, `QZoneHttpRuntimeTests.cs`, `QZoneImageSourceResolverTests.cs`, `QZoneDraftGeneratorTests.cs`, `QZoneFeedbackFormatterTests.cs` — focused contracts only.
- Modify: `docs/qzone-boundary.md` — record the deployed runtime, real-mode configuration, image origins and future URL-whitelist scope.
- Create: `tools/local-production/Test-QZoneRealRuntime.ps1` — explicit operator command that invokes read-only and selected write smoke actions; it contains no credentials.

### Task 1: Define real QZone models and obtain the NapCat session

**Files:**

- Modify: `sources/Alife.Function/Alife.Function.QChat/QZoneService.cs:14-31`
- Create: `sources/Alife.Function/Alife.Function.QChat/NapCatQZoneSessionProvider.cs`
- Create: `Tests/Alife.Test.QChat/NapCatQZoneSessionProviderTests.cs`

- [ ] **Step 1: Write the failing OneBot session-provider tests**

```csharp
[Test]
public async Task GetSessionAsync_UsesNapCatGetCookiesAndParsesCurrentAccount()
{
    FakeActionInvoker invoker = new();
    invoker.Enqueue(new NapCatQZoneCookieResponse(
        "uin=o10001; p_uin=o10001; p_skey=session-value;", "701234"));
    NapCatQZoneSessionProvider provider = new(invoker);

    QZoneSession session = await provider.GetSessionAsync();

    Assert.That(invoker.Calls.Single().Action, Is.EqualTo("get_cookies"));
    Assert.That(invoker.Calls.Single().Json, Is.EqualTo("{\"domain\":\"qzone.qq.com\"}"));
    Assert.That(session.AccountId, Is.EqualTo(10001));
    Assert.That(session.Bkn, Is.EqualTo("701234"));
}

[TestCase("", "701234", "qzone_cookie_unavailable")]
[TestCase("uin=o10001", "", "qzone_bkn_unavailable")]
public void GetSessionAsync_RejectsIncompleteNapCatResponse(string cookies, string bkn, string reason)
{
    FakeActionInvoker invoker = new();
    invoker.Enqueue(new NapCatQZoneCookieResponse(cookies, bkn));

    QZoneSessionUnavailableException exception = Assert.ThrowsAsync<QZoneSessionUnavailableException>(
        async () => await new NapCatQZoneSessionProvider(invoker).GetSessionAsync())!;

    Assert.That(exception.Message, Is.EqualTo(reason));
    Assert.That(exception.Message, Does.Not.Contain(cookies));
}
```

- [ ] **Step 2: Run the session-provider test to verify it fails**

Run:

```powershell
& 'C:\Users\hu shu\.dotnet\dotnet.exe' test Tests\Alife.Test.QChat\Alife.Test.QChat.csproj --no-restore --filter 'FullyQualifiedName~NapCatQZoneSessionProviderTests' -v:minimal
```

Expected: compilation failure because `NapCatQZoneSessionProvider`, `QZoneSession`, and `NapCatQZoneCookieResponse` do not exist.

- [ ] **Step 3: Add the session contract and minimal provider**

```csharp
public sealed record NapCatQZoneCookieResponse(string Cookies, string Bkn);
public sealed record QZoneSession(long AccountId, string Cookies, string Bkn);
public interface IQZoneSessionProvider
{
    Task<QZoneSession> GetSessionAsync(CancellationToken cancellationToken = default);
}

public sealed class NapCatQZoneSessionProvider(IOneBotActionInvoker invoker) : IQZoneSessionProvider
{
    public async Task<QZoneSession> GetSessionAsync(CancellationToken cancellationToken = default)
    {
        NapCatQZoneCookieResponse? response = await invoker.CallActionAsync<NapCatQZoneCookieResponse>(
            "get_cookies", new { domain = "qzone.qq.com" });
        string cookies = response?.Cookies?.Trim() ?? "";
        string bkn = response?.Bkn?.Trim() ?? "";
        if (cookies.Length == 0) throw new QZoneSessionUnavailableException("qzone_cookie_unavailable");
        if (bkn.Length == 0) throw new QZoneSessionUnavailableException("qzone_bkn_unavailable");
        return new QZoneSession(ParseAccountId(cookies), cookies, bkn);
    }
}
```

`ParseAccountId` must accept either `p_uin=o123` or `uin=o123`, reject zero/missing values with `qzone_account_unavailable`, and never include Cookie text in the exception.

At the top of `QZoneService.cs`, extend the models without breaking current positional construction:

```csharp
public sealed record QZonePostSnapshot(string PostId, long TargetId, string Content,
    string? TopicId = null, string? FeedsKey = null, long? CreatedAtUnixSeconds = null);
public sealed record QZoneCommentSnapshot(string CommentId, long UserId, string Content,
    string? TopicId = null, string? ParentCommentId = null);
public sealed record QZoneUploadedImage(string AlbumId, string Lloc, string Sloc,
    int Width, int Height, int Type, string Url);
```

- [ ] **Step 4: Run the focused test to verify it passes**

Run the command from Step 2.

Expected: 3 passing tests; no Cookie string in test output.

- [ ] **Step 5: Commit the session boundary**

```powershell
git add sources/Alife.Function/Alife.Function.QChat/NapCatQZoneSessionProvider.cs sources/Alife.Function/Alife.Function.QChat/QZoneService.cs Tests/Alife.Test.QChat/NapCatQZoneSessionProviderTests.cs
git commit -m 'feat(qzone): obtain ephemeral NapCat sessions'
```

### Task 2: Implement real QZone HTTP read and text interaction calls

**Files:**

- Create: `sources/Alife.Function/Alife.Function.QChat/QZoneHttpRuntime.cs`
- Modify: `sources/Alife.Function/Alife.Function.QChat/QZoneService.cs:14-31`
- Create: `Tests/Alife.Test.QChat/QZoneHttpRuntimeTests.cs`

- [ ] **Step 1: Write the failing HTTP contract tests**

```csharp
[Test]
public async Task GetLatestPost_UsesEphemeralSessionAndParsesJsonpFeed()
{
    RecordingHandler handler = new("_Callback({\"code\":0,\"data\":{\"msglist\":[{\"tid\":\"t1\",\"uin\":10001,\"content\":\"hello\",\"created_time\":42}]}});");
    QZoneHttpRuntime runtime = new(new FixedSessionProvider(10001), new HttpClient(handler));

    QZonePostSnapshot? post = await runtime.GetLatestPost(10001);

    Assert.That(post, Is.EqualTo(new QZonePostSnapshot("t1", 10001, "hello", null, null, 42)));
    Assert.That(handler.Request!.RequestUri!.AbsoluteUri, Does.Contain("emotion_cgi_msglist_v6"));
    Assert.That(handler.Request.Headers.GetValues("Cookie").Single(), Does.Contain("p_skey=session-value"));
    Assert.That(handler.Request.RequestUri.Query, Does.Contain("g_tk=701234"));
}

[Test]
public async Task PublishCommentReplyLikeAndDelete_SendQZoneFormFields()
{
    RecordingHandler handler = new("{\"code\":0}");
    QZoneHttpRuntime runtime = new(new FixedSessionProvider(10001), new HttpClient(handler));

    await runtime.PublishPost("text");
    await runtime.Comment(20002, "tid", "comment");
    await runtime.ReplyComment(20002, "tid", "cid", "reply");
    await runtime.LikePost(20002, "tid");
    await runtime.DeletePost(new QZonePostSnapshot("tid", 10001, "text", "10001_tid__1", "tid", 42));

    Assert.That(handler.RequestBodies[0], Does.Contain("con=text"));
    Assert.That(handler.RequestBodies[1], Does.Contain("content=comment"));
    Assert.That(handler.RequestBodies[2], Does.Contain("commentId=cid"));
    Assert.That(handler.RequestBodies[3], Does.Contain("unikey=20002_tid"));
    Assert.That(handler.RequestBodies[4], Does.Contain("feedsKey=tid"));
}
```

- [ ] **Step 2: Run the HTTP contract tests to verify they fail**

Run:

```powershell
& 'C:\Users\hu shu\.dotnet\dotnet.exe' test Tests\Alife.Test.QChat\Alife.Test.QChat.csproj --no-restore --filter 'FullyQualifiedName~QZoneHttpRuntimeTests' -v:minimal
```

Expected: compilation failure because `QZoneHttpRuntime` and the expanded runtime methods do not exist.

- [ ] **Step 3: Expand `IQZoneRuntime` and implement `QZoneHttpRuntime`**

Add these methods to `IQZoneRuntime`:

```csharp
Task<QZoneUploadedImage> UploadImage(QZoneImageUpload upload);
Task PublishImagePost(string content, IReadOnlyList<QZoneUploadedImage> images);
Task DeletePost(QZonePostSnapshot post);
Task DeleteComment(long targetId, string postId, string commentId);
Task DeleteReply(long targetId, string postId, string commentId, string replyId);
```

Use one `GetSessionAsync` call per runtime operation. Create a new request per call, apply the session Cookie header and `g_tk=session.Bkn`, and post `FormUrlEncodedContent` for text operations. The runtime must expose these fixed endpoint constants:

```csharp
const string FeedListUrl = "https://user.qzone.qq.com/proxy/domain/taotao.qq.com/cgi-bin/emotion_cgi_msglist_v6";
const string PublishUrl = "https://user.qzone.qq.com/proxy/domain/taotao.qzone.qq.com/cgi-bin/emotion_cgi_publish_v6";
const string CommentUrl = "https://user.qzone.qq.com/proxy/domain/taotao.qzone.qq.com/cgi-bin/emotion_cgi_re_feeds";
const string ReplyUrl = "https://user.qzone.qq.com/proxy/domain/taotao.qzone.qq.com/cgi-bin/emotion_cgi_addreply_ugc";
const string LikeUrl = "https://user.qzone.qq.com/proxy/domain/w.qzone.qq.com/cgi-bin/likes/internal_dolike_app";
const string DeleteUrl = "https://user.qzone.qq.com/proxy/domain/taotao.qzone.qq.com/cgi-bin/emotion_cgi_delete_v6";
const string UploadUrl = "https://up.qzone.qq.com/cgi-bin/upload/cgi_upload_image";
```

The required form values are `hostuin/session.AccountId`, `con`, `feedversion=1`, `ver=1`, `ugc_right=1`, `format=json`, and `qzreferrer` for posts; `hostUin`, `topicId=$"{targetId}_{postId}"`, `commentUin=session.AccountId`, and `content` for comments/replies; and `opuin=session.AccountId`, `unikey=$"{targetId}_{postId}"`, `curkey`, `appid=311`, `format=json` for likes. `DeletePost` must throw `qzone_delete_metadata_unavailable` unless the post is owned by `session.AccountId` and has `TopicId`, `FeedsKey`, and `CreatedAtUnixSeconds`.

`ParseJsonOrJsonp` must strip text before the first `(` and after the final `)` only when the body is callback-wrapped, then parse JSON. A non-zero `code` or a non-success HTTP status must throw `QZoneHttpException` containing only `qzone_http_<status>` or `qzone_api_<code>`.

- [ ] **Step 4: Run the HTTP contract tests to verify they pass**

Run the command from Step 2.

Expected: 2 passing tests and no network request, because `RecordingHandler` captures every request.

- [ ] **Step 5: Commit the real text runtime**

```powershell
git add sources/Alife.Function/Alife.Function.QChat/QZoneHttpRuntime.cs sources/Alife.Function/Alife.Function.QChat/QZoneService.cs Tests/Alife.Test.QChat/QZoneHttpRuntimeTests.cs
git commit -m 'feat(qzone): add real HTTP text runtime'
```

### Task 3: Add image-source resolution, image upload, and graph-free image posts

**Files:**

- Create: `sources/Alife.Function/Alife.Function.QChat/QZoneImageSourceResolver.cs`
- Modify: `sources/Alife.Function/Alife.Function.QChat/QZoneHttpRuntime.cs`
- Modify: `sources/Alife.Function/Alife.Function.QChat/QZoneService.cs`
- Create: `Tests/Alife.Test.QChat/QZoneImageSourceResolverTests.cs`
- Modify: `Tests/Alife.Test.QChat/QZoneHttpRuntimeTests.cs`

- [ ] **Step 1: Write the failing image source and upload tests**

```csharp
[Test]
public async Task ResolveAsync_UsesLocalGeneratedImageBytesWithoutKeepingASecondCopy()
{
    string path = Path.Combine(TestContext.CurrentContext.WorkDirectory, "generated.png");
    await File.WriteAllBytesAsync(path, [1, 2, 3]);
    QZoneImageSourceResolver resolver = new(new HttpClient(new ThrowingHandler()));

    QZoneImageUpload upload = await resolver.ResolveAsync(
        QZoneImageSource.GeneratedFile(path, "generated.png"));

    Assert.That(upload.Bytes, Is.EqualTo(new byte[] { 1, 2, 3 }));
    Assert.That(upload.Origin, Is.EqualTo(QZoneImageOrigin.Generated));
}

[Test]
public async Task ResolveAsync_DownloadsAnExplicitOwnerUrl()
{
    QZoneImageSourceResolver resolver = new(new HttpClient(new BytesHandler([9, 8, 7], "image/jpeg")));

    QZoneImageUpload upload = await resolver.ResolveAsync(
        QZoneImageSource.OwnerUrl(new Uri("https://example.invalid/image.jpg")));

    Assert.That(upload.Bytes, Is.EqualTo(new byte[] { 9, 8, 7 }));
    Assert.That(upload.ContentType, Is.EqualTo("image/jpeg"));
    Assert.That(upload.Origin, Is.EqualTo(QZoneImageOrigin.OwnerProvided));
}

[Test]
public async Task UploadImage_UsesQZoneBase64FormAndReturnsPublicationMetadata()
{
    RecordingHandler handler = new("{\"code\":0,\"data\":{\"albumid\":\"a\",\"lloc\":\"l\",\"sloc\":\"s\",\"width\":3,\"height\":2,\"type\":1,\"url\":\"https://image\"}}");
    QZoneHttpRuntime runtime = new(new FixedSessionProvider(10001), new HttpClient(handler));

    QZoneUploadedImage image = await runtime.UploadImage(new QZoneImageUpload("x.jpg", "image/jpeg", [1,2,3], QZoneImageOrigin.OwnerProvided));

    Assert.That(handler.RequestBodies.Single(), Does.Contain("picfile=AQID"));
    Assert.That(image.AlbumId, Is.EqualTo("a"));
}
```

- [ ] **Step 2: Run the image tests to verify they fail**

Run:

```powershell
& 'C:\Users\hu shu\.dotnet\dotnet.exe' test Tests\Alife.Test.QChat\Alife.Test.QChat.csproj --no-restore --filter 'FullyQualifiedName~QZoneImageSourceResolverTests|FullyQualifiedName~QZoneHttpRuntimeTests' -v:minimal
```

Expected: compilation failure because `QZoneImageSource`, `QZoneImageUpload`, and the resolver do not exist.

- [ ] **Step 3: Implement image source resolution and HTTP multipart-equivalent QZone form upload**

Define the public data model:

```csharp
public enum QZoneImageOrigin { OwnerProvided, Generated }
public sealed record QZoneImageUpload(string FileName, string ContentType, byte[] Bytes, QZoneImageOrigin Origin);
public abstract record QZoneImageSource
{
    public static QZoneImageSource OwnerFile(string path, string? fileName = null) => new QZoneLocalImageSource(path, fileName, QZoneImageOrigin.OwnerProvided);
    public static QZoneImageSource GeneratedFile(string path, string? fileName = null) => new QZoneLocalImageSource(path, fileName, QZoneImageOrigin.Generated);
    public static QZoneImageSource OwnerUrl(Uri url) => new QZoneRemoteImageSource(url);
}
```

`QZoneImageSourceResolver.ResolveAsync` must accept only existing local files or absolute HTTP(S) URLs; it reads the bytes once, rejects an empty image and payloads larger than `QZoneServiceConfig.MaxQZoneImageBytes`, and returns `qzone_image_source_unavailable`, `qzone_image_source_invalid`, or `qzone_image_too_large` without echoing the full local path/URL. It must not search the web or persist a duplicate download.

`QZoneHttpRuntime.UploadImage` sends the QZone-required base64 form values `uploadtype=1`, `albumtype=7`, `skey`, `p_skey`, `uin`, `p_uin`, `p_skey`, `refer=shuoshuo`, `output_type=json`, `base64=1`, and `picfile`. It uses the session Cookie but never logs the `skey`/`p_skey` values. `PublishImagePost` forms `richtype=1`, tab-joined `richval` (`albumid,lloc,sloc,type,height,width`) and `pic_bo` values built from the returned URLs.

Add `[XmlFunction] QZonePostImage(string content, string sourceKind, string sourceValue)` to `QZoneService`: map only `owner_file`, `generated_file`, and `owner_url` to the data model, resolve the source, upload once, then call `PublishImagePost`. In dry-run return `dry-run: would publish QQ Zone image post` before resolving/downloading a source.

- [ ] **Step 4: Run the image tests to verify they pass**

Run the command from Step 2.

Expected: all image-source and HTTP image tests pass without external network access.

- [ ] **Step 5: Commit image support**

```powershell
git add sources/Alife.Function/Alife.Function.QChat/QZoneImageSourceResolver.cs sources/Alife.Function/Alife.Function.QChat/QZoneHttpRuntime.cs sources/Alife.Function/Alife.Function.QChat/QZoneService.cs Tests/Alife.Test.QChat/QZoneImageSourceResolverTests.cs Tests/Alife.Test.QChat/QZoneHttpRuntimeTests.cs
git commit -m 'feat(qzone): upload sourced images to real space'
```

### Task 4: Select the real runtime, remove redundant target gates, and format QZone feedback by role

**Files:**

- Modify: `sources/Alife.Function/Alife.Function.QChat/QZoneService.cs`
- Modify: `sources/Alife.Function/Alife.Function.QChat/QZoneInteractionPolicy.cs`
- Modify: `sources/Alife.Function/Alife.Function.QChat/OneBotQZoneRuntime.cs`
- Create: `sources/Alife.Function/Alife.Function.QChat/QZoneFeedbackFormatter.cs`
- Modify: `Tests/Alife.Test.QChat/QZoneServiceTests.cs`
- Modify: `Tests/Alife.Test.QChat/OneBotQZoneRuntimeTests.cs`
- Create: `Tests/Alife.Test.QChat/QZoneFeedbackFormatterTests.cs`

- [ ] **Step 1: Write the failing integration and formatter tests**

```csharp
[Test]
public async Task ConnectAsync_UsesNapCatSessionRuntimeWhenConfiguredForRealQZone()
{
    FakeActionConnection connection = new();
    QZoneService service = new(actionConnection: connection) {
        Configuration = new QZoneServiceConfig {
            EnableQZone = true, DryRunExternalActions = false,
            UseNapCatQZoneHttpRuntime = true, AutoConnect = true
        }
    };

    await service.ConnectAsync();

    Assert.That(connection.Calls, Is.Empty);
    Assert.That(connection.ConnectCount, Is.EqualTo(1));
}

[Test]
public void Format_FailedQZoneActionUsesMixuPersonality()
{
    string text = QZoneFeedbackFormatter.Format("mixu", false, "qzone_api_100");
    Assert.That(text, Does.Contain("咪绪"));
    Assert.That(text, Does.Not.Contain("qzone_api_100"));
}

[Test]
public void ShouldLikeTarget_AllowsAccessibleTargetWithoutDuplicatePrivateContactList()
{
    Assert.That(QZoneInteractionPolicy.ShouldLikeTarget(new QZoneInteractionConfig(), 999, () => 0.01), Is.True);
}
```

- [ ] **Step 2: Run the integration tests to verify they fail**

Run:

```powershell
& 'C:\Users\hu shu\.dotnet\dotnet.exe' test Tests\Alife.Test.QChat\Alife.Test.QChat.csproj --no-restore --filter 'FullyQualifiedName~QZoneServiceTests|FullyQualifiedName~OneBotQZoneRuntimeTests|FullyQualifiedName~QZoneFeedbackFormatterTests|FullyQualifiedName~QZoneInteractionPolicyTests' -v:minimal
```

Expected: failures for missing `UseNapCatQZoneHttpRuntime` and `QZoneFeedbackFormatter`; existing legacy tests continue to compile before the interface expansion is completed.

- [ ] **Step 3: Wire the runtime and feedback policy**

Add these configuration fields:

```csharp
public bool UseNapCatQZoneHttpRuntime { get; set; }
public string PersonaId { get; set; } = "";
public int MaxQZoneImageBytes { get; set; } = 8 * 1024 * 1024;
public int MaxQZoneImagesPerPost { get; set; } = 9;
```

When `UseNapCatQZoneHttpRuntime` is true, `GetRuntime` and `ConnectAsync` must construct `QZoneHttpRuntime(new NapCatQZoneSessionProvider(connection), httpClient)` after the existing OneBot connection is connected. Retain the public eight-argument constructor and existing injected-runtime seams. The legacy `OneBotQZoneRuntime` must throw `NotSupportedException("onebot_qzone_runtime_not_real")` for the new image/delete members instead of mapping them to invented OneBot action names.

Change `QZoneInteractionPolicy.ShouldLikeTarget` so it only checks `EnableQZone`, an optional non-empty explicit target list, and the configured probability. Remove `IsPrivateChatContact` from `QZoneService.QZoneLike`; a blank `AllowedQZoneTargetIds` remains unconstrained. Do not add a replacement target whitelist.

`QZoneFeedbackFormatter.Format(personaId, succeeded, safeReason)` must map only generic safe reason groups (`published`, `commented`, `liked`, `deleted`, `qzone_api_*`, `qzone_http_*`, `qzone_session_*`) to Chinese outcome text, then pass it through `QChatPersonaFeedback.Prefix(new QChatPersonaFeedbackContext(personaId, QChatSenderRole.Owner), text)`. It must never expose Cookie, URL, token, raw exception text or QZone API codes.

Make `Report`, `ReportQuery`, and image/delete result paths call this formatter with `Configuration.PersonaId`. Keep existing direct `QZoneActionResult.Reason` values stable for programmatic callers; format only user-facing `functionService`/life-event output.

- [ ] **Step 4: Run the integration suite to verify it passes**

Run the command from Step 2.

Expected: existing legacy compatibility tests plus the three added behavior tests pass.

- [ ] **Step 5: Commit service integration**

```powershell
git add sources/Alife.Function/Alife.Function.QChat/QZoneService.cs sources/Alife.Function/Alife.Function.QChat/QZoneInteractionPolicy.cs sources/Alife.Function/Alife.Function.QChat/OneBotQZoneRuntime.cs sources/Alife.Function/Alife.Function.QChat/QZoneFeedbackFormatter.cs Tests/Alife.Test.QChat/QZoneServiceTests.cs Tests/Alife.Test.QChat/OneBotQZoneRuntimeTests.cs Tests/Alife.Test.QChat/QZoneFeedbackFormatterTests.cs
git commit -m 'feat(qzone): wire NapCat real runtime by persona'
```

### Task 5: Generate role-aware drafts and enable deliberate live autonomy

**Files:**

- Create: `sources/Alife.Function/Alife.Function.QChat/QZoneDraftGenerator.cs`
- Modify: `sources/Alife.Function/Alife.Function.QChat/QZoneService.cs:275-340`
- Modify: `sources/Alife.Function/Alife.Function.QChat/QZoneAutonomyModels.cs`
- Modify: `sources/Alife.Function/Alife.Function.QChat/QZoneAutonomyScheduler.cs`
- Create: `Tests/Alife.Test.QChat/QZoneDraftGeneratorTests.cs`
- Modify: `Tests/Alife.Test.QChat/QZoneServiceTests.cs`
- Modify: `Tests/Alife.Test.QChat/QZoneAutonomyModelsTests.cs`

- [ ] **Step 1: Write the failing draft and live-autonomy tests**

```csharp
[Test]
public async Task GenerateAsync_UsesRoleEnvelopeAndTrimsModelOutput()
{
    FakeChatCompletionService chat = new("  a quiet post  ");
    QZoneSemanticKernelDraftGenerator generator = new(chat);

    string draft = await generator.GenerateAsync(new QZoneDraftRequest(
        "xiayu", QZoneAutonomyContentEnvelope.XiaYuRestrained, Array.Empty<string>()));

    Assert.That(draft, Is.EqualTo("a quiet post"));
    Assert.That(chat.LastHistory!.ToString(), Does.Contain("maximum 120 characters"));
    Assert.That(chat.LastHistory!.ToString(), Does.Not.Contain("Cookie"));
}

[Test]
public async Task RunAutonomyOnceAsync_PublishesGeneratedDraftOnlyWhenLiveSwitchIsEnabled()
{
    FakeQZoneRuntime runtime = new();
    QZoneService service = QZoneService.CreateForAutonomy(
        runtime, DueNowScheduler(), AlwaysPostPolicy(), draftGenerator: new FixedDraftGenerator("hello"));
    service.Configuration = new QZoneServiceConfig {
        EnableQZone = true, EnableQZoneAutonomy = true,
        DryRunExternalActions = false, QZoneAutonomyDryRunOnly = false,
        EnableQZoneAutonomyLivePosting = true
    };

    QZoneAutonomyRunResult result = await service.RunAutonomyOnceAsync(MixuAutonomyRequest());

    Assert.That(result.Executed, Is.True);
    Assert.That(runtime.Posts, Is.EqualTo(new[] { "hello" }));
}
```

- [ ] **Step 2: Run draft/autonomy tests to verify they fail**

Run:

```powershell
& 'C:\Users\hu shu\.dotnet\dotnet.exe' test Tests\Alife.Test.QChat\Alife.Test.QChat.csproj --no-restore --filter 'FullyQualifiedName~QZoneDraftGeneratorTests|FullyQualifiedName~QZoneServiceTests|FullyQualifiedName~QZoneAutonomyModelsTests' -v:minimal
```

Expected: compilation failure because the draft-generator types, factory parameter, and live switch do not exist.

- [ ] **Step 3: Implement the generator and autonomous state transition**

Define:

```csharp
public sealed record QZoneDraftRequest(string PersonaId, QZoneAutonomyContentEnvelope Envelope, IReadOnlyList<string> RecentContentHashes);
public interface IQZoneDraftGenerator
{
    Task<string> GenerateAsync(QZoneDraftRequest request, CancellationToken cancellationToken = default);
}
public sealed class QZoneSemanticKernelDraftGenerator(IChatCompletionService service) : IQZoneDraftGenerator { }
```

The Semantic Kernel history has one system instruction and one user instruction: request a single natural QZone post, honor `Envelope.Topic`, `Envelope.Style`, `Envelope.MaximumLength`, return the text only, and do not mention tools, policies, memory, system prompts, or credentials. Trim surrounding whitespace and throw `qzone_draft_empty` when no text remains. `QZoneService.StartAsync` resolves `IChatCompletionService` from the supplied `Kernel` and supplies this generator only for the current character's service; production keys and model selection remain owned by the existing character Kernel configuration.

Add `EnableQZoneAutonomyLivePosting=false` to `QZoneServiceConfig`. In `RunAutonomyOnceAsync` preserve all disabled, paused, schedule and persona gates. After a due post envelope:

1. retain the existing dry-run record when `DryRunExternalActions` is true;
2. return `live_autonomy_disabled` when `EnableQZoneAutonomyLivePosting` is false;
3. require `QZoneAutonomyDryRunOnly=false` in the existing scheduler settings for a live call;
4. generate and normalize one draft, call `GetRuntime().PublishPost`, then call `scheduler.RecordPostSucceeded` and return `live_published`;
5. on model or HTTP failure call a new `RecordPostFailure` method with safe code `draft_failed` or `publish_failed`, a 30-minute cooldown, then return the safe reason without exception detail.

Add `draft_failed` and `publish_failed` to the persisted safe reason set and implement `RecordPostFailure` with the same state-store locking and persistence as `RecordPostSucceeded`. It must not save the draft text.

- [ ] **Step 4: Run draft/autonomy tests to verify they pass**

Run the command from Step 2.

Expected: the new live test posts exactly once; all old first-phase dry-run tests still pass with their existing configuration defaults.

- [ ] **Step 5: Commit role-aware live autonomy**

```powershell
git add sources/Alife.Function/Alife.Function.QChat/QZoneDraftGenerator.cs sources/Alife.Function/Alife.Function.QChat/QZoneService.cs sources/Alife.Function/Alife.Function.QChat/QZoneAutonomyModels.cs sources/Alife.Function/Alife.Function.QChat/QZoneAutonomyScheduler.cs Tests/Alife.Test.QChat/QZoneDraftGeneratorTests.cs Tests/Alife.Test.QChat/QZoneServiceTests.cs Tests/Alife.Test.QChat/QZoneAutonomyModelsTests.cs
git commit -m 'feat(qzone): publish persona drafts through live autonomy'
```

### Task 6: Document and perform the minimum real two-account verification

**Files:**

- Modify: `docs/qzone-boundary.md`
- Create: `tools/local-production/Test-QZoneRealRuntime.ps1`
- Modify: `Tests/Alife.Test.QChat/Alife.Test.QChat.csproj` only if a new source file needs explicit inclusion

- [ ] **Step 1: Write the local real-runtime operator script and its dry-run test mode**

```powershell
param(
    [ValidateSet('Read','Post','Comment','Like','Image')][string]$Operation = 'Read',
    [ValidateSet(3001,3002)][int]$Port,
    [switch]$Execute
)

if (-not $Execute) {
    [pscustomobject]@{ operation=$Operation; port=$Port; execute=$false; message='Add -Execute to call the selected real QZone operation.' } | ConvertTo-Json
    exit 0
}
```

The script reads the OneBot token only from `ALIFE_ACCOUNT_A_ONEBOT_TOKEN`/`ALIFE_ACCOUNT_B_ONEBOT_TOKEN`, never prints it, and starts no QQ/NapCat process. It sends its request to the already-running local service or explicit test endpoint; `Post`, `Comment`, `Like`, and `Image` require separate explicit `-Execute` invocation. The image operation takes a caller-selected local file path and refuses a missing path.

- [ ] **Step 2: Verify the default operator mode performs no external action**

Run:

```powershell
powershell -ExecutionPolicy Bypass -File tools\local-production\Test-QZoneRealRuntime.ps1 -Operation Read -Port 3001
```

Expected: JSON with `execute:false`; no WebSocket, HTTP or QZone call.

- [ ] **Step 3: Update the operator documentation**

In `docs/qzone-boundary.md`, document these exact deployment conditions:

```text
UseNapCatQZoneHttpRuntime=true
EnableQZone=true
DryRunExternalActions=false
EnableQZoneAutonomy=true                 # only for scheduled posts
QZoneAutonomyDryRunOnly=false             # only for scheduled live posts
EnableQZoneAutonomyLivePosting=true       # only after manual verification
```

Document that no QZone target whitelist is configured, owner-provided direct image URLs are accepted in this version, and a domain URL whitelist will be added only before future autonomous remote-image collection.

- [ ] **Step 4: Run focused automated verification**

Run:

```powershell
& 'C:\Users\hu shu\.dotnet\dotnet.exe' build Tests\Alife.Test.QChat\Alife.Test.QChat.csproj --no-restore -v:minimal
& 'C:\Users\hu shu\.dotnet\dotnet.exe' test Tests\Alife.Test.QChat\Alife.Test.QChat.csproj --no-restore --no-build --filter 'FullyQualifiedName~QZone' -v:minimal
```

Expected: build succeeds; all QZone tests pass.

- [ ] **Step 5: With the owner's live authorization, execute the shortest real check matrix**

Run each operation only once per account in this order: `Read`; a unique short text `Post`; `Comment` or `Like`; `Image` with one local test image. Confirm the post/comment/image appears in the intended account's QQ Space before enabling `EnableQZoneAutonomyLivePosting`.

Expected: each result identifies the correct role account and reports only persona-formatted success/failure text; terminal output never includes Cookie, BKN, OneBot token or API key.

- [ ] **Step 6: Commit documentation and operator script**

```powershell
git add docs/qzone-boundary.md tools/local-production/Test-QZoneRealRuntime.ps1
git commit -m 'docs(qzone): add real runtime operator guide'
```

## Plan self-review

- Spec coverage: Task 1 implements ephemeral NapCat sessions; Task 2 implements real reads and text interactions; Task 3 implements the three approved image origins, upload and graph-free image posts; Task 4 integrates the runtime, eliminates the duplicate target gate and adds persona feedback; Task 5 implements Grok/Semantic-Kernel candidate generation and live randomized publication; Task 6 documents and performs only the necessary two-account verification.
- URL whitelist: intentionally excluded from this plan. It becomes a separate task before any autonomous remote-image retrieval is added.
- Sensitive data: every task keeps Cookie/BKN/keys out of persistence, documentation examples, Git and status text.
- Type consistency: `QZoneSession`, `IQZoneSessionProvider`, `QZoneImageUpload`, `QZoneUploadedImage`, `IQZoneDraftGenerator`, and `EnableQZoneAutonomyLivePosting` are defined before use in later tasks.
- Completeness scan: every current-scope task includes a concrete implementation and verification step.
