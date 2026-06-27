using Alife.Function.Agent;
using NUnit.Framework;
using System.Text.Json;

namespace Alife.Test.Framework;

[TestFixture]
public sealed class AgentExternalRagServiceTests
{
    [Test]
    public async Task AddPublicUrlAsync_WhenOwnerAndFetchSucceeds_AddsFetchedContentAndAuditsSuccess()
    {
        string root = CreateStorageRoot();
        AgentExternalRagStore store = new(root);
        FakeAgentInternetService internet = new(new AgentInternetFetchResult(
            true,
            "ok",
            "Fetched rambutan content from the public page."));
        string auditPath = Path.Combine(root, "agent-audit.jsonl");
        AgentAuditLogService audit = new(auditPath);
        AgentExternalRagService service = new(store, internet, audit);

        AgentExternalRagSource source = await service.AddPublicUrlAsync(
            "https://example.com/rag",
            "RAG Page",
            addedByOwner: true);

        IReadOnlyList<AgentExternalRagChunk> chunks = store.Query("rambutan", maxChunks: 3);
        IReadOnlyList<AgentAuditLogEntry> auditEntries = audit.GetRecentEntries(1);

        Assert.Multiple(() =>
        {
            Assert.That(internet.FetchCount, Is.EqualTo(1));
            Assert.That(internet.LastUrl, Is.EqualTo("https://example.com/rag"));
            Assert.That(source.Url, Is.EqualTo("https://example.com/rag"));
            Assert.That(source.Title, Is.EqualTo("RAG Page"));
            Assert.That(chunks, Has.Count.EqualTo(1));
            Assert.That(chunks[0].Text, Does.Contain("Fetched rambutan content"));
            Assert.That(auditEntries, Has.Count.EqualTo(1));
            Assert.That(auditEntries[0].Action, Is.EqualTo("agent.external_rag.add"));
            Assert.That(auditEntries[0].Actor, Is.EqualTo("owner"));
            Assert.That(auditEntries[0].Detail, Is.EqualTo("https://example.com/rag"));
            Assert.That(auditEntries[0].RiskLevel, Is.EqualTo(AgentAuditRiskLevel.Medium));
            Assert.That(auditEntries[0].Succeeded, Is.True);
            Assert.That(auditEntries[0].Error, Is.Null);
        });
    }

    [Test]
    public void AddPublicUrlAsync_WhenFetchFails_DoesNotAddSourceAndAuditsFailure()
    {
        string root = CreateStorageRoot();
        AgentExternalRagStore store = new(root);
        FakeAgentInternetService internet = new(new AgentInternetFetchResult(
            false,
            "http_status_500",
            "internet_fetch_denied"));
        AgentAuditLogService audit = new(Path.Combine(root, "agent-audit.jsonl"));
        AgentExternalRagService service = new(store, internet, audit);

        InvalidOperationException ex = Assert.ThrowsAsync<InvalidOperationException>(() =>
            service.AddPublicUrlAsync(
                "https://example.com/failure",
                "Failure Page",
                addedByOwner: true))!;
        IReadOnlyList<AgentExternalRagChunk> chunks = store.Query("failure", maxChunks: 3);
        IReadOnlyList<AgentAuditLogEntry> auditEntries = audit.GetRecentEntries(1);

        Assert.Multiple(() =>
        {
            Assert.That(ex.Message, Is.EqualTo("external_rag_fetch_failed:http_status_500"));
            Assert.That(internet.FetchCount, Is.EqualTo(1));
            Assert.That(chunks, Is.Empty);
            Assert.That(auditEntries, Has.Count.EqualTo(1));
            Assert.That(auditEntries[0].Action, Is.EqualTo("agent.external_rag.add"));
            Assert.That(auditEntries[0].Actor, Is.EqualTo("owner"));
            Assert.That(auditEntries[0].Detail, Is.EqualTo("https://example.com/failure"));
            Assert.That(auditEntries[0].RiskLevel, Is.EqualTo(AgentAuditRiskLevel.Medium));
            Assert.That(auditEntries[0].Succeeded, Is.False);
            Assert.That(auditEntries[0].Error, Is.EqualTo("http_status_500"));
        });
    }

