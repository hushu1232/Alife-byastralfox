using Alife.Framework;

namespace Alife.Components.Services;

/// <summary>
/// A UI-safe projection of a character activity lifecycle state.
/// </summary>
public enum ActivityActivationState
{
    Initializing,
    Active,
    Failed,
    Destroyed
}

/// <summary>
/// Contains only the character UI identity and lifecycle state. It deliberately
/// carries no exception, diagnostic, DataAgent, or LangGraph payload.
/// </summary>
public sealed record ActivityActivationStatus(
    string CharacterName,
    ActivityActivationState State);

/// <summary>
/// Provides safe lifecycle state notifications for client UI components.
/// </summary>
public class ActivityNotifyService
{
    public event Action? OnChanged;
    public event Action<ActivityActivationStatus>? OnActivationStateChanged;

    readonly Dictionary<string, ActivityActivationState> states = new(StringComparer.Ordinal);
    readonly object stateGate = new();

    public ActivityNotifyService(ChatActivitySystem system)
    {
        system.Activating += character => Publish(character.Name, ActivityActivationState.Initializing);
        system.Activated += activity => Publish(activity.Character.Name, ActivityActivationState.Active);
        system.ActivationFailed += (character, _) => Publish(character.Name, ActivityActivationState.Failed);
        system.Destroyed += activity => Publish(activity.Character.Name, ActivityActivationState.Destroyed);
    }

    public ActivityActivationState? GetActivationState(string characterName)
    {
        lock (stateGate)
            return states.TryGetValue(characterName, out ActivityActivationState state)
                ? state
                : null;
    }

    void Publish(string characterName, ActivityActivationState state)
    {
        ActivityActivationStatus status = new(characterName, state);
        lock (stateGate)
            states[characterName] = state;

        NotifySafely(OnActivationStateChanged, status);
        NotifySafely(OnChanged);
    }

    static void NotifySafely(Action? handlers)
    {
        if (handlers == null)
            return;

        foreach (Action handler in handlers.GetInvocationList())
        {
            try
            {
                handler();
            }
            catch
            {
                // UI refresh listeners must never change the activation result.
            }
        }
    }

    static void NotifySafely(Action<ActivityActivationStatus>? handlers, ActivityActivationStatus status)
    {
        if (handlers == null)
            return;

        foreach (Action<ActivityActivationStatus> handler in handlers.GetInvocationList())
        {
            try
            {
                handler(status);
            }
            catch
            {
                // UI state listeners must never change the activation result.
            }
        }
    }
}
