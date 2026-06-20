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
