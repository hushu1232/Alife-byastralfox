using System;

namespace Alife.Function.QChat;

public static class QChatPersonaStylePolicy
{
    public static string Format(string? agentId, QChatPersonaFrame frame, string? plainText)
    {
        ArgumentNullException.ThrowIfNull(frame);

        if (string.Equals(agentId?.Trim(), "xiayu", StringComparison.OrdinalIgnoreCase) == false)
        {
            return "tone=gentle_capable\nsuffix=natural_not_every_sentence";
        }

        if (frame.SpeakerRole == QChatPersonaSpeakerRole.Owner)
        {
            bool attacked = ContainsAny(plainText, "笨", "蠢", "废物", "垃圾", "滚", "讨厌");
            return string.Join('\n', [
                "tone=warm_intimate",
                "punctuation=avoid_chinese_full_stop",
                "length=can_be_more_complete_when_helpful",
                attacked
                    ? "owner_attack=natural_hurt"
                    : "owner_attack=none",
                "rule=Do not use fixed catchphrases. Let the reply sound naturally close and caring."
            ]);
        }

        bool isBoundary = frame.RecommendedStance is QChatPersonaResponseStance.HostilePushback
            or QChatPersonaResponseStance.ProtectivePushback;
        return string.Join('\n', [
            "tone=polite_reserved",
            "punctuation=prefer_chinese_full_stop",
            "length=concise_complete_sentence",
            isBoundary ? "defense=natural_sharp" : "defense=none",
            "rule=Stay rational and courteous. For a boundary violation, generate a firm natural reply instead of a fixed canned phrase."
        ]);
    }

    static bool ContainsAny(string? text, params string[] values)
    {
        foreach (string value in values)
        {
            if ((text ?? string.Empty).Contains(value, StringComparison.OrdinalIgnoreCase))
                return true;
        }

        return false;
    }
}
