namespace Alife.Function.DataAgent;

public interface IDataAgentProgressSink
{
    void Publish(DataAgentProgressEvent? progressEvent);
}
