using System;
using System.Collections.Generic;

namespace Alife.Function.QChat;

public enum QChatOwnerEngineeringReplyStage
{
    Intake,
    Hypothesis,
    Blocked,
    Complete,
}

public sealed record QChatOwnerEngineeringReply(
    QChatOwnerEngineeringReplyStage Stage,
    string Facts,
    string? Verification = null,
    string? UncertaintyOrFailure = null);

public static class QChatOwnerEngineeringReplyFormatter
{
    public static string Format(string? agentId, QChatSenderRole senderRole, QChatOwnerEngineeringReply? reply)
    {
        if (reply is null)
            return string.Empty;

        string facts = reply.Facts?.Trim() ?? string.Empty;
        if (string.IsNullOrWhiteSpace(facts))
            return string.Empty;

        string personaLead = GetPersonaLead(agentId, senderRole);
        string stageLead = reply.Stage switch
        {
            QChatOwnerEngineeringReplyStage.Intake => "我会先收窄路径。",
            QChatOwnerEngineeringReplyStage.Hypothesis => "目前的判断在这里。",
            QChatOwnerEngineeringReplyStage.Blocked => "这条路径暂时卡住了。",
            QChatOwnerEngineeringReplyStage.Complete => "这条路径已处理完。",
            _ => "工程状态如下。",
        };

        List<string> lines = [personaLead + stageLead, facts];
        AddTrimmedLine(lines, reply.Verification);
        AddTrimmedLine(lines, reply.UncertaintyOrFailure);
        return string.Join(Environment.NewLine, lines);
    }

    static string GetPersonaLead(string? agentId, QChatSenderRole senderRole)
    {
        if (senderRole != QChatSenderRole.Owner)
            return "工程状态如下。";

        return agentId?.Trim().ToLowerInvariant() switch
        {
            "xiayu" => "术术，",
            "mixu" => "主人，",
            _ => "工程状态如下。",
        };
    }

    static void AddTrimmedLine(List<string> lines, string? value)
    {
        string line = value?.Trim() ?? string.Empty;
        if (!string.IsNullOrWhiteSpace(line))
            lines.Add(line);
    }
}