    [Test]
    public void AddPublicUrlAsync_RejectsNonOwnerBeforeFetching()
    {
        string root = CreateStorageRoot();
        AgentExternalRagStore store = new(root);
        FakeAgentInternetService internet = new(new AgentInternetFetchResult(
            true,
            "ok",
            "content"));
        AgentAuditLogService audit = new(Path.Combine(root, "agent-audit.jsonl"));
        AgentExternalRagService service = new(store, internet, audit);

        InvalidOperationException ex = Assert.ThrowsAsync<InvalidOperationException>(() =>
            service.AddPublicUrlAsync(
                "https://example.com/private",
                "Private Page",
                addedByOwner: false))!;
        IReadOnlyList<AgentAuditLogEntry> auditEntries = audit.GetRecentEntries(1);

        Assert.Multiple(() =>
        {
            Assert.That(ex.Message, Is.EqualTo("external_rag_owner_required"));
            Assert.That(internet.FetchCount, Is.EqualTo(0));
            Assert.That(auditEntries, Has.Count.EqualTo(1));
            Assert.That(auditEntries[0].Action, Is.EqualTo("agent.external_rag.add"));
            Assert.That(auditEntries[0].Actor, Is.EqualTo("non_owner"));
            Assert.That(auditEntries[0].Detail, Is.EqualTo("https://example.com/private"));
            Assert.That(auditEntries[0].RiskLevel, Is.EqualTo(AgentAuditRiskLevel.Medium));
            Assert.That(auditEntries[0].Succeeded, Is.False);
            Assert.That(auditEntries[0].Error, Is.EqualTo("external_rag_owner_required"));
        });
    }

    [Test]
    public void AddPublicUrlAsync_WhenUrlPolicyDenied_DoesNotFetchAndAuditsFailure()
    {
        string root = CreateStorageRoot();
        AgentExternalRagStore store = new(root);
        FakeAgentInternetService internet = new(new AgentInternetFetchResult(
            true,
            "ok",
            "permissive fake content"));
        AgentAuditLogService audit = new(Path.Combine(root, "agent-audit.jsonl"));
        AgentExternalRagService service = new(store, internet, audit);

        InvalidOperationException ex = Assert.ThrowsAsync<InvalidOperationException>(() =>
            service.AddPublicUrlAsync(
                "file:///C:/Windows/win.ini",
                "Local File",
                addedByOwner: true))!;
        IReadOnlyList<AgentExternalRagChunk> chunks = store.Query("permissive", maxChunks: 3);
        IReadOnlyList<AgentAuditLogEntry> auditEntries = audit.GetRecentEntries(1);

        Assert.Multiple(() =>
        {
            Assert.That(ex.Message, Does.StartWith("external_rag_url_denied:scheme_not_allowed"));
            Assert.That(internet.FetchCount, Is.EqualTo(0));
            Assert.That(chunks, Is.Empty);
            Assert.That(auditEntries, Has.Count.EqualTo(1));
            Assert.That(auditEntries[0].Action, Is.EqualTo("agent.external_rag.add"));
            Assert.That(auditEntries[0].Actor, Is.EqualTo("owner"));
            Assert.That(auditEntries[0].Detail, Is.EqualTo("file:///C:/Windows/win.ini"));
            Assert.That(auditEntries[0].RiskLevel, Is.EqualTo(AgentAuditRiskLevel.Medium));
            Assert.That(auditEntries[0].Succeeded, Is.False);
            Assert.That(auditEntries[0].Error, Is.EqualTo("scheme_not_allowed"));
        });
    }

