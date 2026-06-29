using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Alife.Framework;
using Alife.Function.Interpreter;
using Microsoft.Extensions.Logging;
using Microsoft.SemanticKernel;

namespace Alife.Function.FunctionCaller;

public enum DocumentMode
{
    Not,
    Implicit,
    Explicit,
}

[Module("Xml函数执行器", "提供一种Xml函数调用框架，可以将注册其中的函数，暴露给AI，并指导其用Xml标签调用。",
    defaultCategory: "astralfox-alife/功能底座",
    launchOrder: -1000)]
public class XmlFunctionCaller(ILogger<XmlFunctionCaller> logger) : InteractiveModule<XmlFunctionCaller>
{
    public bool IsIdle => executor.IsInactive;
    public XmlFunctionExecutionPolicy ExecutionPolicy => handlerTable.ExecutionPolicy;

    public ToolRouteDecision RouteCurrentTurn(string utterance, ToolRouteState state)
    {
        handlerTable.ExecutionPolicy.SetGovernedToolNames(toolRouter.ToolNames);
        ToolRouteDecision route = toolRouter.Route(utterance, state);
        handlerTable.ExecutionPolicy.CurrentRoute = route;
        return route;
    }

    public string BuildRoutedFunctionGuide(ToolRouteDecision route)
    {
        ArgumentNullException.ThrowIfNull(route);

        string documents = handlerTable.Document(function => route.Allows(function.Name));
        if (string.IsNullOrWhiteSpace(documents))
            return string.Empty;

        return $"""
               Tool Broker route: {route.Intent}
               Reason: {route.Reason}
               Allowed XML tools for this turn:
               {documents}
               """;
    }

    public ToolRouteState CreateToolRouteState(bool isOwner, bool isPrivateChat, bool isTrustedRuntime = true)
    {
        lock (dataAgentRouteGate)
        {
            return new ToolRouteState(
                activeDataAgentSessionId,
                activeDataAgentSessionStatus,
                isOwner,
                isPrivateChat,
                isTrustedRuntime);
        }
    }

    public IDisposable UseToolRouteState(ToolRouteState state)
    {
        ToolRouteState? previous = scopedToolRouteState.Value;
        scopedToolRouteState.Value = state;
        return new ToolRouteStateScope(scopedToolRouteState, previous);
    }

