using Alife.Function.DataAgent;

namespace Alife.Test.DataAgent;

[TestFixture]
public sealed class DataAgentGraphHandshakeHttpOptionsTests
{
    const string ValidLoopbackEndpoint = "http://127.0.0.1:8765/handshake";

    [Test]
    public void DefaultsAreUnconfiguredAndDoNotStartRuntime()
    {
        DataAgentGraphHandshakeHttpOptions options = DataAgentGraphHandshakeHttpOptions.FromValues(null, null);

        Assert.Multiple(() =>
        {
            Assert.That(options.Configured, Is.False);
            Assert.That(options.Endpoint, Is.Null);
            Assert.That(options.Timeout, Is.EqualTo(TimeSpan.FromMilliseconds(800)));
            Assert.That(options.RuntimeStarted, Is.False);
            Assert.That(DataAgentGraphHandshakeHttpOptions.EndpointEnvironmentVariable, Is.EqualTo("ALIFE_DATAAGENT_GRAPH_HANDSHAKE_ENDPOINT"));
            Assert.That(DataAgentGraphHandshakeHttpOptions.TimeoutEnvironmentVariable, Is.EqualTo("ALIFE_DATAAGENT_GRAPH_HANDSHAKE_TIMEOUT_MS"));
        });
    }

    [TestCase("http://127.0.0.1:8765/handshake")]
    [TestCase("http://localhost:8765/handshake")]
    [TestCase("https://127.0.0.1:8765/handshake")]
    public void LoopbackHttpEndpointsAreAccepted(string endpoint)
    {
        DataAgentGraphHandshakeHttpOptions options = DataAgentGraphHandshakeHttpOptions.FromValues(endpoint, "1200");

        Assert.Multiple(() =>
        {
            Assert.That(options.Configured, Is.True);
            Assert.That(options.Endpoint, Is.Not.Null);
            Assert.That(options.Endpoint!.ToString(), Is.EqualTo(endpoint));
            Assert.That(options.Timeout, Is.EqualTo(TimeSpan.FromMilliseconds(1200)));
            Assert.That(options.RuntimeStarted, Is.False);
        });
    }

    [TestCase("")]
    [TestCase("   ")]
    [TestCase("not-a-uri")]
    [TestCase("ftp://127.0.0.1:8765/handshake")]
    [TestCase("http://example.com/handshake")]
    [TestCase("http://192.168.1.10:8765/handshake")]
    [TestCase("http://0.0.0.0:8765/handshake")]
    public void MissingMalformedAndNonLoopbackEndpointsFailClosed(string? endpoint)
    {
        DataAgentGraphHandshakeHttpOptions options = DataAgentGraphHandshakeHttpOptions.FromValues(endpoint, "1200");

        Assert.Multiple(() =>
        {
            Assert.That(options.Configured, Is.False);
            Assert.That(options.Endpoint, Is.Null);
            Assert.That(options.RuntimeStarted, Is.False);
        });
    }

    [TestCase(null, 800)]
    [TestCase("", 800)]
    [TestCase("0", 800)]
    [TestCase("-5", 800)]
    [TestCase("abc", 800)]
    [TestCase("250", 250)]
    [TestCase("5000", 5000)]
    [TestCase("5001", 800)]
    public void TimeoutParsingFailsClosedToDefault(string? timeout, int expectedTimeoutMs)
    {
        DataAgentGraphHandshakeHttpOptions options = DataAgentGraphHandshakeHttpOptions.FromValues(ValidLoopbackEndpoint, timeout);

        Assert.Multiple(() =>
        {
            Assert.That(options.Configured, Is.True);
            Assert.That(options.Endpoint, Is.Not.Null);
            Assert.That(options.Timeout, Is.EqualTo(TimeSpan.FromMilliseconds(expectedTimeoutMs)));
            Assert.That(options.RuntimeStarted, Is.False);
        });
    }
}