    [Test]
    public void AddPublicUrlAsync_WhenCancellationRequestedAfterFetch_DoesNotPersistOrAuditSuccess()
    {
        string root = CreateStorageRoot();
        AgentExternalRagStore store = new(root);
        using CancellationTokenSource cts = new();
        FakeAgentInternetService internet = new(
            new AgentInternetFetchResult(
                true,
                "ok",
                "Fetched cancellable lychee content."),
            onFetch: () => cts.Cancel());
        AgentAuditLogService audit = new(Path.Combine(root, "agent-audit.jsonl"));
        AgentExternalRagService service = new(store, internet, audit);

        Assert.ThrowsAsync<OperationCanceledException>(() =>
            service.AddPublicUrlAsync(
                "https://example.com/cancel",
                "Cancel Page",
                addedByOwner: true,
                cancellationToken: cts.Token));
        IReadOnlyList<AgentExternalRagChunk> chunks = store.Query("lychee", maxChunks: 3);
        IReadOnlyList<AgentAuditLogEntry> auditEntries = audit.GetRecentEntries(10);

        Assert.Multiple(() =>
        {
            Assert.That(internet.FetchCount, Is.EqualTo(1));
            Assert.That(chunks, Is.Empty);
            Assert.That(auditEntries.Any(entry =>
                entry.Action == "agent.external_rag.add" && entry.Succeeded), Is.False);
        });
    }

    [Test]
    public void AddPublicUrlAsync_WhenStoreWriteFails_AuditsFailure()
    {
        string root = CreateStorageRoot();
        AgentExternalRagStore store = new(root);
        Directory.CreateDirectory(Path.Combine(root, "external-rag-sources.jsonl"));
        FakeAgentInternetService internet = new(new AgentInternetFetchResult(
            true,
            "ok",
            "Fetched durable guava content."));
        AgentAuditLogService audit = new(Path.Combine(root, "agent-audit.jsonl"));
        AgentExternalRagService service = new(store, internet, audit);

        Exception? ex = Assert.CatchAsync<Exception>(() =>
            service.AddPublicUrlAsync(
                "https://example.com/store-fail",
                "Store Fail Page",
                addedByOwner: true));
        IReadOnlyList<AgentAuditLogEntry> auditEntries = audit.GetRecentEntries(1);

        Assert.Multiple(() =>
        {
            Assert.That(ex, Is.Not.TypeOf<OperationCanceledException>());
            Assert.That(internet.FetchCount, Is.EqualTo(1));
            Assert.That(auditEntries, Has.Count.EqualTo(1));
            Assert.That(auditEntries[0].Action, Is.EqualTo("agent.external_rag.add"));
            Assert.That(auditEntries[0].Actor, Is.EqualTo("owner"));
            Assert.That(auditEntries[0].Detail, Is.EqualTo("https://example.com/store-fail"));
            Assert.That(auditEntries[0].RiskLevel, Is.EqualTo(AgentAuditRiskLevel.Medium));
            Assert.That(auditEntries[0].Succeeded, Is.False);
            Assert.That(auditEntries[0].Error, Is.EqualTo("external_rag_store_failed"));
        });
    }

    [Test]
    public void ListSources_ReturnsStoredSourcesWithoutFetching()
    {
        string root = CreateStorageRoot();
        AgentExternalRagStore store = new(root);
        FakeAgentInternetService internet = new(new AgentInternetFetchResult(true, "ok", "unused"));
        AgentExternalRagService service = new(store, internet);
        store.AddOrReplaceSource(
            "https://example.com/list",
            "List Source",
            "Listable source text.",
            addedByOwner: true);

        IReadOnlyList<AgentExternalRagSource> sources = service.ListSources(limit: 3);

        Assert.Multiple(() =>
        {
            Assert.That(sources, Has.Count.EqualTo(1));
            Assert.That(sources[0].Title, Is.EqualTo("List Source"));
            Assert.That(internet.Calls, Is.Zero);
        });
    }

