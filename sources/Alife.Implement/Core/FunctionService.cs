using System.Text;
using Alife.Framework;
using Alife.Function.Interpreter;
using Microsoft.SemanticKernel;

namespace Alife.Implement;

[Plugin("函数调用", "为AI增加一种基于Xml的流式函数执行功能，实现快速实时的交互能力。", launchOrder: -1000)]
public class FunctionService : InteractivePlugin<FunctionService>
{
    public bool IsIdle => executor.IsIdle;

    public void RegisterHandler(XmlHandler handler, params string[] plainAreas)
    {
        handlerTable.Register(handler);
        this.plainAreas.AddRange(plainAreas);
    }

    public void UnregisterHandler(XmlHandler handler) => handlerTable.Unregister(handler);

    public void RegisterHandler(object handler, params string[] plainAreas)
    {
        handlerTable.Register(new XmlHandler(handler));
        this.plainAreas.AddRange(plainAreas);
    }

    public void UnregisterHandler(object handler) => handlerTable.Unregister(new XmlHandler(handler));

    readonly XmlHandlerTable handlerTable = new();
    readonly List<string> plainAreas = new();
    XmlStreamParser parser = null!;
    XmlStreamExecutor executor = null!;

    public override async Task StartAsync(Kernel kernel, ChatActivity chatActivity)
    {
        await base.StartAsync(kernel, chatActivity);

        //创建xml解析执行器等
        parser = new XmlStreamParser(plainAreas.ToArray());
        executor = new XmlStreamExecutor(
            parser,
            handlerTable,
            ["，", "。", "！", "？", "......", "~"],
            minBreakingLength: 9
        );
        parser.Error += OnError;
        executor.Error += OnError;

        //注入使用说明
        string prompt = $"""
                         你拥有输出特定的xml标签来实现功能调用的能力（虽然你也可以直接输出普通文本，但那样通常无法被外界看到或听到）。
                         注意！由于xml的解释器的存在，【" | & | < | >】之类的xml符号都无法直接输出，你需要使用xml转义的方式【&quot; | &amp; | &lt; | &gt;】来输出尖括号。如果你在调用其他标签功能时，出现了因Xml符号中断导致的异常，你可以尝试将其转义输出来解决。

                         ## 目前支持的函数调用和说明文档
                         {handlerTable.Document()}

                         ## 使用时可以参考如下示例（注意！确保你要调用的函数在上面的说明文档中）
                         ```text
                         <say> <!-- 根据情况选择输出方式，比如这里选择用语音方式输出 -->
                         主人你看我~
                         可以一边跳舞
                         <mtn /> <!-- 标签可以嵌套，如say中嵌套mtn来实现边说话边做动作 -->
                         一边说话噢。
                         另外我还可以通过‘左尖括号python右尖括号’执行脚本呢！ <!-- 通过用代词描述‘左尖括号、右尖括号’来避免输出xml符号 -->
                         </say>
                         <python> <!-- 因为python执行需要时间，在结尾调用比较合适。 -->
                         print('Hello World!')
                         <python>
                         ```   
                         """;

        chatActivity.ChatBot.ChatHistory.AddSystemMessage(prompt);
        chatActivity.ChatBot.ChatReceived += OnChatReceived;
        chatActivity.ChatBot.ChatSent += OnChatSent;
        chatActivity.ChatBot.ChatOver += OnChatOver;
    }

    public override async Task DestroyAsync()
    {
        await Task.Run(async () =>
        {
            while (executor.IsIdle == false)
                await Task.Yield();
        });
        await executor.DisposeAsync();

        await base.DestroyAsync();
    }

    void OnChatSent(string _)
    {
        executor.Reset();
    }

    void OnChatOver()
    {
        executor.Flush();
    }

    void OnChatReceived(string obj)
    {
        executor.Feed(obj);
    }

    void OnError(string tag, Exception exception)
    {
        Poke($"执行{tag}标签出错：{exception.Message}");
    }
}