using Alife.Function.Interpreter;
using Alife.Function.FunctionCaller;
using Microsoft.Extensions.Logging.Abstractions;

[TestFixture]
public class XmlFunctionPolicyTests
{
    [Test]
    public void FunctionCallerRegistersImplicitHandlerDocumentTrigger()
    {
        XmlFunctionCaller caller = new(NullLogger<XmlFunctionCaller>.Instance);
        XmlHandler handler = new(new HiddenTool())
        {
            Name = "HiddenTool",
            Description = "Hidden tool docs."
        };

        caller.RegisterHandler(handler, DocumentMode.Implicit);
        string guide = caller.BuildFunctionGuide();

        Assert.That(caller.CanHandleFunction("hiddentool"), Is.True);
        Assert.That(caller.CanHandleFunction("hidden_ping"), Is.True);
        Assert.That(guide, Does.Contain("### 隐式服务"));
        Assert.That(guide, Does.Contain("<hiddentool />"));
        Assert.That(guide, Does.Not.Contain("<hidden_ping"));
    }

    [Test]
    public void Handle_BlocksHighRiskFunctionByDefault()
    {
        PolicyHandler handler = new();
        XmlHandlerTable table = new();
        table.Register(new XmlHandler(handler));

        XmlContext context = OneShotContext();
        InvalidOperationException? exception = Assert.ThrowsAsync<InvalidOperationException>(
            async () => await table.Handle("deletefile", context));

        Assert.That(exception!.Message, Does.Contain("high-risk"));
        Assert.That(handler.DeleteCalls, Is.Zero);
    }

    [Test]
    public async Task Handle_BlocksFunctionWhenTurnBudgetIsExhausted()
    {
        PolicyHandler handler = new();
        XmlHandlerTable table = new();
        table.ExecutionPolicy.MaxBudgetPerTurn = 2;
        table.Register(new XmlHandler(handler));

        XmlContext context = OneShotContext();
        await table.Handle("ping", context);
        await table.Handle("ping", context);

        InvalidOperationException? exception = Assert.ThrowsAsync<InvalidOperationException>(
            async () => await table.Handle("ping", context));

        Assert.That(exception!.Message, Does.Contain("budget"));
        Assert.That(handler.PingCalls, Is.EqualTo(2));

        table.ExecutionPolicy.ResetTurnBudget();
        await table.Handle("ping", context);
        Assert.That(handler.PingCalls, Is.EqualTo(3));
    }

    [Test]
    public async Task Handle_AllowsHighRiskFunctionOnlyWhenAuthorizerApproves()
    {
        PolicyHandler handler = new();
        XmlHandlerTable table = new();
        table.ExecutionPolicy.MaxBudgetPerTurn = 1;
        table.ExecutionPolicy.AuthorizeHighRiskFunction = function =>
            function.Name == "deletefile"
                ? new XmlFunctionExecutionDecision(true, "approved")
                : new XmlFunctionExecutionDecision(false, "denied");
        table.Register(new XmlHandler(handler));

        XmlContext context = OneShotContext();
        await table.Handle("deletefile", context);

        Assert.That(handler.DeleteCalls, Is.EqualTo(1));
        Assert.That(table.ExecutionPolicy.BudgetUsedThisTurn, Is.EqualTo(1));
        InvalidOperationException? exception = Assert.ThrowsAsync<InvalidOperationException>(
            async () => await table.Handle("deletefile", context));
        Assert.That(exception!.Message, Does.Contain("budget"));
        Assert.That(handler.DeleteCalls, Is.EqualTo(1));
    }

    static XmlContext OneShotContext() => new()
    {
        CallMode = CallMode.OneShot,
        Parameters = new Dictionary<string, string>(),
    };

    sealed class PolicyHandler
    {
        public int DeleteCalls { get; private set; }
        public int PingCalls { get; private set; }

        [XmlFunction(FunctionMode.OneShot, riskLevel: XmlFunctionRiskLevel.High)]
        public void DeleteFile()
        {
            DeleteCalls++;
        }

        [XmlFunction(FunctionMode.OneShot)]
        public void Ping()
        {
            PingCalls++;
        }
    }

    sealed class HiddenTool
    {
        [XmlFunction(FunctionMode.OneShot, name: "hidden_ping")]
        public void Ping() {}
    }
}
