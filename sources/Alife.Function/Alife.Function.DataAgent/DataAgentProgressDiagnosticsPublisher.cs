namespace Alife.Function.DataAgent;

public sealed class DataAgentProgressDiagnosticsPublisher(
    DataAgentProgressRecorder recorder,
    Action<string>? diagnosticsPublisher,
    Func<DateTimeOffset>? clock = null) : IDataAgentProgressSink
{
    readonly Func<DateTimeOffset> clock = clock ?? (() => DateTimeOffset.UtcNow);

    public void Publish(DataAgentProgressEvent? progressEvent)
    {
        if (progressEvent is null)
            return;

        recorder.Publish(progressEvent);
        if (diagnosticsPublisher is null)
            return;

        DateTimeOffset now = clock();
        IReadOnlyList<DataAgentProgressEvent> recent = recorder.GetRecent(progressEvent.SessionId, now);
        diagnosticsPublisher(DataAgentProgressDiagnosticsFormatter.Format(recent, progressEvent.SessionId, now));
    }
}