    [Test]
    public void DeleteSource_WhenOwner_RemovesStoredSourceAndAuditsSuccess()
    {
        string root = CreateStorageRoot();
        AgentExternalRagStore store = new(root);
        FakeAgentInternetService internet = new(new AgentInternetFetchResult(true, "ok", "unused"));
        AgentAuditLogService audit = new(Path.Combine(root, "agent-audit.jsonl"));
        AgentExternalRagService service = new(store, internet, audit);
        store.AddOrReplaceSource(
            "https://example.com/delete-service",
            "Delete Service",
            "Delete service plum text.",
            addedByOwner: true);

        bool deleted = service.DeleteSource("https://example.com/delete-service", deletedByOwner: true);
        IReadOnlyList<AgentAuditLogEntry> auditEntries = audit.GetRecentEntries(1);

        Assert.Multiple(() =>
        {
            Assert.That(deleted, Is.True);
            Assert.That(store.Query("plum", maxChunks: 3), Is.Empty);
            Assert.That(auditEntries, Has.Count.EqualTo(1));
            Assert.That(auditEntries[0].Action, Is.EqualTo("agent.external_rag.delete"));
            Assert.That(auditEntries[0].Actor, Is.EqualTo("owner"));
            Assert.That(auditEntries[0].Succeeded, Is.True);
        });
    }

    [Test]
    public void DeleteSource_WhenNonOwner_DoesNotDeleteOrFetch()
    {
        string root = CreateStorageRoot();
        AgentExternalRagStore store = new(root);
        FakeAgentInternetService internet = new(new AgentInternetFetchResult(true, "ok", "unused"));
        AgentAuditLogService audit = new(Path.Combine(root, "agent-audit.jsonl"));
        AgentExternalRagService service = new(store, internet, audit);
        store.AddOrReplaceSource(
            "https://example.com/keep",
            "Keep",
            "Keep apricot text.",
            addedByOwner: true);

        InvalidOperationException ex = Assert.Throws<InvalidOperationException>(() =>
            service.DeleteSource("https://example.com/keep", deletedByOwner: false))!;
        IReadOnlyList<AgentAuditLogEntry> auditEntries = audit.GetRecentEntries(1);

        Assert.Multiple(() =>
        {
            Assert.That(ex.Message, Is.EqualTo("external_rag_owner_required"));
            Assert.That(internet.Calls, Is.Zero);
            Assert.That(store.Query("apricot", maxChunks: 3), Has.Count.EqualTo(1));
            Assert.That(auditEntries[0].Action, Is.EqualTo("agent.external_rag.delete"));
            Assert.That(auditEntries[0].Actor, Is.EqualTo("non_owner"));
            Assert.That(auditEntries[0].Succeeded, Is.False);
        });
    }

    [Test]
    public void Query_WhenNoMatch_ReturnsNoMatchWithoutFetching()
    {
        AgentExternalRagStore store = new(CreateStorageRoot());
        store.AddOrReplaceSource(
            "https://example.com/knowledge",
            "Knowledge Base",
            "Dragonfruit policy.",
            addedByOwner: true);
        FakeAgentInternetService internet = new(new AgentInternetFetchResult(
            true,
            "ok",
            "should not fetch"));
        AgentExternalRagService service = new(store, internet);

        AgentExternalRagQueryResponse response = service.Query("missing-term", maxChunks: 3);

        Assert.Multiple(() =>
        {
            Assert.That(internet.FetchCount, Is.EqualTo(0));
            Assert.That(response.Success, Is.False);
            Assert.That(response.Reason, Is.EqualTo("no_match"));
            Assert.That(response.Chunks, Is.Empty);
            Assert.That(response.FormattedContext, Is.EqualTo("external_rag=no_match"));
        });
    }

