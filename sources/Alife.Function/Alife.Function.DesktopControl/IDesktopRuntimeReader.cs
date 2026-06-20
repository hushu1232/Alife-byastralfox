namespace Alife.Function.DesktopControl;

public interface IDesktopRuntimeReader
{
    Task<DesktopSnapshot> CaptureAsync(CancellationToken cancellationToken = default);
}
