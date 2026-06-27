using System.Threading;
using System.Threading.Tasks;
using NAudio.Wave;

namespace Alife.Function.Speech;

public interface ISpeechAudioPlayer
{
    Task PlayAsync(string filePath, CancellationToken cancellationToken = default);
}

public sealed class NAudioSpeechAudioPlayer : ISpeechAudioPlayer
{
    public async Task PlayAsync(string filePath, CancellationToken cancellationToken = default)
    {
        TaskCompletionSource tcs = new();

        await using AudioFileReader reader = new(filePath);
        SpeechSilenceTrimmer silenceTrimmer = new(reader);
        using WaveOutEvent speaker = new();
        speaker.Init(silenceTrimmer);
        speaker.PlaybackStopped += OnPlaybackStopped;
        speaker.Play();

        await using CancellationTokenRegistration registration = cancellationToken.Register(() => speaker.Stop());
        await tcs.Task;

        void OnPlaybackStopped(object? _, StoppedEventArgs e)
        {
            if (e.Exception != null)
                tcs.TrySetException(e.Exception);
            else
                tcs.TrySetResult();
        }
    }
}
