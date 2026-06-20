namespace Alife.Function.QChat;

public sealed record QChatProfilePolicyDecision(
    bool CanApply,
    string Reason);

public sealed class QChatProfileLearningPolicy
{
    public QChatProfilePolicyDecision Evaluate(
        QChatProfileLearningContext context,
        QChatProfileCandidate candidate)
    {
        if (IsProtectedField(candidate.Field))
            return new QChatProfilePolicyDecision(false, "protected_identity_or_permission");

        if (context.IsOwner == false)
            return new QChatProfilePolicyDecision(false, "owner_required");

        if (candidate.TargetUserId <= 0)
            return new QChatProfilePolicyDecision(false, "invalid_target");

        if (string.IsNullOrWhiteSpace(candidate.Value))
            return new QChatProfilePolicyDecision(false, "empty_value");

        if (candidate.Confidence < 0.8f)
            return new QChatProfilePolicyDecision(false, "low_confidence");

        return new QChatProfilePolicyDecision(true, "allowed");
    }

    static bool IsProtectedField(QChatProfileField field)
    {
        return field is QChatProfileField.OwnerIdentity
            or QChatProfileField.PermissionScope
            or QChatProfileField.AgentIdentity
            or QChatProfileField.DesktopCapability;
    }
}
