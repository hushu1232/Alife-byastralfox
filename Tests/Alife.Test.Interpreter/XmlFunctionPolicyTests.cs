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

    [Test]
    public void ExecutionPolicyRejectsToolOutsideCurrentRoute()
    {
        XmlFunctionExecutionPolicy policy = new()
        {
            CurrentRoute = RouteAllowing("allowed_tool")
        };

        XmlFunctionExecutionDecision decision = policy.TryConsume(Function("denied_tool"));

        Assert.Multiple(() =>
        {
            Assert.That(decision.IsAllowed, Is.False);
            Assert.That(decision.Reason, Is.EqualTo("tool_not_allowed_in_current_route"));
        });
    }

    [Test]
    public void ExecutionPolicyRouteGateDoesNotConsumeBudgetWhenRejectingOutsideRoute()
    {
        XmlFunctionExecutionPolicy policy = new()
        {
            CurrentRoute = RouteAllowing("allowed_tool"),
            MaxBudgetPerTurn = 1
        };

        XmlFunctionExecutionDecision denied = policy.TryConsume(Function("denied_tool"));
        XmlFunctionExecutionDecision allowed = policy.TryConsume(Function("allowed_tool"));

        Assert.Multiple(() =>
        {
            Assert.That(denied.IsAllowed, Is.False);
            Assert.That(denied.Reason, Is.EqualTo("tool_not_allowed_in_current_route"));
            Assert.That(allowed.IsAllowed, Is.True);
            Assert.That(policy.BudgetUsedThisTurn, Is.EqualTo(1));
        });
    }

    [Test]
    public void ExecutionPolicyAllowsListedToolAndConsumesBudgetNormally()
    {
        XmlFunctionExecutionPolicy policy = new()
        {
            CurrentRoute = RouteAllowing("allowed_tool"),
            MaxBudgetPerTurn = 2
        };

        XmlFunctionExecutionDecision first = policy.TryConsume(Function("allowed_tool"));
        XmlFunctionExecutionDecision second = policy.TryConsume(Function("allowed_tool"));
        XmlFunctionExecutionDecision exhausted = policy.TryConsume(Function("allowed_tool"));

        Assert.Multiple(() =>
        {
            Assert.That(first.IsAllowed, Is.True);
            Assert.That(second.IsAllowed, Is.True);
            Assert.That(exhausted.IsAllowed, Is.False);
            Assert.That(exhausted.Reason, Does.Contain("budget"));
            Assert.That(policy.BudgetUsedThisTurn, Is.EqualTo(2));
        });
    }

    [Test]
    public void ResetTurnBudgetClearsBudgetAndCurrentRoute()
    {
        XmlFunctionExecutionPolicy policy = new()
        {
            CurrentRoute = RouteAllowing("allowed_tool"),
            MaxBudgetPerTurn = 1
        };

        XmlFunctionExecutionDecision consumed = policy.TryConsume(Function("allowed_tool"));

        policy.ResetTurnBudget();
        int budgetAfterReset = policy.BudgetUsedThisTurn;
        ToolRouteDecision? routeAfterReset = policy.CurrentRoute;
        XmlFunctionExecutionDecision afterReset = policy.TryConsume(Function("denied_tool"));

        Assert.Multiple(() =>
        {
            Assert.That(consumed.IsAllowed, Is.True);
            Assert.That(budgetAfterReset, Is.Zero);
            Assert.That(routeAfterReset, Is.Null);
            Assert.That(afterReset.IsAllowed, Is.True);
            Assert.That(policy.BudgetUsedThisTurn, Is.EqualTo(1));
        });
    }

    [Test]
    public void UseRouteDoesNotRestoreStaleRouteAfterResetTurnBudget()
    {
        XmlFunctionExecutionPolicy policy = new()
        {
            CurrentRoute = RouteAllowing("old_tool")
        };

        using (IDisposable scope = policy.UseRoute(RouteAllowing("new_tool")))
        {
            policy.ResetTurnBudget();
            scope.Dispose();
        }

        XmlFunctionExecutionDecision decision = policy.TryConsume(Function("denied_by_old_route"));

        Assert.Multiple(() =>
        {
            Assert.That(policy.CurrentRoute, Is.Null);
            Assert.That(decision.IsAllowed, Is.True);
        });
    }

    [Test]
    public void UseRouteRestoresPreviousRouteForNestedScopes()
    {
        XmlFunctionExecutionPolicy policy = new()
        {
            CurrentRoute = RouteAllowing("outer_tool")
        };

        using (policy.UseRoute(RouteAllowing("middle_tool")))
        {
            Assert.That(policy.TryConsume(Function("outer_tool")).Reason, Is.EqualTo("tool_not_allowed_in_current_route"));
            Assert.That(policy.TryConsume(Function("middle_tool")).IsAllowed, Is.True);

            using (policy.UseRoute(RouteAllowing("inner_tool")))
            {
                Assert.That(policy.TryConsume(Function("middle_tool")).Reason, Is.EqualTo("tool_not_allowed_in_current_route"));
                Assert.That(policy.TryConsume(Function("inner_tool")).IsAllowed, Is.True);
            }

            Assert.That(policy.TryConsume(Function("inner_tool")).Reason, Is.EqualTo("tool_not_allowed_in_current_route"));
            Assert.That(policy.TryConsume(Function("middle_tool")).IsAllowed, Is.True);
        }

        Assert.Multiple(() =>
        {
            Assert.That(policy.TryConsume(Function("middle_tool")).Reason, Is.EqualTo("tool_not_allowed_in_current_route"));
            Assert.That(policy.TryConsume(Function("outer_tool")).IsAllowed, Is.True);
        });
    }

    [Test]
    public void ExecutionPolicyRejectsRouteDeniedHighRiskBeforeAuthorization()
    {
        bool authorizerCalled = false;
        XmlFunctionExecutionPolicy policy = new()
        {
            CurrentRoute = RouteAllowing("allowed_tool"),
            AuthorizeHighRiskFunction = _ =>
            {
                authorizerCalled = true;
                return new XmlFunctionExecutionDecision(true, "approved");
            }
        };

        XmlFunctionExecutionDecision decision = policy.TryConsume(
            Function("denied_tool", XmlFunctionRiskLevel.High));

        Assert.Multiple(() =>
        {
            Assert.That(decision.IsAllowed, Is.False);
            Assert.That(decision.Reason, Is.EqualTo("tool_not_allowed_in_current_route"));
            Assert.That(authorizerCalled, Is.False);
        });
    }
    static ToolRouteDecision RouteAllowing(params string[] allowedTools) => new(
        "route-1",
        ToolCapabilityDomain.DataAgent,
        "analysis_continue",
        allowedTools,
        [],
        ToolRouteState.Empty,
        "test_route");

    static XmlFunction Function(
        string name,
        XmlFunctionRiskLevel riskLevel = XmlFunctionRiskLevel.Low,
        int budgetCost = 1) => new()
    {
        Name = name,
        Mode = FunctionMode.OneShot,
        RiskLevel = riskLevel,
        BudgetCost = budgetCost,
        Invoker = (_, _) => Task.CompletedTask,
    };
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
