using Alife.Framework;
using Microsoft.SemanticKernel;

namespace Alife.Implement;

public class MessageFilterData
{
    public bool EnableTimestamp { get; set; } = true;
    public string MessageHeader { get; set; } = "（注意：回复请保持简洁，不要插入旁白，根据消息类型，正确灵活的使用标签功能。）\n";
    public string PokeHeader { get; set; } = "（注意：下方为非用户消息，请思考后再回复）\n";
}
[Plugin("消息滤网", "统一管理消息的提示词注入和格式化。负责添加时间戳、通用提示词以及系统消息头。", LaunchOrder = 900, EditorUI = typeof(MessageFilterServiceUI))]
public class MessageFilterService : InteractivePlugin<MessageFilterService>, IConfigurable<MessageFilterData>
{
    public MessageFilterData? Configuration { get; set; }

    public override async Task StartAsync(Kernel kernel, ChatActivity chatActivity)
    {
        await base.StartAsync(kernel, chatActivity);
        ChatBot.ChatSend += OnChatSend;
        ChatBot.PokeSend += OnPokeSend;
    }

    string OnChatSend(string message)
    {
        string result = message;
        if (Configuration?.EnableTimestamp == true)
            result = $"[当前时间：{DateTime.Now}]{result}";

        return $"{Configuration?.MessageHeader}{result}";
    }

    string OnPokeSend(string message)
    {
        return $"{Configuration?.PokeHeader}{message}";
    }
}
