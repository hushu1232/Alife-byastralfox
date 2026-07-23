namespace Alife.Function.DataAgent;

public enum DataAgentRuntimeHealthState
{
    Healthy,
    Degraded,
    Unavailable
}

public sealed record DataAgentRuntimeHealthEvent
{
    public const string OneBotComponent = "onebot";
    public const string ModelComponent = "model";
    public const string QZoneOperatorComponent = "qzone_operator";
    public const string CharacterActivationComponent = "character_activation";

    public DataAgentRuntimeHealthEvent(
        string accountId,
        string component,
        DataAgentRuntimeHealthState state,
        string reasonCode)
    {
        if (IsAllowedAccountId(accountId) == false)
            throw new ArgumentOutOfRangeException(nameof(accountId));
        if (IsAllowedComponent(component) == false)
            throw new ArgumentOutOfRangeException(nameof(component));
        if (Enum.IsDefined(state) == false)
            throw new ArgumentOutOfRangeException(nameof(state));
        if (IsAllowedReasonCode(reasonCode) == false)
            throw new ArgumentOutOfRangeException(nameof(reasonCode));
        if (IsCompatible(component, state, reasonCode) == false)
            throw new ArgumentOutOfRangeException(nameof(reasonCode));

        AccountId = accountId;
        Component = component;
        State = state;
        ReasonCode = reasonCode;
    }

    public string AccountId { get; }
    public string Component { get; }
    public DataAgentRuntimeHealthState State { get; }
    public string ReasonCode { get; }

    public static bool IsAllowedAccountId(string? value) => value is "account-a" or "account-b";

    public static bool IsAllowedComponent(string? value) => value is
        OneBotComponent or
        ModelComponent or
        QZoneOperatorComponent or
        CharacterActivationComponent;

    public static bool IsAllowedReasonCode(string? value) => value is
        "OneBotConnected" or
        "OneBotUnavailable" or
        "OneBotProbeUnknown" or
        "ModelAuthRejected" or
        "QZoneOperatorReady" or
        "QZoneOperatorUnavailable" or
        "CharacterActivationFailed" or
        "ClientProcessMissing" or
        "DependencyUnavailable" or
        "ConfigurationRejected" or
        "HealthProbeFailed";

    static bool IsCompatible(string component, DataAgentRuntimeHealthState state, string reasonCode) =>
        component switch
        {
            OneBotComponent => (state, reasonCode) is
                (DataAgentRuntimeHealthState.Healthy, "OneBotConnected") or
                (DataAgentRuntimeHealthState.Unavailable, "OneBotUnavailable") or
                (DataAgentRuntimeHealthState.Degraded, "OneBotProbeUnknown"),
            ModelComponent => (state, reasonCode) is
                (DataAgentRuntimeHealthState.Unavailable, "ModelAuthRejected") or
                (DataAgentRuntimeHealthState.Degraded, "HealthProbeFailed"),
            QZoneOperatorComponent => (state, reasonCode) is
                (DataAgentRuntimeHealthState.Healthy, "QZoneOperatorReady") or
                (DataAgentRuntimeHealthState.Unavailable, "QZoneOperatorUnavailable"),
            CharacterActivationComponent => (state, reasonCode) is
                (DataAgentRuntimeHealthState.Unavailable, "CharacterActivationFailed"),
            _ => false
        };
}
