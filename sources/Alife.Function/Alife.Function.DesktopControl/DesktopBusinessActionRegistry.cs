namespace Alife.Function.DesktopControl;

public sealed record DesktopBusinessActionDescriptor(
    string ActionKey,
    string NormalizedRequest,
    string ExecutableName,
    string Arguments);

public sealed class DesktopBusinessActionRegistry(IReadOnlyList<DesktopBusinessActionDescriptor> actions)
{
    readonly Dictionary<string, DesktopBusinessActionDescriptor> actions = actions.ToDictionary(
        action => action.NormalizedRequest,
        StringComparer.OrdinalIgnoreCase);

    public static DesktopBusinessActionRegistry CreateDefault()
    {
        return new DesktopBusinessActionRegistry([
            new DesktopBusinessActionDescriptor(
                "open_notepad",
                "open notepad",
                "notepad.exe",
                ""),
            new DesktopBusinessActionDescriptor(
                "open_calculator",
                "open calculator",
                "calc.exe",
                "")
        ]);
    }

    public bool TryResolve(
        string requestedAction,
        out DesktopBusinessActionDescriptor? descriptor)
    {
        return actions.TryGetValue(NormalizeAction(requestedAction), out descriptor);
    }

    public bool IsSupported(string requestedAction) => TryResolve(requestedAction, out _);

    static string NormalizeAction(string? value)
    {
        string normalized = (value ?? string.Empty).Trim().ToLowerInvariant();
        while (normalized.Contains("  ", StringComparison.Ordinal))
            normalized = normalized.Replace("  ", " ", StringComparison.Ordinal);
        return normalized;
    }
}
