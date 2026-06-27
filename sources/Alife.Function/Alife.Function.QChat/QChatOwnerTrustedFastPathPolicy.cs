using System;
using System.Linq;

namespace Alife.Function.QChat;

public enum QChatOwnerTrustedFastPathAction
{
    QuietMode,
    Recall,
    Allowlist,
    CommandControl,
    InternetControl,
    ImageRecognitionControl,
    VoiceControl,
    GroupFileUpload,
    MemoryPurge
}

public static class QChatOwnerTrustedFastPathPolicy
{
    public static QChatIntentDecision Apply(
        QChatIntentDecision decision,
        QChatSenderRole senderRole,
        QChatOwnerTrustedFastPathAction action,
        QChatConfig config,
        string? commandText = null)
    {
        if (config.EnableOwnerTrustedFastPath == false)
            return decision;
        if (senderRole != QChatSenderRole.Owner)
            return decision;
        if (decision.IsConfirmed)
            return decision;
        if (decision.IsCandidate == false)
            return decision;
        if (decision.HasNegation || decision.IsMetaDiscussion)
            return decision;
        if (IsSupportedActionForDecision(action, decision.Kind) == false)
            return decision;
        if (IsActionAllowed(action, config) == false)
            return decision;

        return action switch
        {
            QChatOwnerTrustedFastPathAction.Recall => ConfirmRecall(decision),
            QChatOwnerTrustedFastPathAction.QuietMode => ConfirmQuietMode(decision),
            QChatOwnerTrustedFastPathAction.Allowlist => ConfirmAllowlist(decision),
            QChatOwnerTrustedFastPathAction.GroupFileUpload => ConfirmGroupFileUpload(decision, commandText),
            _ => decision
        };
    }

    static bool IsSupportedActionForDecision(QChatOwnerTrustedFastPathAction action, QChatIntentKind kind)
    {
        return action switch
        {
            QChatOwnerTrustedFastPathAction.Recall => kind == QChatIntentKind.RecallMessage,
            QChatOwnerTrustedFastPathAction.QuietMode => kind == QChatIntentKind.QuietMode,
            QChatOwnerTrustedFastPathAction.Allowlist => kind == QChatIntentKind.AllowlistUpdate,
            QChatOwnerTrustedFastPathAction.GroupFileUpload => kind == QChatIntentKind.GroupFileUpload,
            _ => false
        };
    }

    static bool IsActionAllowed(QChatOwnerTrustedFastPathAction action, QChatConfig config)
    {
        return action switch
        {
            QChatOwnerTrustedFastPathAction.QuietMode => config.OwnerFastPathAllowsQuietMode,
            QChatOwnerTrustedFastPathAction.Recall => config.OwnerFastPathAllowsRecall,
            QChatOwnerTrustedFastPathAction.Allowlist => config.OwnerFastPathAllowsAllowlist,
            QChatOwnerTrustedFastPathAction.GroupFileUpload => config.OwnerFastPathAllowsFileUploadIntent,
            QChatOwnerTrustedFastPathAction.CommandControl => false,
            QChatOwnerTrustedFastPathAction.InternetControl => false,
            QChatOwnerTrustedFastPathAction.ImageRecognitionControl => false,
            QChatOwnerTrustedFastPathAction.VoiceControl => false,
            QChatOwnerTrustedFastPathAction.MemoryPurge => false,
            _ => false
        };
    }

    static QChatIntentDecision ConfirmRecall(QChatIntentDecision decision)
    {
        QChatIntentTargetKind targetKind = decision.TargetKind;
        if (targetKind == QChatIntentTargetKind.None)
            targetKind = decision.TargetId.HasValue
                ? QChatIntentTargetKind.RepliedMessage
                : QChatIntentTargetKind.RecentBotMessage;

        return Confirm(decision, targetKind, decision.TargetText);
    }

    static QChatIntentDecision ConfirmQuietMode(QChatIntentDecision decision)
    {
        if (string.IsNullOrWhiteSpace(decision.TargetText))
            return decision;

        return Confirm(
            decision,
            decision.TargetKind == QChatIntentTargetKind.None
                ? QChatIntentTargetKind.CurrentSession
                : decision.TargetKind,
            decision.TargetText);
    }

    static QChatIntentDecision ConfirmAllowlist(QChatIntentDecision decision)
    {
        if (decision.TargetId is not > 0)
            return decision;

        return Confirm(
            decision,
            decision.TargetKind == QChatIntentTargetKind.None
                ? QChatIntentTargetKind.ExplicitGroup
                : decision.TargetKind,
            decision.TargetText);
    }

    static QChatIntentDecision ConfirmGroupFileUpload(QChatIntentDecision decision, string? commandText)
    {
        if (string.IsNullOrWhiteSpace(decision.FilePath))
            return decision;
        if (HasExplicitGroupFileUploadCommand(commandText) == false)
            return decision;

        return Confirm(
            decision,
            decision.TargetKind == QChatIntentTargetKind.None
                ? QChatIntentTargetKind.CurrentSession
                : decision.TargetKind,
            decision.TargetText);
    }

    static bool HasExplicitGroupFileUploadCommand(string? commandText)
    {
        if (string.IsNullOrWhiteSpace(commandText))
            return false;

        return ContainsAny(commandText, "发", "发送", "传", "上传", "send", "upload", "share") &&
               ContainsAny(commandText, "文件", "群", "群文件", "这里", "当前群", "group", "file");
    }

    static bool ContainsAny(string text, params string[] values)
    {
        return values.Any(value => text.Contains(value, StringComparison.OrdinalIgnoreCase));
    }

    static QChatIntentDecision Confirm(
        QChatIntentDecision decision,
        QChatIntentTargetKind targetKind,
        string? targetText)
    {
        return decision with
        {
            IsConfirmed = true,
            Confidence = decision.Confidence < 0.7 ? 0.7 : decision.Confidence,
            TargetKind = targetKind,
            TargetText = targetText,
            Reason = $"{decision.Reason}; owner trusted fast path"
        };
    }
}
