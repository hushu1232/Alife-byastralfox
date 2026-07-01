using System;
using Alife.Framework.Models.StateEstimation;

namespace Alife.Function.QChat;

public sealed record QChatSemanticStateEstimate(
    double SemanticCompletion,
    double ContinuationLikelihood,
    double TopicStability,
    double SummaryIntent,
    bool ShouldWait,
    bool ShouldAnswer,
    bool ShouldSummarize,
    string ReasonCode);

public static class QChatSemanticStateEstimator
{
    public static QChatSemanticStateEstimate Estimate(
        QChatSemanticWindowSnapshot snapshot,
        DateTimeOffset now,
        QChatSemanticSettleOptions options)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        ArgumentNullException.ThrowIfNull(options);

        if (snapshot.Messages.Count == 0)
            return new QChatSemanticStateEstimate(0.0, 1.0, 0.0, 0.0, true, false, false, "semantic_window_empty");

        KalmanScalarFilter completion = new(0.45, 0.70);
        KalmanScalarFilter continuation = new(0.60, 0.65);
        KalmanScalarFilter topicStability = new(0.50, 0.50);
        KalmanScalarFilter summaryIntent = new(0.05, 0.40);

        foreach (QChatSemanticWindowMessage message in snapshot.Messages)
        {
            string text = message.Text ?? string.Empty;
            bool incomplete = LooksIncomplete(text);
            bool summary = LooksLikeSummaryRequest(text);
            bool question = LooksLikeQuestion(text);

            double observationNoise = incomplete ? 0.60 : 0.18;
            double completionObservation = incomplete
                ? 0.20
                : summary
                    ? 0.92
                    : question
                        ? 0.88
                        : 0.80;

            completion = completion.Predict(0.08).Update(completionObservation, observationNoise);
            continuation = continuation.Predict(0.06).Update(incomplete ? 0.88 : 0.22, observationNoise);
            topicStability = topicStability.Predict(0.04).Update(EstimateTopicStability(snapshot), 0.25);
            summaryIntent = summaryIntent.Predict(0.04).Update(summary ? 0.95 : 0.05, summary ? 0.12 : 0.35);
        }

        TimeSpan elapsedSinceLast = now - snapshot.LastUpdatedAt;
        bool delaySatisfied = elapsedSinceLast >= options.SettleDelay;
        QChatSemanticWindowMessage latest = snapshot.Messages[^1];
        bool latestIncomplete = LooksIncomplete(latest.Text);

        bool shouldSummarize = delaySatisfied &&
            latestIncomplete == false &&
            summaryIntent.Value >= 0.75 &&
            completion.Value >= 0.75;
        bool shouldAnswer = delaySatisfied &&
            latestIncomplete == false &&
            shouldSummarize == false &&
            completion.Value >= 0.70 &&
            continuation.Value <= 0.45;
        bool shouldWait = shouldAnswer == false && shouldSummarize == false;
        string reasonCode = shouldSummarize
            ? "summary_intent_stable"
            : shouldAnswer
                ? "semantic_completion_stable"
                : "semantic_continuation_likely";

        return new QChatSemanticStateEstimate(
            completion.Value,
            continuation.Value,
            topicStability.Value,
            summaryIntent.Value,
            shouldWait,
            shouldAnswer,
            shouldSummarize,
            reasonCode);
    }

    static bool LooksIncomplete(string? text)
    {
        string value = (text ?? string.Empty).Trim();
        if (value.Length == 0)
            return true;

        return value.EndsWith(",", StringComparison.Ordinal) ||
               value.EndsWith(";", StringComparison.Ordinal) ||
               value.EndsWith(":", StringComparison.Ordinal) ||
               value.EndsWith("...", StringComparison.Ordinal) ||
               value.EndsWith("以及", StringComparison.Ordinal) ||
               value.EndsWith("然后", StringComparison.Ordinal) ||
               value.EndsWith("还有", StringComparison.Ordinal) ||
               value.EndsWith("and", StringComparison.OrdinalIgnoreCase) ||
               value.EndsWith("or", StringComparison.OrdinalIgnoreCase);
    }

    static bool LooksLikeQuestion(string text)
    {
        return text.Contains("?", StringComparison.Ordinal) ||
               text.Contains("？", StringComparison.Ordinal) ||
               text.Contains("怎么", StringComparison.Ordinal) ||
               text.Contains("如何", StringComparison.Ordinal) ||
               text.Contains("为什么", StringComparison.Ordinal);
    }

    static bool LooksLikeSummaryRequest(string text)
    {
        return text.Contains("总结", StringComparison.Ordinal) ||
               text.Contains("归纳", StringComparison.Ordinal) ||
               text.Contains("梳理", StringComparison.Ordinal);
    }

    static double EstimateTopicStability(QChatSemanticWindowSnapshot snapshot)
    {
        if (snapshot.Messages.Count <= 1)
            return 0.80;

        int sameSenderPairs = 0;
        for (int i = 1; i < snapshot.Messages.Count; i++)
        {
            if (snapshot.Messages[i].SenderId == snapshot.Messages[i - 1].SenderId)
                sameSenderPairs++;
        }

        return Math.Clamp(0.45 + sameSenderPairs * 0.15, 0.0, 0.95);
    }
}
