namespace Alife.Function.Emotion;

public enum EmotionEventType
{
    Petted,
    Ignored,
    PositiveChat,
    NegativeChat,
    WakeUp,
    FallAsleep,
    Dragged,
    Fed,
    Scared,
    Complimented,
    Insulted,
    PlayedWith,
    LongAbsence
}

public readonly record struct EmotionEvent(
    EmotionEventType Type,
    float PleasureDelta,
    float ArousalDelta,
    float DominanceDelta,
    float Duration = 0f);
