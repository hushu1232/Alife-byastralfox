using System;

namespace Alife.Function.QChat;

public static class QChatImageRecognitionPolicy
{
    public static QChatImageRecognitionPolicyDecision Decide(QChatImageRecognitionPolicyContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        int maxImages = Math.Max(1, context.Config.MaxImagesPerMessage);
        if (context.Config.EnableImageRecognition == false)
            return Skip("image_recognition_disabled", maxImages);
        if (context.ImageCount <= 0)
            return Skip("no_images", maxImages);
        if (context.ImageCount > maxImages)
            return Skip("too_many_images", maxImages);

        if (context.SenderRole == QChatSenderRole.Owner &&
            context.MessageType == OneBotMessageType.Private)
        {
            return context.Config.AnalyzeOwnerPrivateImages
                ? Analyze("owner_private_image", maxImages)
                : Skip("owner_private_image_disabled", maxImages);
        }

        if (context.SenderRole == QChatSenderRole.Owner &&
            context.MessageType == OneBotMessageType.Group)
        {
            return context.Config.AnalyzeOwnerGroupImages
                ? Analyze("owner_group_image", maxImages)
                : Skip("owner_group_image_disabled", maxImages);
        }

        if (context.MessageType == OneBotMessageType.Private)
        {
            return context.Config.AnalyzePrivateGuestImages
                ? Analyze("private_guest_image", maxImages)
                : Skip("private_guest_image_disabled", maxImages);
        }

        if (context.MessageType == OneBotMessageType.Group && context.IsMentionedOrWoken)
        {
            return context.Config.AnalyzeMentionedGroupImages
                ? Analyze("mentioned_group_image", maxImages)
                : Skip("mentioned_group_image_disabled", maxImages);
        }

        if (context.MessageType == OneBotMessageType.Group && context.IsPassiveGroupMessage)
        {
            return context.Config.AnalyzePassiveGroupImages
                ? Analyze("passive_group_image", maxImages)
                : Skip("passive_group_image_disabled", maxImages);
        }

        return Skip("policy_no_matching_route", maxImages);
    }

    static QChatImageRecognitionPolicyDecision Analyze(string reason, int maxImages) =>
        new(QChatImageRecognitionAction.Analyze, reason, maxImages);

    static QChatImageRecognitionPolicyDecision Skip(string reason, int maxImages) =>
        new(QChatImageRecognitionAction.Skip, reason, maxImages);
}
