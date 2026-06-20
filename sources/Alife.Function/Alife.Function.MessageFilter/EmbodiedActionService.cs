using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Alife.Framework;
using Alife.Function.FunctionCaller;
using Alife.Function.Interpreter;
using Microsoft.SemanticKernel;

namespace Alife.Function.MessageFilter;

[Module(
    "Embodied Action",
    "Provides a unified <act> behavior tag that composes body expression, motion, voice, bubble, and QQ output.",
    defaultCategory: "Alife Official/Interaction",
    LaunchOrder = -80)]
public class EmbodiedActionService(
    IEnumerable<IBodyExpressionSink> bodySinks,
    IEnumerable<IVoiceOutputSink> voiceSinks,
    IEnumerable<IChatOutputSink> chatSinks,
    XmlFunctionCaller? functionCaller = null,
    ILifeEventPublisher? lifeEventPublisher = null) : InteractiveModule<EmbodiedActionService>
    , IContextContributor
{
    public string? LastActionNotice { get; private set; }

    [XmlFunction(FunctionMode.Content, name: "act")]
    [Description("Compose one natural embodied behavior. mode=text|bubble|speak|qchat; qchat requires targetType and targetId.")]
    public async Task Act(
        XmlExecutorContext context,
        [XmlContent] string content,
        string mode = "text",
        string? expression = null,
        string? motion = null,
        string? targetType = null,
        long? targetId = null,
        bool voice = false,
        CancellationToken cancellationToken = default)
    {
        if (context.CallMode != CallMode.Closing)
            return;

        await ExecuteActionAsync(mode, content, expression, motion, targetType, targetId, voice, cancellationToken);
    }

    public async Task ExecuteActionAsync(
        string mode,
        string text,
        string? expression,
        string? motion,
        string? targetType,
        long? targetId,
        bool voice,
        CancellationToken cancellationToken = default)
    {
        LastActionNotice = null;
        string normalizedMode = string.IsNullOrWhiteSpace(mode) ? "text" : mode.Trim().ToLowerInvariant();
        string actionText = text.Trim();

        IBodyExpressionSink? body = bodySinks.FirstOrDefault();
        if (string.IsNullOrWhiteSpace(expression) == false)
        {
            if (body == null) Notice("No body sink is available for expression.");
            else body.PlayExpression(expression.Trim());
        }

        if (string.IsNullOrWhiteSpace(motion) == false)
        {
            if (body == null) Notice("No body sink is available for motion.");
            else body.PlayMotion(motion.Trim());
        }

        switch (normalizedMode)
        {
            case "text":
                PublishLifeEvent(LifeEventKind.Action, $"You performed a text action: {actionText}");
                return;
            case "bubble":
                if (body == null)
                {
                    Notice("No body sink is available for bubble output.");
                    return;
                }
                await body.ShowBubbleAsync(actionText, cancellationToken);
                PublishLifeEvent(LifeEventKind.Action, $"You performed a bubble action: {actionText}");
                return;
            case "speak":
            {
                IVoiceOutputSink? voiceSink = voiceSinks.FirstOrDefault();
                if (voiceSink == null)
                {
                    Notice("No voice sink is available for speech output.");
                    return;
                }
                await voiceSink.SpeakAsync(actionText, cancellationToken);
                PublishLifeEvent(LifeEventKind.Action, $"You performed a speak action: {actionText}");
                return;
            }
            case "qchat":
            {
                IChatOutputSink? chatSink = chatSinks.FirstOrDefault();
                if (chatSink == null)
                {
                    Notice("No chat sink is available for QQ output.");
                    return;
                }
                if (string.IsNullOrWhiteSpace(targetType))
                    throw new InvalidOperationException("targetType is required for qchat actions.");
                if (targetId == null)
                    throw new InvalidOperationException("targetId is required for qchat actions.");
                await chatSink.SendChatAsync(targetType.Trim(), targetId.Value, actionText, voice);
                PublishLifeEvent(LifeEventKind.Action, $"You performed a qchat action to {targetType.Trim()} {targetId.Value}.");
                return;
            }
            default:
                throw new InvalidOperationException($"Unsupported act mode: {mode}");
        }
    }

    public override async Task AwakeAsync(AwakeContext context)
    {
        await base.AwakeAsync(context);
        functionCaller?.RegisterHandler(this);
    }

    public override async Task StartAsync(Kernel kernel, ChatActivity chatActivity)
    {
        await base.StartAsync(kernel, chatActivity);
    }

    public IEnumerable<ContextContribution> GetContextContributions()
    {
        return [
            new ContextContribution(
                "embodied-action",
                """
                Use <act> when one natural behavior should combine body expression, body motion, voice, desk-pet bubble, or QQ output.
                Existing concrete XML tags remain available for precise tool use.

                Examples:
                <act mode="speak" expression="smile" motion="wave">I understand.</act>
                <act mode="bubble" expression="thinking">Let me check that.</act>
                <act mode="qchat" targetType="group" targetId="123456">I saw it.</act>
                """,
                Priority: 700,
                MaxLength: 900)
        ];
    }

    void Notice(string message)
    {
        LastActionNotice = message;
        PublishLifeEvent(LifeEventKind.Action, message);
        if (ChatBot != null!)
            Poke(message);
    }

    void PublishLifeEvent(LifeEventKind kind, string summary)
    {
        lifeEventPublisher?.Publish(new LifeEvent(
            DateTimeOffset.Now,
            kind,
            "EmbodiedAction",
            summary));
    }
}
