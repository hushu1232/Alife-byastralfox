using Alife.Function.Agent;
using NUnit.Framework;
using System.Net;

namespace Alife.Test.Framework;

[TestFixture]
public sealed class AgentBrowserMediaOutputServiceTests
{
    static readonly string TestRoot = Path.Combine("D:\\tmp", "alife-browser-media", "tests");

    [SetUp]
    public void SetUp()
    {
        if (Directory.Exists(TestRoot))
            Directory.Delete(TestRoot, recursive: true);
    }

    [TearDown]
    public void TearDown()
    {
        if (Directory.Exists(TestRoot))
            Directory.Delete(TestRoot, recursive: true);
    }

    [Test]
    public async Task PrepareAsync_VideoLinkReturnsUrlWithoutFetchingOrWriting()
    {
        CountingFetcher fetcher = new(new AgentBrowserMediaFetchResult(true, "ok", "video/mp4", []));
        AgentBrowserMediaOutputService service = CreateService(fetcher);

        AgentBrowserMediaOutputResult result = await service.PrepareAsync(new AgentBrowserMediaOutputRequest(
            AgentBrowserMediaOutputKind.VideoLink,
            "https://example.com/video.mp4",
            TestConfig()));

        Assert.Multiple(() =>
        {
            Assert.That(result.Success, Is.True);
            Assert.That(result.Reason, Is.EqualTo("ok"));
            Assert.That(result.Kind, Is.EqualTo(AgentBrowserMediaOutputKind.VideoLink));
            Assert.That(result.Url, Is.EqualTo("https://example.com/video.mp4"));
            Assert.That(result.ReturnText, Is.EqualTo("https://example.com/video.mp4"));
            Assert.That(result.LocalPath, Is.Null);
            Assert.That(fetcher.Calls, Is.Zero);
            Assert.That(Directory.Exists(TestRoot), Is.False);
        });
    }

    [TestCase("http://127.0.0.1/cat.png", "browser_agent_unsafe_url")]
    [TestCase("https://example.com/archive.zip", "browser_agent_media_type_denied")]
    public async Task PrepareAsync_DeniesUnsafeOrNonMediaImageWithoutFetching(string url, string reason)
    {
        CountingFetcher fetcher = new(new AgentBrowserMediaFetchResult(true, "ok", "image/png", [1, 2, 3]));
        AgentBrowserMediaOutputService service = CreateService(fetcher);

        AgentBrowserMediaOutputResult result = await service.PrepareAsync(new AgentBrowserMediaOutputRequest(
            AgentBrowserMediaOutputKind.Image,
            url,
            TestConfig()));

        Assert.Multiple(() =>
        {
            Assert.That(result.Success, Is.False);
            Assert.That(result.Reason, Is.EqualTo(reason));
            Assert.That(result.Kind, Is.EqualTo(AgentBrowserMediaOutputKind.Image));
            Assert.That(result.Url, Is.EqualTo(url));
            Assert.That(result.LocalPath, Is.Null);
            Assert.That(fetcher.Calls, Is.Zero);
            Assert.That(Directory.Exists(TestRoot), Is.False);
        });
    }

    [Test]
    public async Task PrepareAsync_OversizeImageDeniesWithoutWriting()
    {
        CountingFetcher fetcher = new(new AgentBrowserMediaFetchResult(true, "ok", "image/png", [1, 2, 3, 4]));
        AgentBrowserMediaOutputService service = CreateService(fetcher);

        AgentBrowserMediaOutputResult result = await service.PrepareAsync(new AgentBrowserMediaOutputRequest(
            AgentBrowserMediaOutputKind.Image,
            "https://example.com/cat.png",
            TestConfig(maxImageBytes: 3)));

        Assert.Multiple(() =>
        {
            Assert.That(result.Success, Is.False);
            Assert.That(result.Reason, Is.EqualTo("browser_agent_media_too_large"));
            Assert.That(result.LocalPath, Is.Null);
            Assert.That(fetcher.Calls, Is.EqualTo(1));
            Assert.That(Directory.Exists(TestRoot), Is.False);
        });
    }

