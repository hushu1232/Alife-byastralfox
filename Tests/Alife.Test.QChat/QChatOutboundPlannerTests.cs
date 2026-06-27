using Alife.Function.QChat;
using NUnit.Framework;
using System.Threading;
using System.Threading.Tasks;

namespace Alife.Test.QChat;

public class QChatOutboundPlannerTests
{
    [Test]
    public void ShortTextUsesOneSendItem()
    {
        QChatOutboundPlanner planner = new();

        QChatOutboundMessagePlan plan = planner.PlanText("hello");

        Assert.Multiple(() =>
        {
            Assert.That(plan.Items, Has.Count.EqualTo(1));
            Assert.That(plan.Items[0].Kind, Is.EqualTo(QChatOutboundItemKind.Text));
            Assert.That(plan.Items[0].Text, Is.EqualTo("hello"));
            Assert.That(plan.Items[0].MediaId, Is.Null);
        });
    }

    [TestCase("\u95ED\u5634\uFF0C\u5435\u5F97\u5F88\u3002")]
    [TestCase("\u4F60\u53EF\u4EE5\u70E6\u6211\uFF0C\u522B\u70E6\u672F\u3002")]
    [TestCase("\u6EDA\u8FDC\u70B9\uFF0C\u522B\u628A\u6211\u8010\u5FC3\u5F53\u514D\u8D39\u8D44\u6E90\u3002")]
    public void PlanTextKeepsShortAggressiveReplyAsSingleMessage(string text)
    {
        QChatOutboundMessagePlan plan = new QChatOutboundPlanner(maxTextLength: 40).PlanText(text);

        Assert.Multiple(() =>
        {
            Assert.That(plan.Items, Has.Count.EqualTo(1));
            Assert.That(plan.Items[0].Text, Is.EqualTo(text));
        });
    }

    [Test]
    public void PlanTextRemovesStageDirectionPrefixButKeepsNaturalReply()
    {
        QChatOutboundMessagePlan plan = new QChatOutboundPlanner()
            .PlanText("\uFF08\u51B7\u51B7\u770B\u7740\u4F60\uFF09\u5C11\u88C5\u719F\u3002");

        Assert.Multiple(() =>
        {
            Assert.That(plan.Items, Has.Count.EqualTo(1));
            Assert.That(plan.Items[0].Text, Is.EqualTo("\u5C11\u88C5\u719F\u3002"));
        });
    }

    [Test]
    public void PlanTextRemovesPersonaFrameButKeepsNaturalReply()
    {
        string text = "[qchat persona frame]\nspeaker_role=owner\nrecommended_stance=tender\n[/qchat persona frame]\n\u672F\u672F\uFF0C\u6211\u5728\u3002";

        QChatOutboundMessagePlan plan = new QChatOutboundPlanner().PlanText(text);

        Assert.Multiple(() =>
        {
            Assert.That(plan.Items, Has.Count.EqualTo(1));
            Assert.That(plan.Items[0].Text, Is.EqualTo("\u672F\u672F\uFF0C\u6211\u5728\u3002"));
        });
    }

    [Test]
    public void PlanTextDropsPureInternalState()
    {
        QChatOutboundMessagePlan plan = new QChatOutboundPlanner()
            .PlanText("\u5FC3\u7406\u72B6\u6001\uFF1A\u5AC9\u5992");

        Assert.That(plan.Items, Is.Empty);
    }

    [Test]
    public void PlanTextDropsCrossAgentCallMarkup()
    {
        QChatOutboundMessagePlan plan = new QChatOutboundPlanner()
            .PlanText("<call target=\"\u771F\u592E\">\u7761\u4E86\u5417\u771F\u592E</call>");

        Assert.That(plan.Items, Is.Empty);
    }

    [TestCase("\u771F\u592E\uFF0C\u4F60\u90A3\u8FB9\u7684\u70E4\u9C7C\u5403\u5B8C\u6CA1\u6709")]
    [TestCase("\u771F\u592E\u3002")]
    [TestCase("\u54AA\u7EEA\uFF1F")]
    public void PlanTextDropsDirectCrossAgentAddress(string text)
    {
        QChatOutboundMessagePlan plan = new QChatOutboundPlanner().PlanText(text);

        Assert.That(plan.Items, Is.Empty);
    }

    [Test]
    public void PlanTextKeepsNonAddressMentionOfOtherAgentName()
    {
        string text = "\u4F60\u4EEC\u8BF4\u7684\u662F\u771F\u592E\uFF1F";

        QChatOutboundMessagePlan plan = new QChatOutboundPlanner().PlanText(text);

        Assert.Multiple(() =>
        {
            Assert.That(plan.Items, Has.Count.EqualTo(1));
            Assert.That(plan.Items[0].Text, Is.EqualTo(text));
        });
    }