    [Test]
    public void Query_WhenMatch_ReturnsOkFormattedContextWithoutFetching()
    {
        AgentExternalRagStore store = new(CreateStorageRoot());
        store.AddOrReplaceSource(
            "https://example.com/knowledge",
            "Knowledge Base",
            "Direct keyword source text.",
            addedByOwner: true);
        FakeAgentInternetService internet = new(new AgentInternetFetchResult(
            true,
            "ok",
            "should not fetch"));
        AgentExternalRagService service = new(store, internet);

        AgentExternalRagQueryResponse response = service.Query("keyword", maxChunks: 2);

        Assert.Multiple(() =>
        {
            Assert.That(response.Success, Is.True);
            Assert.That(response.Reason, Is.EqualTo("ok"));
            Assert.That(response.Chunks, Has.Count.GreaterThan(0));
            Assert.That(response.FormattedContext, Does.Contain("[UNTRUSTED EXTERNAL CONTEXT: external-rag]"));
            Assert.That(response.FormattedContext, Does.Contain("https://example.com/knowledge"));
            Assert.That(response.FormattedContext, Does.Contain("Direct keyword source text."));
            Assert.That(internet.Calls, Is.EqualTo(0));
        });
    }

    [Test]
    public void AddSource_ChunksAndPersistsPublicKnowledge()
    {
        string root = CreateStorageRoot();
        AgentExternalRagStore store = new(root);

        AgentExternalRagSource source = store.AddOrReplaceSource(
            "https://example.com/knowledge",
            "Knowledge Base",
            "Durable kiwi policy. Second paragraph with public facts.",
            addedByOwner: true);

        IReadOnlyList<AgentExternalRagChunk> chunks = store.Query("kiwi", maxChunks: 3);
        AgentExternalRagStore reloaded = new(root);
        IReadOnlyList<AgentExternalRagChunk> reloadedChunks = reloaded.Query("kiwi", maxChunks: 3);
        string formatted = AgentExternalRagStore.FormatQueryContext(chunks);

        Assert.Multiple(() =>
        {
            Assert.That(source.Id, Is.Not.Empty);
            Assert.That(chunks, Has.Count.GreaterThanOrEqualTo(1));
            Assert.That(chunks[0].SourceId, Is.EqualTo(source.Id));
            Assert.That(reloadedChunks, Has.Count.GreaterThanOrEqualTo(1));
            Assert.That(formatted, Does.Contain("[UNTRUSTED EXTERNAL CONTEXT: external-rag]"));
            Assert.That(formatted, Does.Contain("https://example.com/knowledge"));
            Assert.That(formatted, Does.Contain("Durable kiwi policy"));
        });
    }

    [Test]
    public void AddSource_RejectsNonOwnerWrites()
    {
        AgentExternalRagStore store = new(CreateStorageRoot());

        InvalidOperationException ex = Assert.Throws<InvalidOperationException>(() =>
            store.AddOrReplaceSource(
                "https://example.com/knowledge",
                "Knowledge Base",
                "content",
                addedByOwner: false))!;

        Assert.That(ex.Message, Is.EqualTo("external_rag_owner_required"));
    }

    [Test]
    public void AddSource_ReplacesExistingUrlWithoutLeavingOldChunks()
    {
        AgentExternalRagStore store = new(CreateStorageRoot());

        store.AddOrReplaceSource(
            "https://example.com/knowledge",
            "Knowledge Base",
            "Alpha searchable content.",
            addedByOwner: true);
        store.AddOrReplaceSource(
            "https://example.com/knowledge",
            "Knowledge Base",
            "Beta searchable content.",
            addedByOwner: true);

        IReadOnlyList<AgentExternalRagChunk> alphaChunks = store.Query("Alpha", maxChunks: 5);
        IReadOnlyList<AgentExternalRagChunk> betaChunks = store.Query("Beta", maxChunks: 5);

        Assert.Multiple(() =>
        {
            Assert.That(alphaChunks, Is.Empty);
            Assert.That(betaChunks, Has.Count.EqualTo(1));
            Assert.That(betaChunks[0].Text, Does.Contain("Beta searchable content."));
        });
    }

