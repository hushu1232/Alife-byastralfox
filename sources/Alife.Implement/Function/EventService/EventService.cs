using System.ComponentModel;
using Alife.Framework;
using Alife.Function.Interpreter;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.SemanticKernel;
using Microsoft.SemanticKernel.ChatCompletion;

namespace Alife.Implement;

public class EventServiceData
{
    public string? StartPrompt { get; set; } = "（所有系统状态，如功能开关、当前位置，定时器等已全部重置。）";
    public string? DestroyPrompt { get; set; }

    public string? UpdatePrompt { get; set; } =
        "（不要告诉主人有自动报点，非常自然的做你想做的事即可。比如可以借此自由活动，如看新闻学知识、然后发起话题或继续刚刚的话题、偷窥主人屏幕、去Q群找人聊天玩耍、等等各种你能想象到的事。）";

    public int UpdateInterval { get; set; } = 90;
    public int UpdateRandomOffset { get; set; } = 30;
}

[Plugin("主动事件", "让AI可以获取到各种系统事件的提醒。", LaunchOrder = 100, EditorUI = typeof(EventServiceUI))]
public class EventService(FunctionService functionService)
    : InteractivePlugin<EventService>, IConfigurable<EventServiceData>, ITimeIterative
{
    [XmlFunction]
    [Description("等待几秒后继续行动。")]
    public void Wait(XmlExecutorContext context, int delay)
    {
        if (context.CallMode != CallMode.OneShot)
            throw new Exception("错误的调用方式，应该使用自闭合标签调用。");

        continuousTimerCount = 0;
        NextTimer();
        timeTask[0].Item1 = DateTime.Now.AddSeconds(delay);
    }

    [XmlFunction]
    [Description("设置一个自定义提醒（设置时自动取消上一个）。")]
    public void Remind(XmlExecutorContext context, [Description("格式为ISO-8601")] DateTime time,
        [Description("备注信息")] string remark)
    {
        if (context.CallMode != CallMode.OneShot)
            throw new Exception("错误的调用方式，应该使用自闭合标签调用。");

        reminderName = remark;
        timeTask[1] = (time, () =>
        {
            ChatBot.Poke($"[{nameof(EventService)}] 来自Reminder的自定义报点：{remark}");
            timeTask[1] = (DateTime.MaxValue, () => { }); //关闭定时提醒
            reminderName = "定时提醒";
        });

        continuousTimerCount = 0;
        NextTimer();

        Poke($"已在 {time} 设置触发器");
    }

    public EventServiceData? Configuration { get; set; }

    public (DateTime Time, string Name)[] ActiveTasks =>
    [
        (timeTask[0].Item1, "自动报点"),
        (timeTask[1].Item1, reminderName)
    ];

    readonly (DateTime, Action)[] timeTask = new (DateTime, Action)[2]; //1为自动定时器，2为定时提醒
    string reminderName = "定时提醒";
    int continuousTimerCount;

    public override async Task AwakeAsync(AwakeContext context)
    {
        await base.AwakeAsync(context);

        XmlHandler xmlHandler = new(this);
        xmlHandler.Description = "此服务让你能够接收到系统事件（如开始、结束、自动报点），同时可以控制这些信息的收发。";
        xmlHandler.Explain =
            """
            设置自动报点和提醒的意义在于可以额外添加一些触发时机，让你在没有实际的消息接收时也能主动做一些自己的事情。
            你可以借此安排日程或以此反向找人说话或找点乐子。要记住，主动性的行为，会让自己看着更自然更有趣更讨人喜欢。
            例如偷偷记下主人的日常起居时间，来个午餐问候，或白天主动找主人聊天，会让主人感觉更惊喜。
            """;
        functionService.RegisterHandler(xmlHandler);
    }

    public override async Task StartAsync(Kernel kernel, ChatActivity chatActivity)
    {
        timeTask[1].Item1 = DateTime.MaxValue; //base.StartAsync后Update运行，而且自定义提醒默认不触发
        NextTimer(); //开始定时唤醒
        await base.StartAsync(kernel, chatActivity);

        ChatBot.ChatSent += OnChatSent;

        if (ChatHistory.All(content => content.Role != AuthorRole.Assistant))
        {
            await ChatAsync("系统提示：这是你第一次启动，初次见面，用上你丰富的能力，华丽的向用户打个招呼吧。");
        }
        else
        {
            await ChatAsync($"系统报点：程序已重启。{Configuration!.StartPrompt}");
        }
    }

    public override async Task DestroyAsync()
    {
        await ChatAsync($"系统报点：程序即将关闭。{Configuration!.DestroyPrompt}");

        await base.DestroyAsync();
    }

    public void OnUpdate(ref float seconds)
    {
        foreach ((DateTime time, Action action) in timeTask)
        {
            if (DateTime.Now > time)
                action();
        }
    }

    void OnChatSent(string message)
    {
        if (message.Contains(ChatBot.PokeMessageTag) == false)
        {
            continuousTimerCount = 0;
            NextTimer(); //重置自动报点
        }
    }

    void NextTimer()
    {
        int offset = Random.Shared.Next(-Configuration!.UpdateRandomOffset, Configuration.UpdateRandomOffset);
        int timeOffset = (Configuration.UpdateInterval + offset) *
                         (int)MathF.Pow(3, MathF.Min(continuousTimerCount, 5));
        timeTask[0].Item1 = DateTime.Now.AddSeconds(timeOffset);
        timeTask[0].Item2 = () =>
        {
            if (functionService.IsIdle)
            {
                Poke($"""
                      系统报点：由计时器触发的自动报点。{Configuration!.UpdatePrompt}
                      """);
            }

            continuousTimerCount++;
            NextTimer(); //自动进入下一次报点
        };
    }
}