    [Test]
    public async Task PrepareAsync_ImageWritesUnderConfiguredDDriveRoot()
    {
        byte[] body = [0x89, 0x50, 0x4e, 0x47, 0x0d, 0x0a, 0x1a, 0x0a];
        CountingFetcher fetcher = new(new AgentBrowserMediaFetchResult(true, "ok", "image/png", body));
        AgentBrowserMediaOutputService service = CreateService(fetcher);

        AgentBrowserMediaOutputResult result = await service.PrepareAsync(new AgentBrowserMediaOutputRequest(
            AgentBrowserMediaOutputKind.Image,
            "https://example.com/images/cat.png",
            TestConfig()));

        Assert.Multiple(() =>
        {
            Assert.That(result.Success, Is.True);
            Assert.That(result.Reason, Is.EqualTo("ok"));
            Assert.That(result.Kind, Is.EqualTo(AgentBrowserMediaOutputKind.Image));
            Assert.That(result.Url, Is.EqualTo("https://example.com/images/cat.png"));
            Assert.That(result.LocalPath, Is.Not.Null);
            Assert.That(result.ReturnText, Is.EqualTo(result.LocalPath));
            Assert.That(Path.GetFullPath(result.LocalPath!), Does.StartWith(Path.GetFullPath(TestRoot)));
            Assert.That(File.ReadAllBytes(result.LocalPath!), Is.EqualTo(body));
            Assert.That(fetcher.Calls, Is.EqualTo(1));
        });
    }

    [Test]
    public async Task PrepareAsync_ImageDeniedWhenResolvedHostIsPrivate()
    {
        CountingFetcher fetcher = new(new AgentBrowserMediaFetchResult(true, "ok", "image/png", [0x89, 0x50, 0x4e, 0x47, 0x0d, 0x0a, 0x1a, 0x0a]));
        AgentBrowserMediaOutputService service = new(
            fetcher: fetcher.FetchAsync,
            resolveHostAsync: (_, _) => Task.FromResult(new[] { IPAddress.Parse("10.0.0.1") }));

        AgentBrowserMediaOutputResult result = await service.PrepareAsync(new AgentBrowserMediaOutputRequest(
            AgentBrowserMediaOutputKind.Image,
            "https://example.com/cat.png",
            TestConfig()));

        Assert.Multiple(() =>
        {
            Assert.That(result.Success, Is.False);
            Assert.That(result.Reason, Is.EqualTo("browser_agent_unsafe_url"));
            Assert.That(fetcher.Calls, Is.Zero);
            Assert.That(Directory.Exists(TestRoot), Is.False);
        });
    }

    [Test]
    public async Task PrepareAsync_ImageWithFakeBodySignatureDeniedWithoutWriting()
    {
        CountingFetcher fetcher = new(new AgentBrowserMediaFetchResult(true, "ok", "image/png", [1, 2, 3, 4, 5, 6, 7, 8]));
        AgentBrowserMediaOutputService service = CreateService(fetcher);

        AgentBrowserMediaOutputResult result = await service.PrepareAsync(new AgentBrowserMediaOutputRequest(
            AgentBrowserMediaOutputKind.Image,
            "https://example.com/cat.png",
            TestConfig()));

        Assert.Multiple(() =>
        {
            Assert.That(result.Success, Is.False);
            Assert.That(result.Reason, Is.EqualTo("browser_agent_media_type_denied"));
            Assert.That(result.LocalPath, Is.Null);
            Assert.That(fetcher.Calls, Is.EqualTo(1));
            Assert.That(Directory.Exists(TestRoot), Is.False);
        });
    }

    [Test]
    public async Task PrepareAsync_VideoZipExtensionDeniedWithoutFetching()
    {
        CountingFetcher fetcher = new(new AgentBrowserMediaFetchResult(true, "ok", "video/mp4", []));
        AgentBrowserMediaOutputService service = CreateService(fetcher);

        AgentBrowserMediaOutputResult result = await service.PrepareAsync(new AgentBrowserMediaOutputRequest(
            AgentBrowserMediaOutputKind.VideoLink,
            "https://example.com/video.zip",
            TestConfig()));

        Assert.Multiple(() =>
        {
            Assert.That(result.Success, Is.False);
            Assert.That(result.Reason, Is.EqualTo("browser_agent_media_type_denied"));
            Assert.That(result.LocalPath, Is.Null);
            Assert.That(fetcher.Calls, Is.Zero);
        });
    }

