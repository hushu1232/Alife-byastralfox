using Alife.Function.QChat;
using NUnit.Framework;

namespace Alife.Test.QChat;

[TestFixture]
public class QChatTaskFeedbackFormatterTests
{
    [Test]
    public void FormatUploadProgressNamesGroupAndFileWithoutInternalState()
    {
        string message = QChatTaskFeedbackFormatter.Format(new QChatTaskFeedbackContext(
            QChatTaskFeedbackKind.Progress,
            "group-file-upload",
            "hello_world.c",
            925402131,
            null));

        Assert.Multiple(() =>
        {
            Assert.That(message, Does.Contain("hello_world.c"));
            Assert.That(message, Does.Contain("925402131"));
            Assert.That(message, Does.Contain("\u5728\u4f20"));
            Assert.That(message, Does.Not.Contain("gateway"));
            Assert.That(message, Does.Not.Contain("session"));
        });
    }

    [Test]
    public void FormatUploadSuccessNamesGroupAndFile()
    {
        string message = QChatTaskFeedbackFormatter.Format(new QChatTaskFeedbackContext(
            QChatTaskFeedbackKind.Succeeded,
            "group-file-upload",
            "hello_world.c",
            925402131,
            null));

        Assert.That(message, Is.EqualTo("hello_world.c \u5df2\u4e0a\u4f20\u5230 925402131 \u7fa4\u6587\u4ef6"));
    }

    [Test]
    public void FormatUploadFailureUsesDedicatedFailureWordingWithoutRawDetail()
    {
        string message = QChatTaskFeedbackFormatter.Format(new QChatTaskFeedbackContext(
            QChatTaskFeedbackKind.Failed,
            "group-file-upload",
            "hello_world.c",
            925402131,
            "NapCat upload failed"));

        Assert.Multiple(() =>
        {
            Assert.That(message, Does.Contain("\u6ca1\u4f20\u6210"));
            Assert.That(message, Does.Not.Contain("NapCat upload failed"));
            Assert.That(message, Does.Not.Contain("\u600e\u4e48\u5566"));
        });
    }

    [Test]
    public void FormatPrivateUploadFailureDoesNotCallTargetGroupFile()
    {
        string message = QChatTaskFeedbackFormatter.Format(new QChatTaskFeedbackContext(
            QChatTaskFeedbackKind.Failed,
            "qq.private_file_upload",
            "private.txt",
            456,
            "NapCat private upload failed"));

        Assert.Multiple(() =>
        {
            Assert.That(message, Does.Contain("private.txt"));
            Assert.That(message, Does.Contain("456"));
            Assert.That(message, Does.Contain("\u79c1\u804a\u6587\u4ef6"));
            Assert.That(message, Does.Not.Contain("\u7fa4\u6587\u4ef6"));
            Assert.That(message, Does.Not.Contain("NapCat private upload failed"));
        });
    }

    [Test]
    public void FormatTaskFeedbackForMixuMotherKeepsFileWithoutFailureDetail()
    {
        QChatPersonaFeedbackContext context = new("mixu", QChatSenderRole.PrivateGuest, "\u5988\u5988", "mother");
        string message = QChatTaskFeedbackFormatter.Format(
            new QChatTaskFeedbackContext(
                QChatTaskFeedbackKind.Failed,
                "qq.file_upload",
                "report.txt",
                42,
                "gateway-timeout"),
            context);

        Assert.Multiple(() =>
        {
            Assert.That(message, Does.Contain("\u5988\u5988"));
            Assert.That(message, Does.Contain("report.txt"));
            Assert.That(message, Does.Not.Contain("gateway-timeout"));
        });
    }

    [Test]
    public void FormatUploadUncertainReportsPossibleLateCompletion()
    {
        string message = QChatTaskFeedbackFormatter.Format(new QChatTaskFeedbackContext(
            QChatTaskFeedbackKind.Uncertain,
            "group-file-upload",
            "hello_world.c",
            925402131,
            "\u63a5\u53e3\u8d85\u65f6"));

        Assert.Multiple(() =>
        {
            Assert.That(message, Does.Contain("\u63a5\u53e3\u6ca1\u786e\u8ba4"));
            Assert.That(message, Does.Contain("\u53ef\u80fd"));
            Assert.That(message, Does.Contain("hello_world.c"));
        });
    }
}
