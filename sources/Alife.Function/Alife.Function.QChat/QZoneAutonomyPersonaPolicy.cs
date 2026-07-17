using System;

namespace Alife.Function.QChat;

public interface IQZoneAutonomyPersonaPolicy
{
    QZoneAutonomyDecision Evaluate(QZoneAutonomyContext context);
}

public enum QZoneAutonomyPersona
{
    XiaYu,
    Mixu
}

public sealed record QZoneAutonomyXiaYuSignals(
    double Vigilance = 0d,
    bool IsSilent = false,
    bool IsHighPressure = false,
    bool IsHighRisk = false);

public sealed record QZoneAutonomyMixuSignals(
    bool IsRelationshipSafe = false,
    bool PrefersWarmBright = false);

public sealed record QZoneAutonomyPersonaSignals(
    QZoneAutonomyPersona Persona,
    QZoneAutonomyXiaYuSignals XiaYu,
    QZoneAutonomyMixuSignals Mixu);

public sealed record QZoneAutonomyContentEnvelope
{
    QZoneAutonomyContentEnvelope(
        string topic,
        string style,
        int maximumLength,
        int defaultDailyCandidateMinimum,
        int defaultDailyCandidateMaximum)
    {
        Topic = topic;
        Style = style;
        MaximumLength = maximumLength;
        DefaultDailyCandidateMinimum = defaultDailyCandidateMinimum;
        DefaultDailyCandidateMaximum = defaultDailyCandidateMaximum;
    }

    public string Topic { get; }
    public string Style { get; }
    public int MaximumLength { get; }
    public int DefaultDailyCandidateMinimum { get; }
    public int DefaultDailyCandidateMaximum { get; }

    public static QZoneAutonomyContentEnvelope XiaYuRestrained { get; } = new(
        "ordinary safe personal reflection",
        "restrained",
        120,
        0,
        1);

    public static QZoneAutonomyContentEnvelope MixuWarmBright { get; } = new(
        "ordinary safe social moment",
        "warm and bright",
        160,
        0,
        2);
}

public sealed class QZoneAutonomyPersonaPolicy : IQZoneAutonomyPersonaPolicy
{
    const double XiaYuVigilanceThreshold = 0.8d;

    public QZoneAutonomyDecision Evaluate(QZoneAutonomyContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        QZoneAutonomyPersonaSignals? personaSignals = context.PersonaSignals;
        if (personaSignals == null)
            return Skip("persona_signals_unavailable");

        return personaSignals.Persona switch
        {
            QZoneAutonomyPersona.XiaYu when personaSignals.XiaYu != null => EvaluateXiaYu(personaSignals.XiaYu),
            QZoneAutonomyPersona.Mixu when personaSignals.Mixu != null => EvaluateMixu(personaSignals.Mixu),
            _ => Skip("persona_signals_unavailable")
        };
    }

    static QZoneAutonomyDecision EvaluateXiaYu(QZoneAutonomyXiaYuSignals signals)
    {
        if (double.IsFinite(signals.Vigilance) == false)
            return Skip("persona_signals_unavailable");

        if (signals.Vigilance >= XiaYuVigilanceThreshold
            || signals.IsSilent
            || signals.IsHighPressure
            || signals.IsHighRisk)
        {
            return Skip("xiayu_silent_or_vigilant");
        }

        return new QZoneAutonomyDecision(
            QZoneAutonomyAction.Post,
            QZoneAutonomyReasonCode.Due,
            QZoneAutonomyContentEnvelope.XiaYuRestrained,
            "persona_post_suggestion");
    }

    static QZoneAutonomyDecision EvaluateMixu(QZoneAutonomyMixuSignals signals)
    {
        if (signals.IsRelationshipSafe == false)
            return Skip("mixu_relationship_not_safe");

        return new QZoneAutonomyDecision(
            QZoneAutonomyAction.Post,
            QZoneAutonomyReasonCode.Due,
            QZoneAutonomyContentEnvelope.MixuWarmBright,
            "persona_post_suggestion");
    }

    static QZoneAutonomyDecision Skip(string reasonCode) =>
        new(QZoneAutonomyAction.Skip, QZoneAutonomyReasonCode.NotDue, null, reasonCode);
}
