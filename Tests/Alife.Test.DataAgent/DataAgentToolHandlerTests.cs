using System.Reflection;
using Alife.Function.DataAgent;
using Alife.Function.Interpreter;

namespace Alife.Test.DataAgent;

[TestFixture]
public sealed class DataAgentToolHandlerTests
{
    [Test]
    public void QueryReturnsDataAgentContext()
    {
        DataAgentToolHandler handler = new(new DataAgentService(CreateDatabasePath()));

        string context = handler.Query("Which documents describe DataAgent NL2SQL?");

        Assert.Multiple(() =>
        {
            Assert.That(context, Does.Contain("[data_agent_context]"));
            Assert.That(context, Does.Contain("dataset=document_index"));
            Assert.That(context, Does.Contain("docs/superpowers/specs/2026-06-27-dataagent-nl2sql-design.md"));
            Assert.That(context, Does.Contain("[/data_agent_context]"));
        });
    }

    [Test]
    public void QueryOutputIsContextBlockNotRawSqlOnly()
    {
        DataAgentToolHandler handler = new(new DataAgentService(CreateDatabasePath()));

        string context = handler.Query("Which runtime readiness gate is required?");

        Assert.Multiple(() =>
        {
            Assert.That(context, Does.Contain("sql_status=validated"));
            Assert.That(context, Does.Contain("summary="));
            Assert.That(context.TrimStart(), Does.StartWith("[data_agent_context]"));
            Assert.That(context.TrimStart(), Does.Not.StartWith("SELECT "));
        });
    }

    [Test]
    public void QueryMethodIsRegisteredAsDataAgentXmlFunction()
    {
        MethodInfo method = typeof(DataAgentToolHandler).GetMethod(nameof(DataAgentToolHandler.Query))!;

        XmlFunctionAttribute attribute = method.GetCustomAttribute<XmlFunctionAttribute>()!;

        Assert.Multiple(() =>
        {
            Assert.That(attribute, Is.Not.Null);
            Assert.That(attribute.Mode, Is.EqualTo(FunctionMode.OneShot));
            Assert.That(attribute.Name, Is.EqualTo("dataagent_query"));
        });
    }

    static string CreateDatabasePath()
    {
        string directory = Path.Combine(TestContext.CurrentContext.WorkDirectory, "dataagent-tool-handler-tests");
        Directory.CreateDirectory(directory);
        string databasePath = Path.Combine(directory, $"{Guid.NewGuid():N}.sqlite");
        DataAgentSchemaInitializer.Initialize(databasePath);
        DataAgentFixtureImporter.Import(databasePath);
        return databasePath;
    }
}