    public void UpdateDataAgentAnalysisRouteSessionFromContext(string context)
    {
        if (string.IsNullOrWhiteSpace(context))
            return;

        string? sessionId = ReadContextValue(context, "session_id=");
        string? status = ReadContextValue(context, "status=");
        if (string.IsNullOrWhiteSpace(sessionId) || string.IsNullOrWhiteSpace(status))
            return;

        lock (dataAgentRouteGate)
        {
            activeDataAgentSessionStatus = status.Trim();
            activeDataAgentSessionId = ToolRouteState.IsLiveDataAgentAnalysisStatus(status)
                ? sessionId.Trim()
                : string.Empty;
        }
    }
    public void RegisterHandlerWithoutDocument(XmlHandler handler, params string[] plainAreas)
    {
        handlerTable.Register(handler);
        this.plainAreas.AddRange(plainAreas);
    }
    public void RegisterHandler(XmlHandler handler, params string[] plainAreas)
    {
        RegisterHandler(handler, DocumentMode.Explicit, plainAreas);
    }
    public void RegisterHandler(XmlHandler handler, DocumentMode documentMode, params string[] plainAreas)
    {
        handlerTable.Register(handler);
        this.plainAreas.AddRange(plainAreas);
        switch (documentMode)
        {
            case DocumentMode.Not:
                break;
            case DocumentMode.Implicit:
                if (string.IsNullOrWhiteSpace(handler.Name))
                    throw new InvalidOperationException("Implicit XmlHandler requires a name.");
                implicitHandlers.Add(handler);
                AddImplicitTrigger(handler);
                break;
            case DocumentMode.Explicit:
                explicitHandlers.Add(handler);
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(documentMode), documentMode, null);
        }
    }
    public void RegisterHandler(object handler, params string[] plainAreas)
    {
        RegisterHandler(new XmlHandler(handler), plainAreas);
    }
    public void UnregisterHandler(XmlHandler handler)
    {
        handlerTable.Unregister(handler);
    }
    public void UnregisterHandler(object handler)
    {
        UnregisterHandler(new XmlHandler(handler));
    }

    readonly XmlHandlerTable handlerTable = new();
    readonly ToolCapabilityRouter toolRouter = ToolCapabilityRouter.CreateDefault();
    readonly AsyncLocal<ToolRouteState?> scopedToolRouteState = new();
    readonly object dataAgentRouteGate = new();
    string activeDataAgentSessionId = string.Empty;
    string activeDataAgentSessionStatus = string.Empty;
    readonly List<string> plainAreas = new();
    XmlStreamParser parser = null!;
    XmlStreamExecutor executor = null!;
    readonly List<XmlHandler> explicitHandlers = new();
    readonly List<XmlHandler> implicitHandlers = new();

    public bool CanHandleFunction(string name) => handlerTable.ContainsFunction(name);

    public string BuildFunctionGuide()
    {
        string explicitDocuments = string.Join("\n", explicitHandlers.Select(handler => handler.Document()));
        string implicitDocuments = string.Join("\n", implicitHandlers.Select(GetImplicitDocument));
        return $"""
                ## 可用函数

                ### 显式服务

                {explicitDocuments}

                ### 隐式服务

                有些服务是渐进式加载的，你需要显式阅读它们的文档，再学习如何使用。读取隐式服务文档时，直接输出对应 XML 标签即可。

                {implicitDocuments}

                上面这些标签都是开启隐式服务文档的入口。根据实际情况主动查阅它们，很多能力可能藏在其中。
                """;
    }

    static string GetImplicitDocument(XmlHandler handler)
    {
        return $"- <{handler.Name!.ToLowerInvariant()} /> : {handler.Description}";
    }

    void AddImplicitTrigger(XmlHandler source)
    {
        XmlHandler trigger = new()
        {
            Name = source.Name + "_Trigger",
        };
        trigger.Functions.Add(new XmlFunction
        {
            Name = source.Name!.ToLowerInvariant(),
            Invoker = (_, _) =>
            {
                Poke(source.Document());
                return Task.CompletedTask;
            }
        });
        handlerTable.Register(trigger);
    }

    public override async Task StartAsync(Kernel kernel, ChatActivity chatActivity)
    {
        await base.StartAsync(kernel, chatActivity);

        //创建xml解析执行器等
        parser = new XmlStreamParser(plainAreas.ToArray());
        executor = new XmlStreamExecutor(
            parser,
            handlerTable,
            ["，", "。", "！", "？", "......", "~", "…"],
            minBreakingLength: 9
        );
        handlerTable.ExecutionPolicy.ResetTurnBudget();
        handlerTable.ExecutionPolicy.SetGovernedToolNames(toolRouter.ToolNames);
        chatActivity.ChatBot.ChatSend += OnChatSend;
        parser.Error += OnError;
        executor.Error += OnError;

        chatActivity.ChatBot.ChatReceived += OnChatReceived;
        chatActivity.ChatBot.ChatSent += OnChatSent;

        Prompt($"""
                默认情况下你仅支持输出普通文本，但由于各种插件功能服务的存在，使得你还拥有通过输出特定的xml标签(<>)执行功能调用的能力。

                ## 可用函数(不一定全，具体要看其他功能服务的说明)

                {BuildFunctionGuide()}

                ## 使用提示
                1. 由于xml的解释器的存在，【" | & | < | >】之类的xml符号都无法直接输出，你需要使用xml转义的方式【&quot; | &amp; | &lt; | &gt;】来输出尖括号。
                2. xml调用方式非常自由，允许你进行嵌套，或一次使用多条。
                3. 很多xml函数拥有调用后返回结果的功能，因此你可以通过多轮对话解决事情（如先调用一下获取手册，然后等到收到结果后，再决定下一步的操作）

                ## 使用示例
                当你的函数足够丰富后，你可以尝试用如下的方式使用他们，这是官方最佳示例：
                ```
                (可选，未被标签包裹的文字，用户看不到，所以可以在此实现空消息、自言自语、思考等动作)
                <speak> <!-- 默认采用语音方式对外输出，并在文本中穿插表情动作，来实现动态的交互效果 -->
                主人你看我画的好不好看，<expression option="开心" />今天特意给你画的噢！<motion option="摆摆手"/>
                看你每天那么累，给你打打气。
                </speak>
                <python> <!-- 因为python执行需要时间，在结尾调用比较合适。 -->
                show('cheer.png')
                <python>
                ```   
                """);
    }

    public override async Task DestroyAsync()
    {
        if (ChatBot != null)
            ChatBot.ChatSend -= OnChatSend;

        await executor.WaitToInactive();
        await executor.DisposeAsync();

        await base.DestroyAsync();
    }

    string OnChatSend(string message)
    {
        ToolRouteState state = scopedToolRouteState.Value ?? ToolRouteState.Empty;
        ToolRouteDecision route = RouteCurrentTurn(message, state);
        string routedGuide = BuildRoutedFunctionGuide(route);
        if (string.IsNullOrWhiteSpace(routedGuide))
            return message;

        return $"""
               {message}

               [tool_route_context]
               {routedGuide}
               [/tool_route_context]
               """;
    }

    async void OnChatSent(string _)
    {
        try
        {
            await ChatBot.RequestChatAsync();
            try
            {
                executor.Flush();
                await executor.WaitToInactive(ChatBot.ChatBreakTokenSource.Token);
            }
            catch (OperationCanceledException)
            {
                await executor.CancelAsync();
            }
            finally
            {
                ChatBot.ReleaseChat();
                handlerTable.ExecutionPolicy.ResetTurnBudget();
            }
        }
        catch (OperationCanceledException) {}
        catch (Exception e)
        {
            logger.LogError(e, "处理聊天发送事件失败");
        }
    }

    void OnChatReceived(string obj)
    {
        executor.Feed(obj);
    }

    void OnError(string tag, Exception exception)
    {
        Poke($"执行{tag}标签出错：{exception.Message}");
        logger.LogWarning(exception, $"执行{tag}标签出错");
    }

    static string? ReadContextValue(string context, string prefix)
    {
        foreach (string line in context.Split('\n'))
        {
            string trimmed = line.Trim();
            if (trimmed.StartsWith(prefix, StringComparison.Ordinal))
                return trimmed[prefix.Length..].Trim();
        }

        return null;
    }

    sealed class ToolRouteStateScope(AsyncLocal<ToolRouteState?> target, ToolRouteState? previous) : IDisposable
    {
        bool disposed;

        public void Dispose()
        {
            if (disposed)
                return;

            target.Value = previous;
            disposed = true;
        }
    }
}
