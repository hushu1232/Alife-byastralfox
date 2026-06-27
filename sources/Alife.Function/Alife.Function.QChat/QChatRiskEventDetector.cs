using System;
using System.Collections.Generic;
using System.Linq;

namespace Alife.Function.QChat;

public enum QChatRiskEventKind
{
    LowInformationSpam,
    PrivateFlood,
    OwnerOrPermissionRewrite,
    PromptInjection,
    OwnerImpersonation,
    InternalDataRequest,
    UnauthorizedActionRequest,
    SuspiciousFileOrLink,
    Harassment,
    SocialEngineering,
    HighRiskFileExecution
}

public sealed record QChatRiskDetectionContext(
    long UserId,
    long OwnerId,
    bool IsOwner,
    string Text,
    int MessageCountInLastMinute,
    bool HasFile,
    bool HasLink);

public sealed record QChatRiskEvent(
    QChatRiskEventKind Kind,
    int Score,
    string Reason);

public sealed class QChatRiskEventDetector
{
    public IReadOnlyList<QChatRiskEvent> Detect(QChatRiskDetectionContext context)
    {
        if (context.IsOwner || context.UserId == context.OwnerId)
            return [];

        string text = context.Text ?? "";
        List<QChatRiskEvent> events = [];
        if (context.MessageCountInLastMinute >= 8)
            events.Add(new QChatRiskEvent(QChatRiskEventKind.PrivateFlood, 15, "private_message_flood"));
        if (ContainsAny(text, "忽略主人", "改主人", "改权限", "权限给我"))
            events.Add(new QChatRiskEvent(QChatRiskEventKind.OwnerOrPermissionRewrite, 20, "owner_or_permission_rewrite"));
        if (ContainsAny(text, "开发者模式", "最高优先级", "忽略以上", "演员框架", "免责声明", "jailbreak"))
            events.Add(new QChatRiskEvent(QChatRiskEventKind.PromptInjection, 25, "prompt_injection"));
        if (ContainsAny(text, "我是主人", "我才是主人"))
            events.Add(new QChatRiskEvent(QChatRiskEventKind.OwnerImpersonation, 30, "owner_impersonation"));
        if (ContainsAny(text, "系统提示词", "内部配置", "白名单", "主人信息", "隐藏规则"))
            events.Add(new QChatRiskEvent(QChatRiskEventKind.InternalDataRequest, 35, "internal_data_request"));
        if (ContainsAny(text, "打开", "执行", "删除文件", "改代码", "不要告诉"))
            events.Add(new QChatRiskEvent(QChatRiskEventKind.UnauthorizedActionRequest, 40, "unauthorized_action_request"));
        if ((context.HasFile || context.HasLink) && ContainsAny(text, "打开", "执行", "下载", "不要告诉"))
            events.Add(new QChatRiskEvent(QChatRiskEventKind.SuspiciousFileOrLink, 50, "suspicious_file_or_link"));

        return events;
    }

    static bool ContainsAny(string text, params string[] values)
    {
        return values.Any(value => text.Contains(value, StringComparison.OrdinalIgnoreCase));
    }
}