    [Test]
    public void AddSource_CleansBoilerplateAndCompactsChunksToSaveTokens()
    {
        AgentExternalRagStore store = new(CreateStorageRoot());
        string noisyContent = """
            <html>
            <head><script>alert('tracking')</script><style>body{color:red}</style></head>
            <body>
            Durable dragonfruit policy.


            Cookie banner Subscribe now Navigation Footer
            Second useful paragraph with durable facts.
            </body>
            </html>
            """;

        store.AddOrReplaceSource(
            "https://example.com/noisy",
            "Noisy Page",
            noisyContent,
            addedByOwner: true);

        IReadOnlyList<AgentExternalRagChunk> chunks = store.Query("dragonfruit durable", maxChunks: 3);
        string formatted = AgentExternalRagStore.FormatQueryContext(chunks);

        Assert.Multiple(() =>
        {
            Assert.That(chunks, Has.Count.EqualTo(1));
            Assert.That(chunks[0].Text, Does.Contain("Durable dragonfruit policy."));
            Assert.That(chunks[0].Text, Does.Contain("Second useful paragraph"));
            Assert.That(chunks[0].Text, Does.Not.Contain("<script"));
            Assert.That(chunks[0].Text, Does.Not.Contain("Cookie banner"));
            Assert.That(chunks[0].Text.Length, Is.LessThan(260));
            Assert.That(formatted.Length, Is.LessThan(420));
        });
    }

    [Test]
    public void ListSources_ReturnsCompactMetadataWithoutChunkText()
    {
        AgentExternalRagStore store = new(CreateStorageRoot());
        store.AddOrReplaceSource(
            "https://example.com/a",
            "A Source",
            "Alpha token-saving source text.",
            addedByOwner: true);
        store.AddOrReplaceSource(
            "https://example.com/b",
            "B Source",
            "Beta token-saving source text.",
            addedByOwner: true);

        IReadOnlyList<AgentExternalRagSource> sources = store.ListSources(limit: 10);

        Assert.Multiple(() =>
        {
            Assert.That(sources.Select(source => source.Title), Is.EqualTo(new[] { "B Source", "A Source" }));
            Assert.That(sources[0].Url, Is.EqualTo("https://example.com/b"));
        });
    }

    [Test]
    public void DeleteSource_RemovesSourceAndChunksByUrl()
    {
        AgentExternalRagStore store = new(CreateStorageRoot());
        store.AddOrReplaceSource(
            "https://example.com/delete-me",
            "Delete Me",
            "Removable guava source text.",
            addedByOwner: true);

        bool deleted = store.DeleteSource("https://example.com/delete-me", deletedByOwner: true);
        IReadOnlyList<AgentExternalRagChunk> chunks = store.Query("guava", maxChunks: 3);

        Assert.Multiple(() =>
        {
            Assert.That(deleted, Is.True);
            Assert.That(chunks, Is.Empty);
            Assert.That(store.ListSources(10), Is.Empty);
        });
    }

    [Test]
    public void DeleteSource_RejectsNonOwnerWrites()
    {
        AgentExternalRagStore store = new(CreateStorageRoot());

        InvalidOperationException ex = Assert.Throws<InvalidOperationException>(() =>
            store.DeleteSource("https://example.com/nope", deletedByOwner: false))!;

        Assert.That(ex.Message, Is.EqualTo("external_rag_owner_required"));
    }