    [Test]
    public void PlanTextPreservesQqImageCqCode()
    {
        string text = "[CQ:image,file=D:/Alife/Runtime/BrowserAgentMedia/a.png]";

        QChatOutboundMessagePlan plan = new QChatOutboundPlanner().PlanText(text);

        Assert.Multiple(() =>
        {
            Assert.That(plan.Items, Has.Count.EqualTo(1));
            Assert.That(plan.Items[0].Text, Is.EqualTo(text));
        });
    }

    [Test]
    public void EmptyTextProducesNoSendItems()
    {
        QChatOutboundPlanner planner = new();

        Assert.Multiple(() =>
        {
            Assert.That(planner.PlanText(null).Items, Is.Empty);
            Assert.That(planner.PlanText("").Items, Is.Empty);
            Assert.That(planner.PlanText("   \r\n\t").Items, Is.Empty);
        });
    }

    [Test]
    public void LongParagraphSplitsByBlankParagraphNotSentenceFragments()
    {
        QChatOutboundPlanner planner = new(maxTextLength: 45);
        string text = "First sentence. Second sentence.\n\nSecond paragraph.\n\nThird paragraph.";

        QChatOutboundMessagePlan plan = planner.PlanText(text);

        Assert.Multiple(() =>
        {
            Assert.That(plan.Items, Has.Count.EqualTo(2));
            Assert.That(plan.Items[0].Text, Is.EqualTo("First sentence. Second sentence."));
            Assert.That(plan.Items[1].Text, Is.EqualTo("Second paragraph.\n\nThird paragraph."));
        });
    }

    [Test]
    public void LongSingleParagraphDoesNotSplitJustBecauseItIsLong()
    {
        QChatOutboundPlanner planner = new(maxTextLength: 10);
        string text = "This is one long paragraph with no safe blank-line boundary, so it stays together.";

        QChatOutboundMessagePlan plan = planner.PlanText(text);

        Assert.Multiple(() =>
        {
            Assert.That(plan.Items, Has.Count.EqualTo(1));
            Assert.That(plan.Items[0].Text, Is.EqualTo(text));
        });
    }

    [Test]
    public void CodeBlockRemainsIntact()
    {
        QChatOutboundPlanner planner = new(maxTextLength: 40);
        string codeBlock = "```csharp\nConsole.WriteLine(\"a\");\n\nConsole.WriteLine(\"b\");\n```";
        string text = $"Intro paragraph.\n\n{codeBlock}\n\nAfter paragraph.";

        QChatOutboundMessagePlan plan = planner.PlanText(text);

        Assert.Multiple(() =>
        {
            Assert.That(plan.Items, Has.Count.EqualTo(3));
            Assert.That(plan.Items[0].Text, Is.EqualTo("Intro paragraph."));
            Assert.That(plan.Items[1].Text, Is.EqualTo(codeBlock));
            Assert.That(plan.Items[2].Text, Is.EqualTo("After paragraph."));
        });
    }

    [Test]
    public void ListBlockDoesNotSplitMidListItem()
    {
        QChatOutboundPlanner planner = new(maxTextLength: 35);
        string listBlock = "- first item has more detail\n- second item has more detail";
        string text = $"Lead paragraph.\n\n{listBlock}\n\nTail paragraph.";

        QChatOutboundMessagePlan plan = planner.PlanText(text);

        Assert.Multiple(() =>
        {
            Assert.That(plan.Items, Has.Count.EqualTo(3));
            Assert.That(plan.Items[0].Text, Is.EqualTo("Lead paragraph."));
            Assert.That(plan.Items[1].Text, Is.EqualTo(listBlock));
            Assert.That(plan.Items[2].Text, Is.EqualTo("Tail paragraph."));
        });
    }

    [Test]
    public void ListBlockKeepsBlankContinuationParagraphWithListItem()
    {
        QChatOutboundPlanner planner = new(maxTextLength: 35);
        string listBlock = "- first item\n\n  continuation for first item\n- second item";
        string text = $"Lead paragraph.\n\n{listBlock}\n\nTail paragraph.";

        QChatOutboundMessagePlan plan = planner.PlanText(text);

        Assert.Multiple(() =>
        {
            Assert.That(plan.Items, Has.Count.EqualTo(3));
            Assert.That(plan.Items[0].Text, Is.EqualTo("Lead paragraph."));
            Assert.That(plan.Items[1].Text, Is.EqualTo(listBlock));
            Assert.That(plan.Items[2].Text, Is.EqualTo("Tail paragraph."));
        });
    }

