namespace Alife.Function.DataAgent;

public enum DataAgentGraphHandshakeStreamEventKind
{
    Progress,
    FinalResponse
}

public sealed record DataAgentGraphHandshakeStreamEvent(
    DataAgentGraphHandshakeStreamEventKind Kind,
    DataAgentGraphHandshakeProgress? Progress = null,
    DataAgentGraphHandshakeResponse? Response = null);

public sealed record DataAgentGraphHandshakeStreamResult(
    DataAgentGraphHandshakeResponse Response,
    IReadOnlyList<DataAgentGraphHandshakeProgress> Progress);

public interface IDataAgentGraphHandshakeStreamClient
{
    DataAgentGraphHandshakeStreamResult TryHandshakeStream(DataAgentGraphHandshakeRequest request);
}

public sealed class DataAgentGraphSidecarInvalidStreamException : Exception
{
    public string ReasonCode { get; }

    public DataAgentGraphSidecarInvalidStreamException(string reasonCode)
        : base(reasonCode)
    {
        ReasonCode = reasonCode;
    }

    public DataAgentGraphSidecarInvalidStreamException(string reasonCode, Exception innerException)
        : base(reasonCode, innerException)
    {
        ReasonCode = reasonCode;
    }
}
