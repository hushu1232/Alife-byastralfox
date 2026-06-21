using System;

namespace Alife.Function.QChat;

public sealed record QChatDecisionTrace(
    string TraceId,
    long BotId,
    string AgentId,
    OneBotMessageType MessageType,
    QChatSenderRole SenderRole,
    QChatIntentKind IntentKind,
    bool IntentCandidate,
    bool IntentConfirmed,
    string GateDecision,
    string ReplyDecision,
    string CapabilityDecision,
    string FinalAction,
    string Reason,
    DateTimeOffset CreatedAt)
{
    public string ToDiagnosticText()
    {
        return string.Join(" ",
            "qchat decision:",
            $"trace={Compact(TraceId)}",
            $"bot={BotId}",
            $"agent={Compact(AgentId)}",
            $"surface={MessageType}",
            $"actor={SenderRole}",
            $"intent={IntentKind}",
            $"candidate={FormatBool(IntentCandidate)}",
            $"confirmed={FormatBool(IntentConfirmed)}",
            $"gate={Compact(GateDecision)}",
            $"reply={Compact(ReplyDecision)}",
            $"capability={Compact(CapabilityDecision)}",
            $"action={Compact(FinalAction)}",
            $"reason={Compact(Reason)}",
            $"created={CreatedAt:O}");
    }

    static string FormatBool(bool value)
    {
        return value ? "true" : "false";
    }

    static string Compact(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return "none";

        return value
            .Replace('\r', ' ')
            .Replace('\n', ' ')
            .Trim();
    }
}