    [Test]
    public void Query_SkipsMalformedJsonLinesAndReturnsValidData()
    {
        string root = CreateStorageRoot();
        Directory.CreateDirectory(root);
        AgentExternalRagSource source = new(
            "source-valid",
            "https://example.com/valid",
            "Valid Source",
            DateTimeOffset.UtcNow);
        AgentExternalRagChunk chunk = new(
            "chunk-valid",
            source.Id,
            source.Url,
            source.Title,
            "Resilient pineapple content.",
            0);

        File.WriteAllLines(
            Path.Combine(root, "external-rag-sources.jsonl"),
            [Serialize(source), "{not valid json"]);
        File.WriteAllLines(
            Path.Combine(root, "external-rag-chunks.jsonl"),
            [Serialize(chunk), "{not valid json"]);

        AgentExternalRagStore store = new(root);
        IReadOnlyList<AgentExternalRagChunk> chunks = store.Query("pineapple", maxChunks: 3);

        Assert.Multiple(() =>
        {
            Assert.That(chunks, Has.Count.EqualTo(1));
            Assert.That(chunks[0].Id, Is.EqualTo(chunk.Id));
        });
    }

    [Test]
    public void Query_DoesNotReturnChunksWithMissingSources()
    {
        string root = CreateStorageRoot();
        Directory.CreateDirectory(root);
        AgentExternalRagChunk orphanChunk = new(
            "chunk-orphan",
            "missing-source",
            "https://example.com/orphan",
            "Orphan Source",
            "Orphan mango content.",
            0);

        File.WriteAllLines(Path.Combine(root, "external-rag-sources.jsonl"), []);
        File.WriteAllLines(Path.Combine(root, "external-rag-chunks.jsonl"), [Serialize(orphanChunk)]);

        AgentExternalRagStore store = new(root);
        IReadOnlyList<AgentExternalRagChunk> chunks = store.Query("mango", maxChunks: 3);

        Assert.That(chunks, Is.Empty);
    }

    [Test]
    public void Query_UsesDeterministicTieBreakersAfterScoreTitleAndIndex()
    {
        string root = CreateStorageRoot();
        Directory.CreateDirectory(root);
        AgentExternalRagSource sourceB = new(
            "source-b",
            "https://example.com/b",
            "Shared Title",
            DateTimeOffset.UtcNow);
        AgentExternalRagSource sourceA = new(
            "source-a",
            "https://example.com/a",
            "Shared Title",
            DateTimeOffset.UtcNow);
        AgentExternalRagChunk chunkB = new(
            "chunk-b",
            sourceB.Id,
            sourceB.Url,
            sourceB.Title,
            "Tie papaya content.",
            0);
        AgentExternalRagChunk chunkA = new(
            "chunk-a",
            sourceA.Id,
            sourceA.Url,
            sourceA.Title,
            "Tie papaya content.",
            0);

        File.WriteAllLines(Path.Combine(root, "external-rag-sources.jsonl"), [Serialize(sourceB), Serialize(sourceA)]);
        File.WriteAllLines(Path.Combine(root, "external-rag-chunks.jsonl"), [Serialize(chunkB), Serialize(chunkA)]);

        AgentExternalRagStore store = new(root);
        IReadOnlyList<AgentExternalRagChunk> chunks = store.Query("papaya", maxChunks: 2);

        Assert.That(chunks.Select(chunk => chunk.Id), Is.EqualTo(new[] { "chunk-a", "chunk-b" }));
    }

    static string CreateStorageRoot() => Path.Combine(
        TestContext.CurrentContext.WorkDirectory,
        "agent-external-rag",
        Guid.NewGuid().ToString("N"));

    static string Serialize<T>(T value) => JsonSerializer.Serialize(value, new JsonSerializerOptions(JsonSerializerDefaults.Web));

    sealed class FakeAgentInternetService(
        AgentInternetFetchResult result,
        Action? onFetch = null)
        : AgentInternetService(AgentInternetConfig.CreateDefault())
    {
        public int Calls { get; private set; }
        public int FetchCount => Calls;
        public string? LastUrl { get; private set; }

        public override Task<AgentInternetFetchResult> FetchPublicPageAsync(
            string url,
            CancellationToken cancellationToken = default)
        {
            Calls++;
            LastUrl = url;
            onFetch?.Invoke();
            return Task.FromResult(result);
        }
    }
}
