using Alife.Function.QChat;
using NUnit.Framework;

namespace Alife.Test.QChat;

[TestFixture]
public class QChatDeterministicTaskRunnerTests
{
    [Test]
    public async Task ExecuteAsyncReturnsSucceededWhenActionCompletes()
    {
        QChatDeterministicTaskResult result = await QChatDeterministicTaskRunner.ExecuteAsync(
            new QChatDeterministicTaskContext(
                "qq.file_upload",
                "hello_world.c",
                OneBotMessageType.Group,
                925402131),
            () => Task.CompletedTask);

        Assert.Multiple(() =>
        {
            Assert.That(result.Status, Is.EqualTo(QChatDeterministicTaskStatus.Succeeded));
            Assert.That(result.Succeeded, Is.True);
            Assert.That(result.Error, Is.Null);
            Assert.That(result.Context.FileName, Is.EqualTo("hello_world.c"));
        });
    }

    [Test]
    public async Task ExecuteAsyncReturnsFailedWhenActionThrows()
    {
        QChatDeterministicTaskResult result = await QChatDeterministicTaskRunner.ExecuteAsync(
            new QChatDeterministicTaskContext(
                "qq.file_upload",
                "hello_world.c",
                OneBotMessageType.Group,
                925402131),
            () => throw new InvalidOperationException("NapCat upload failed"));

        Assert.Multiple(() =>
        {
            Assert.That(result.Status, Is.EqualTo(QChatDeterministicTaskStatus.Failed));
            Assert.That(result.Succeeded, Is.False);
            Assert.That(result.Error, Does.Contain("NapCat upload failed"));
            Assert.That(result.Exception, Is.TypeOf<InvalidOperationException>());
        });
    }

    [Test]
    public void ExecuteAsyncRejectsNullAction()
    {
        ArgumentNullException? exception = Assert.ThrowsAsync<ArgumentNullException>(async () =>
            await QChatDeterministicTaskRunner.ExecuteAsync(
                new QChatDeterministicTaskContext(
                    "qq.file_upload",
                    "hello_world.c",
                    OneBotMessageType.Group,
                    925402131),
                null!));

        Assert.That(exception!.ParamName, Is.EqualTo("action"));
    }

    [Test]
    public void ExecuteAsyncRejectsNullContext()
    {
        ArgumentNullException? exception = Assert.ThrowsAsync<ArgumentNullException>(async () =>
            await QChatDeterministicTaskRunner.ExecuteAsync(
                null!,
                () => Task.CompletedTask));

        Assert.That(exception!.ParamName, Is.EqualTo("context"));
    }
}
