using Alife.Function.DataAgent;

namespace Alife.Test.DataAgent;

[TestFixture]
public sealed class DataAgentGraphHandshakeStreamOptionsTests
{
    const string ValidLoopbackEndpoint = "http://127.0.0.1:8765/handshake-stream";

    [Test]
    public void DefaultsAreDisabledUnconfiguredAndDoNotStartRuntime()
    {
        DataAgentGraphHandshakeStreamOptions options = DataAgentGraphHandshakeStreamOptions.FromValues(null, null, null);

        Assert.Multiple(() =>
        {
            Assert.That(options.Enabled, Is.False);
            Assert.That(options.Configured, Is.False);
            Assert.That(options.Endpoint, Is.Null);
            Assert.That(options.Timeout, Is.EqualTo(TimeSpan.FromMilliseconds(800)));
            Assert.That(options.RuntimeStarted, Is.False);
            Assert.That(DataAgentGraphHandshakeStreamOptions.EnabledEnvironmentVariable, Is.EqualTo("ALIFE_DATAAGENT_GRAPH_HANDSHAKE_STREAM_ENABLED"));
            Assert.That(DataAgentGraphHandshakeStreamOptions.EndpointEnvironmentVariable, Is.EqualTo("ALIFE_DATAAGENT_GRAPH_HANDSHAKE_STREAM_ENDPOINT"));
            Assert.That(DataAgentGraphHandshakeStreamOptions.TimeoutEnvironmentVariable, Is.EqualTo("ALIFE_DATAAGENT_GRAPH_HANDSHAKE_STREAM_TIMEOUT_MS"));
        });
    }

    [TestCase("true")]
    [TestCase("1")]
    [TestCase("yes")]
    public void EnabledLoopbackEndpointIsConfigured(string enabled)
    {
        DataAgentGraphHandshakeStreamOptions options = DataAgentGraphHandshakeStreamOptions.FromValues(enabled, ValidLoopbackEndpoint, "1200");

        Assert.Multiple(() =>
        {
            Assert.That(options.Enabled, Is.True);
            Assert.That(options.Configured, Is.True);
            Assert.That(options.Endpoint, Is.Not.Null);
            Assert.That(options.Endpoint!.ToString(), Is.EqualTo(ValidLoopbackEndpoint));
            Assert.That(options.Timeout, Is.EqualTo(TimeSpan.FromMilliseconds(1200)));
            Assert.That(options.RuntimeStarted, Is.False);
        });
    }

    [TestCase("false")]
    [TestCase("0")]
    [TestCase("no")]
    [TestCase("maybe")]
    public void DisabledOrUnknownEnabledValueIsNotConfigured(string enabled)
    {
        DataAgentGraphHandshakeStreamOptions options = DataAgentGraphHandshakeStreamOptions.FromValues(enabled, ValidLoopbackEndpoint, "1200");

        Assert.Multiple(() =>
        {
            Assert.That(options.Enabled, Is.False);
            Assert.That(options.Configured, Is.False);
            Assert.That(options.Endpoint, Is.Null);
            Assert.That(options.Timeout, Is.EqualTo(TimeSpan.FromMilliseconds(1200)));
            Assert.That(options.RuntimeStarted, Is.False);
        });
    }

    [TestCase("http://localhost:8765/handshake-stream")]
    [TestCase("https://127.0.0.1:8765/handshake-stream")]
    public void LoopbackStreamEndpointsAreAccepted(string endpoint)
    {
        DataAgentGraphHandshakeStreamOptions options = DataAgentGraphHandshakeStreamOptions.FromValues("true", endpoint, "1200");

        Assert.Multiple(() =>
        {
            Assert.That(options.Enabled, Is.True);
            Assert.That(options.Configured, Is.True);
            Assert.That(options.Endpoint, Is.Not.Null);
            Assert.That(options.Endpoint!.ToString(), Is.EqualTo(endpoint));
            Assert.That(options.Timeout, Is.EqualTo(TimeSpan.FromMilliseconds(1200)));
            Assert.That(options.RuntimeStarted, Is.False);
        });
    }

    [TestCase("http://example.com/handshake-stream")]
    [TestCase("http://0.0.0.0:8765/handshake-stream")]
    [TestCase("file:///tmp/handshake-stream")]
    [TestCase("")]
    [TestCase("   ")]
    public void NonLoopbackOrBlankEndpointIsNotConfigured(string endpoint)
    {
        DataAgentGraphHandshakeStreamOptions options = DataAgentGraphHandshakeStreamOptions.FromValues("true", endpoint, "1200");

        Assert.Multiple(() =>
        {
            Assert.That(options.Enabled, Is.True);
            Assert.That(options.Configured, Is.False);
            Assert.That(options.Endpoint, Is.Null);
            Assert.That(options.Timeout, Is.EqualTo(TimeSpan.FromMilliseconds(1200)));
            Assert.That(options.RuntimeStarted, Is.False);
        });
    }

    [TestCase(null, 800)]
    [TestCase("", 800)]
    [TestCase("99", 800)]
    [TestCase("100", 100)]
    [TestCase("5000", 5000)]
    [TestCase("5001", 800)]
    public void TimeoutParsingUsesSafeBounds(string? timeout, int expectedTimeoutMs)
    {
        DataAgentGraphHandshakeStreamOptions options = DataAgentGraphHandshakeStreamOptions.FromValues("true", ValidLoopbackEndpoint, timeout);

        Assert.Multiple(() =>
        {
            Assert.That(options.Enabled, Is.True);
            Assert.That(options.Configured, Is.True);
            Assert.That(options.Endpoint, Is.Not.Null);
            Assert.That(options.Timeout, Is.EqualTo(TimeSpan.FromMilliseconds(expectedTimeoutMs)));
            Assert.That(options.RuntimeStarted, Is.False);
        });
    }
}
