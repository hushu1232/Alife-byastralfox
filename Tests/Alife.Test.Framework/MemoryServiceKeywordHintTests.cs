using Alife.Function.FunctionCaller;
using Alife.Function.Memory;
using Microsoft.Extensions.Logging.Abstractions;

namespace Alife.Test.Framework;

public class MemoryServiceKeywordHintTests
{
    [Test]
    public void AppendMemoryHintIfNeeded_AddsSafeRecallHintForConfiguredKeyword()
    {
        MemoryService service = new(new XmlFunctionCaller(NullLogger<XmlFunctionCaller>.Instance))
        {
            Configuration = new MemoryConfig
            {
                EnableMemoryKeywordHint = true,
                MemoryHintKeywords = ["remember-this"]
            }
        };

        string result = service.AppendMemoryHintIfNeeded("please remember-this detail");

        Assert.Multiple(() =>
        {
            Assert.That(result, Does.Contain("MemoryService"));
            Assert.That(result, Does.Contain("permissions"));
            Assert.That(result, Does.Contain("audit"));
            Assert.That(result, Does.Contain("please remember-this detail"));
        });
    }

    [Test]
    public void AppendMemoryHintIfNeeded_LeavesUnmatchedMessageUnchanged()
    {
        MemoryService service = new(new XmlFunctionCaller(NullLogger<XmlFunctionCaller>.Instance))
        {
            Configuration = new MemoryConfig
            {
                EnableMemoryKeywordHint = true,
                MemoryHintKeywords = ["remember-this"]
            }
        };

        string result = service.AppendMemoryHintIfNeeded("plain chat");

        Assert.That(result, Is.EqualTo("plain chat"));
    }
}
