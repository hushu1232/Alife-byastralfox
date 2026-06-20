using System;
using System.ComponentModel;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Alife.Platform;
using Autofac;
using Alife.Framework;
using Alife.Function.Emotion;
using Alife.Function.FunctionCaller;
using Alife.Function.Interpreter;
using Microsoft.SemanticKernel;

namespace Alife.Function.DeskPet;

[Module("桌宠交互", @"将Live2D桌宠接入AI系统，实现表现力同步和互动反馈（仅支持Cubism 3及以上版本的live2D模型）
可选模型下载地址：
https://github.com/imuncle/live2d",
    defaultCategory: "Alife 官方/交互方式",
    EditorUI = typeof(DeskPetServiceUI))]
public class DeskPetService(
    XmlFunctionCaller functionService,
    IDeskPetRuntime? petRuntime = null,
    ILifeEventPublisher? lifeEventPublisher = null) : InteractiveModule<DeskPetService>, IAsyncDisposable, IConfigurable<DeskPetServiceConfig>, IEmbodiedCapability, IBodyExpressionSink, IModuleHealthReporter
{
    [XmlFunction(FunctionMode.Content)]
    [Description("显示一段气泡文本")]
    public async Task Speak(XmlExecutorContext context, [XmlContent] string content, CancellationToken cancellationToken)
    {
        switch (context.CallMode)
        {
            case CallMode.Closing:
            {
                if (DateTimeOffset.Now.ToUnixTimeMilliseconds() < lastBubbleEndTime)
                    await Task.Delay(TimeSpan.FromMilliseconds(lastBubbleEndTime - DateTimeOffset.Now.ToUnixTimeMilliseconds()));
                client!.HideBubble();
                break;
            }
            case CallMode.Content:
            {
                await ShowBubbleAsync(content, cancellationToken);
                break;
            }
        }
    }

    public void PlayExpression(string option) => Expression(option);

    public void PlayMotion(string option) => Motion(option);

    public async Task ShowBubbleAsync(string text, CancellationToken cancellationToken = default)
    {
        string content = text.Trim();
        if (string.IsNullOrWhiteSpace(content))
            return;
        if (cancellationToken.IsCancellationRequested)
            return;

        if (DateTimeOffset.Now.ToUnixTimeMilliseconds() < lastBubbleEndTime)
            await Task.Delay(
                TimeSpan.FromMilliseconds(lastBubbleEndTime - DateTimeOffset.Now.ToUnixTimeMilliseconds()),
                cancellationToken);
        client!.ShowBubble(content);
        lastBubbleEndTime = DateTimeOffset.Now.ToUnixTimeMilliseconds() + content.Length * 150;
        PublishLifeEvent($"You displayed a desk-pet bubble: {content}");
    }

    [XmlFunction(FunctionMode.OneShot)]
    [Description("表演一个表情（具体选项见附加说明）")]
    public void Expression(string option)
    {
        option = option.Trim();
        if (string.IsNullOrWhiteSpace(option))
            return;
        if (client!.SupportedExpressions.Contains(option) == false)
            throw new Exception("选项不存在");

        client!.PlayExpression(option);
        PublishLifeEvent($"Your desk-pet body played expression: {option}.");
    }

    [XmlFunction(FunctionMode.OneShot)]
    [Description("表演一个动作（具体选项见附加说明）")]
    public void Motion(string option)
    {
        option = option.Trim();
        if (string.IsNullOrWhiteSpace(option))
            return;
        if (client!.SupportedMotions.TryGetValue(option, out (string Group, int Index) motion) == false)
            throw new Exception("选项不存在");

        client.PlayMotion(motion.Group, motion.Index);
        PublishLifeEvent($"Your desk-pet body played motion: {option}.");
    }

    [XmlFunction(FunctionMode.OneShot)]
    [Description("获取当前屏幕位置（使用后需等待结果返回）")]
    public async Task Position()
    {
        try
        {
            (double x, double y) = await client!.GetPositionAsync();
            Poke($"当前位置: x={x}, y={y}");
        }
        catch (TimeoutException)
        {
            Poke("获取坐标超时");
        }
    }

    [XmlFunction(FunctionMode.OneShot)]
    [Description("在屏幕上进行相对移动（注意！该移动方式为相对位置移动，使用前最好先确认当前位置）")]
    public async Task Move(double x = 0, double y = 0, int duration = 1000)
    {
        await client!.MoveAsync(x, y, duration);
        (x, y) = await client!.GetPositionAsync();
        PublishLifeEvent($"Your desk-pet body moved to x={x}, y={y}.");
        Poke($"移动成功，当前位置: x={x}, y={y}");
    }


    protected override string ChatTextFilter(string text)
    {
        return $"""
                {text}
                (请使用DeskPet功能响应)
                """;
    }


    public DeskPetServiceConfig? Configuration { get; set; }

    public string Name => "Desk pet body";
    public EmbodiedCapabilityKind Kind => EmbodiedCapabilityKind.Body;
    public string SelfDescription => "Your visible Live2D body. It can show expressions, play motions, display speech bubbles, receive touch interactions, and reflect your current activity.";

    public string? GetCurrentState()
    {
        string modelName = string.IsNullOrWhiteSpace(Configuration?.ModelName) ? "Mao" : Configuration!.ModelName;
        string connection = client == null ? "not connected yet" : "connected";
        return $"Model: {modelName}; client: {connection}.";
    }

    public ModuleHealth GetHealth()
    {
        if (isDisposed)
            return new ModuleHealth("DeskPet", ModuleHealthStatus.Unavailable, "Desk pet service has been disposed.");
        if (client == null)
            return new ModuleHealth("DeskPet", ModuleHealthStatus.Degraded, "Desk pet client is not initialized yet.");

        return new ModuleHealth(
            "DeskPet",
            ModuleHealthStatus.Healthy,
            $"Desk pet client is initialized; expressions: {client.SupportedExpressions.Count()}; motions: {client.SupportedMotions.Count}.");
    }

    IDeskPetRuntime? client;
    EmotionLive2DParameterDriver? emotionParameterDriver;
    CancellationTokenSource? emotionSyncCancellation;
    Task? emotionSyncTask;
    long lastBubbleEndTime;

    public override async Task AwakeAsync(AwakeContext context)
    {
        await base.AwakeAsync(context);

        string? modelName = Configuration?.ModelName;
        if (string.IsNullOrWhiteSpace(modelName))
            modelName = "Mao";
        client = petRuntime ?? new PetServer(modelName);
        string supportedExpressionsDescription = string.Join(", ", client.SupportedExpressions);
        if (string.IsNullOrEmpty(supportedExpressionsDescription)) supportedExpressionsDescription = $"当前不支持<{nameof(Expression)}>功能";
        string supportedMotionsDescription = string.Join(", ", client.SupportedMotions.Keys);
        if (string.IsNullOrEmpty(supportedMotionsDescription)) supportedMotionsDescription = $"当前不支持<{nameof(Motion)}>功能";

        XmlHandler xmlHandler = new(this);
        functionService?.RegisterHandlerWithoutDocument(xmlHandler);

        Prompt($"""
                此服务让你获得一副交互性的Live2D身体。这是你主要的对外输出表情动作等外观信息的工具，需要积极使用。

                ## 支持工具
                {xmlHandler.FunctionDocument()}

                ## 工具选项
                - 支持的 {nameof(Expression)} 选项：{supportedExpressionsDescription}
                - 支持的 {nameof(Motion)} 选项：{supportedMotionsDescription}

                ## 其他信息
                - 当前屏幕分辨率：{AlifePlatform.GetResolution()}
                """);
    }

    public override async Task StartAsync(Kernel kernel, ChatActivity chatActivity)
    {
        await base.StartAsync(kernel, chatActivity);

        await client!.WaitReadyAsync();
        client.OnInput += Chat;
        client.OnInteracted += text => Chat("交互：" + text);
        TryStartEmotionParameterSync(chatActivity);

        // 启动状态轮询
        _ = UpdateStatusLoop(chatActivity.ChatBot);
    }

    void TryStartEmotionParameterSync(ChatActivity chatActivity)
    {
        if (Configuration?.EnableEmotionParameterSync == false)
            return;

        if (chatActivity.ModuleService.TryResolve(out PADEmotionEngine? emotionEngine) == false)
            return;

        emotionParameterDriver = new EmotionLive2DParameterDriver(emotionEngine, client!);
        emotionSyncCancellation = new CancellationTokenSource();
        emotionSyncTask = UpdateEmotionParameterLoop(emotionSyncCancellation.Token);
    }

    async Task UpdateEmotionParameterLoop(CancellationToken cancellationToken)
    {
        int intervalMilliseconds = Math.Max(50, Configuration?.EmotionSyncIntervalMilliseconds ?? 250);
        try
        {
            while (!isDisposed && cancellationToken.IsCancellationRequested == false)
            {
                emotionParameterDriver?.PushCurrentState();
                await Task.Delay(intervalMilliseconds, cancellationToken);
            }
        }
        catch (OperationCanceledException) {}
        catch (Exception e)
        {
            AlifeTerminal.LogError(e.ToString());
        }
    }

    async Task UpdateStatusLoop(ChatBot chatBot)
    {
        bool lastStatus = false;
        while (!isDisposed)
        {
            try
            {
                bool currentStatus = chatBot.IsChatting;
                if (currentStatus != lastStatus)
                {
                    lastStatus = currentStatus;
                    client?.SendStatus(currentStatus);
                }
            }
            catch (Exception e)
            {
                AlifeTerminal.LogError(e.ToString());
            }

            await Task.Delay(250);
        }
    }

    void PublishLifeEvent(string summary)
    {
        lifeEventPublisher?.Publish(new LifeEvent(
            DateTimeOffset.Now,
            LifeEventKind.Body,
            "DeskPet",
            summary));
    }

    public async ValueTask DisposeAsync()
    {
        isDisposed = true;
        emotionSyncCancellation?.Cancel();
        if (emotionSyncTask != null)
            await emotionSyncTask;
        emotionSyncCancellation?.Dispose();
        if (client != null)
            await client.DisposeAsync();
    }

    bool isDisposed;
}
