using System.IO;
using Alife.Function.DataAgent;
using Microsoft.Data.Sqlite;
using NUnit.Framework;

namespace Alife.Test.QChat;

public sealed class DataAgentQChatLatencyAuditLogTests
{
    [Test]
    public void RecordPersistsOnlyLatencyMetadata()
    {
        string path = Path.Combine(TestContext.CurrentContext.WorkDirectory, $"qchat-latency-{Guid.NewGuid():N}.sqlite");
        try
        {
            DataAgentQChatLatencyAuditLog log = new(path);
            log.Record(new DataAgentQChatLatencyAuditRecord(
                AgentId: "mixu",
                ConversationKind: "private",
                Outcome: "sent",
                ElapsedMilliseconds: 420,
                FirstContentMilliseconds: 170,
                CreatedAt: new DateTimeOffset(2026, 7, 22, 12, 0, 0, TimeSpan.Zero)));

            DataAgentQChatLatencyAuditRecord record = log.ReadAll().Single();
            Assert.Multiple(() =>
            {
                Assert.That(record.AgentId, Is.EqualTo("mixu"));
                Assert.That(record.ConversationKind, Is.EqualTo("private"));
                Assert.That(record.Outcome, Is.EqualTo("sent"));
                Assert.That(record.ElapsedMilliseconds, Is.EqualTo(420));
                Assert.That(record.FirstContentMilliseconds, Is.EqualTo(170));
            });
        }
        finally
        {
            SqliteConnection.ClearAllPools();
            if (File.Exists(path))
                File.Delete(path);
        }
    }
}