    [Test]
    public async Task DispatcherSkipsEmptyTextItemsAndPreservesOrder()
    {
        QChatOutboundDispatcher dispatcher = new();
        QChatOutboundMessagePlan plan = new(
        [
            new QChatOutboundMessageItem(QChatOutboundItemKind.Text, "first"),
            new QChatOutboundMessageItem(QChatOutboundItemKind.Text, " "),
            new QChatOutboundMessageItem(QChatOutboundItemKind.Image, "", "image-1"),
            new QChatOutboundMessageItem(QChatOutboundItemKind.Text, "second")
        ]);
        List<QChatOutboundMessageItem> sent = [];

        await dispatcher.DispatchAsync(
            plan,
            (item, _) =>
            {
                sent.Add(item);
                return Task.CompletedTask;
            });

        Assert.Multiple(() =>
        {
            Assert.That(sent, Has.Count.EqualTo(3));
            Assert.That(sent[0].Text, Is.EqualTo("first"));
            Assert.That(sent[1].Kind, Is.EqualTo(QChatOutboundItemKind.Image));
            Assert.That(sent[1].MediaId, Is.EqualTo("image-1"));
            Assert.That(sent[2].Text, Is.EqualTo("second"));
        });
    }

    [Test]
    public void DispatcherThrowsForNullPlanItems()
    {
        QChatOutboundDispatcher dispatcher = new();
        QChatOutboundMessagePlan plan = new(null!);

        Assert.That(
            async () => await dispatcher.DispatchAsync(plan, (_, _) => Task.CompletedTask),
            Throws.TypeOf<ArgumentNullException>());
    }

    [Test]
    public async Task DispatcherSnapshotsItemsBeforeDispatch()
    {
        QChatOutboundDispatcher dispatcher = new();
        List<QChatOutboundMessageItem> items =
        [
            new QChatOutboundMessageItem(QChatOutboundItemKind.Text, "first"),
            new QChatOutboundMessageItem(QChatOutboundItemKind.Text, "second")
        ];
        QChatOutboundMessagePlan plan = new(items);
        List<string> sent = [];

        await dispatcher.DispatchAsync(
            plan,
            (item, _) =>
            {
                sent.Add(item.Text);
                if (item.Text == "first")
                {
                    items.RemoveAt(1);
                    items.Add(new QChatOutboundMessageItem(QChatOutboundItemKind.Text, "mutated"));
                }

                return Task.CompletedTask;
            });

        Assert.That(sent, Is.EqualTo(new[] { "first", "second" }));
    }

    [Test]
    public void DispatcherThrowsForNullArguments()
    {
        QChatOutboundDispatcher dispatcher = new();
        QChatOutboundMessagePlan plan = new([]);

        Assert.Multiple(() =>
        {
            Assert.That(
                async () => await dispatcher.DispatchAsync(null!, (_, _) => Task.CompletedTask),
                Throws.TypeOf<ArgumentNullException>());
            Assert.That(
                async () => await dispatcher.DispatchAsync(plan, null!),
                Throws.TypeOf<ArgumentNullException>());
        });
    }

    [Test]
    public async Task DispatcherPassesCancellationToken()
    {
        QChatOutboundDispatcher dispatcher = new();
        QChatOutboundMessagePlan plan = new(
        [
            new QChatOutboundMessageItem(QChatOutboundItemKind.Text, "hello")
        ]);
        using CancellationTokenSource source = new();
        CancellationToken observedToken = default;

        await dispatcher.DispatchAsync(
            plan,
            (_, cancellationToken) =>
            {
                observedToken = cancellationToken;
                return Task.CompletedTask;
            },
            source.Token);

        Assert.That(observedToken, Is.EqualTo(source.Token));
    }

    [Test]
    public void DispatcherDoesNotSendWhenTokenAlreadyCanceled()
    {
        QChatOutboundDispatcher dispatcher = new();
        QChatOutboundMessagePlan plan = new(
        [
            new QChatOutboundMessageItem(QChatOutboundItemKind.Text, "first")
        ]);
        using CancellationTokenSource source = new();
        source.Cancel();
        int sends = 0;

        Assert.Multiple(() =>
        {
            Assert.That(
                async () => await dispatcher.DispatchAsync(
                    plan,
                    (_, _) =>
                    {
                        sends++;
                        return Task.CompletedTask;
                    },
                    source.Token),
                Throws.TypeOf<OperationCanceledException>());
            Assert.That(sends, Is.Zero);
        });
    }

    [Test]
    public void DispatcherStopsAfterCancellationDuringDispatch()
    {
        QChatOutboundDispatcher dispatcher = new();
        QChatOutboundMessagePlan plan = new(
        [
            new QChatOutboundMessageItem(QChatOutboundItemKind.Text, "first"),
            new QChatOutboundMessageItem(QChatOutboundItemKind.Text, "second")
        ]);
        using CancellationTokenSource source = new();
        List<string> sent = [];

        Assert.Multiple(() =>
        {
            Assert.That(
                async () => await dispatcher.DispatchAsync(
                    plan,
                    (item, _) =>
                    {
                        sent.Add(item.Text);
                        source.Cancel();
                        return Task.CompletedTask;
                    },
                    source.Token),
                Throws.TypeOf<OperationCanceledException>());
            Assert.That(sent, Is.EqualTo(new[] { "first" }));
        });
    }
}