    [Test]
    public async Task PrepareAsync_ImageUrlWithVideoContentTypeDeniedWithoutWriting()
    {
        CountingFetcher fetcher = new(new AgentBrowserMediaFetchResult(true, "ok", "video/mp4", [1, 2, 3]));
        AgentBrowserMediaOutputService service = CreateService(fetcher);

        AgentBrowserMediaOutputResult result = await service.PrepareAsync(new AgentBrowserMediaOutputRequest(
            AgentBrowserMediaOutputKind.Image,
            "https://example.com/cat.png",
            TestConfig()));

        Assert.Multiple(() =>
        {
            Assert.That(result.Success, Is.False);
            Assert.That(result.Reason, Is.EqualTo("browser_agent_media_type_denied"));
            Assert.That(result.LocalPath, Is.Null);
            Assert.That(fetcher.Calls, Is.EqualTo(1));
            Assert.That(Directory.Exists(TestRoot), Is.False);
        });
    }

    [Test]
    public async Task PrepareAsync_FetchFailureDeniedWithoutWriting()
    {
        CountingFetcher fetcher = new(new AgentBrowserMediaFetchResult(false, "fetch_failed", "image/png", []));
        AgentBrowserMediaOutputService service = CreateService(fetcher);

        AgentBrowserMediaOutputResult result = await service.PrepareAsync(new AgentBrowserMediaOutputRequest(
            AgentBrowserMediaOutputKind.Image,
            "https://example.com/cat.png",
            TestConfig()));

        Assert.Multiple(() =>
        {
            Assert.That(result.Success, Is.False);
            Assert.That(result.Reason, Is.EqualTo("browser_agent_media_download_failed"));
            Assert.That(result.LocalPath, Is.Null);
            Assert.That(fetcher.Calls, Is.EqualTo(1));
            Assert.That(Directory.Exists(TestRoot), Is.False);
        });
    }

    [TestCase("C:\\Alife\\Runtime\\BrowserAgentMedia")]
    [TestCase("D:\\Alife\\Runtime\\BrowserAgentMedia\\..\\Other")]
    [TestCase("D:\\SomeOtherDirectory")]
    public async Task PrepareAsync_DeniesUnsafeMediaRootsWithoutWriting(string root)
    {
        CountingFetcher fetcher = new(new AgentBrowserMediaFetchResult(true, "ok", "image/png", [1, 2, 3]));
        AgentBrowserMediaOutputService service = CreateService(fetcher);

        AgentBrowserMediaOutputResult result = await service.PrepareAsync(new AgentBrowserMediaOutputRequest(
            AgentBrowserMediaOutputKind.Image,
            "https://example.com/cat.png",
            new AgentBrowserAutomationConfig
            {
                Enabled = true,
                MaxImageBytes = 1024,
                MediaCacheRoot = root
            }));

        Assert.Multiple(() =>
        {
            Assert.That(result.Success, Is.False);
            Assert.That(result.Reason, Is.EqualTo("browser_agent_unsafe_media_root"));
            Assert.That(result.LocalPath, Is.Null);
            Assert.That(fetcher.Calls, Is.Zero);
        });
    }

    static AgentBrowserAutomationConfig TestConfig(int maxImageBytes = 1024) =>
        new()
        {
            Enabled = true,
            MaxImageBytes = maxImageBytes,
            MediaCacheRoot = TestRoot
        };

    static AgentBrowserMediaOutputService CreateService(CountingFetcher fetcher) =>
        new(
            fetcher: fetcher.FetchAsync,
            resolveHostAsync: (_, _) => Task.FromResult(new[] { IPAddress.Parse("93.184.216.34") }));

    sealed class CountingFetcher(AgentBrowserMediaFetchResult result)
    {
        public int Calls { get; private set; }

        public Task<AgentBrowserMediaFetchResult> FetchAsync(Uri uri, int maxBytes, CancellationToken cancellationToken)
        {
            Calls++;
            return Task.FromResult(result);
        }
    }
}
