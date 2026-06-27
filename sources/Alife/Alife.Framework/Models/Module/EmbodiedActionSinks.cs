using System.Threading;
using System.Threading.Tasks;

namespace Alife.Framework;

public interface IBodyExpressionSink
{
    void PlayExpression(string option);
    void PlayMotion(string option);
    Task ShowBubbleAsync(string text, CancellationToken cancellationToken = default);
}

public interface IVoiceOutputSink
{
    Task SpeakAsync(string text, CancellationToken cancellationToken = default);
}

public interface IChatOutputSink
{
    Task SendChatAsync(string targetType, long targetId, string text, bool voice = false);
}
