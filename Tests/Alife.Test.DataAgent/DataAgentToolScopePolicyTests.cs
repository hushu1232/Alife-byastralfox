using System.Reflection;
using Alife.Function.DataAgent;

namespace Alife.Test.DataAgent;

[TestFixture]
public sealed class DataAgentToolScopePolicyTests
{
    [Test]
    public void PlannerScopeCanReadCatalogAndScenarioPackButCannotExecuteQuery()
    {
        DataAgentNodeToolScope scope = DataAgentToolScopePolicy.ForNode(DataAgentWorkflowNodeNames.QueryPlanner);

        Assert.Multiple(() =>
        {
            Assert.That(scope.AllowsModelCall, Is.True);
            Assert.That(scope.AllowedCapabilities, Does.Contain(DataAgentNodeCapabilities.ReadCatalog));
            Assert.That(scope.AllowedCapabilities, Does.Contain(DataAgentNodeCapabilities.ReadScenarioPack));
            Assert.That(scope.AllowedCapabilities, Does.Not.Contain(DataAgentNodeCapabilities.ExecuteReadOnlyQuery));
            Assert.That(scope.AllowedCapabilities, Does.Not.Contain(DataAgentNodeCapabilities.ReadProgressDiagnostics));
        });
    }

    [Test]
    public void DiagnosticsScopeCannotPlanOrExecuteQueries()
    {
        DataAgentNodeToolScope scope = DataAgentToolScopePolicy.ForNode(DataAgentWorkflowNodeNames.DiagnosticsRouter);

        Assert.Multiple(() =>
        {
            Assert.That(scope.AllowsModelCall, Is.True);
            Assert.That(scope.AllowedCapabilities, Does.Contain(DataAgentNodeCapabilities.ReadProgressDiagnostics));
            Assert.That(scope.AllowedCapabilities, Does.Contain(DataAgentNodeCapabilities.ReadTraceDiagnostics));
            Assert.That(scope.AllowedCapabilities, Does.Contain(DataAgentNodeCapabilities.ReadEvidenceDiagnostics));
            Assert.That(scope.AllowedCapabilities, Does.Not.Contain(DataAgentNodeCapabilities.GenerateQueryPlan));
            Assert.That(scope.AllowedCapabilities, Does.Not.Contain(DataAgentNodeCapabilities.ExecuteReadOnlyQuery));
        });
    }

    [Test]
    public void SafetyAndExecutionNodesAreDeterministic()
    {
        DataAgentNodeToolScope validator = DataAgentToolScopePolicy.ForNode(DataAgentWorkflowNodeNames.QueryPlanValidator);
        DataAgentNodeToolScope compiler = DataAgentToolScopePolicy.ForNode(DataAgentWorkflowNodeNames.SqlCompiler);
        DataAgentNodeToolScope safety = DataAgentToolScopePolicy.ForNode(DataAgentWorkflowNodeNames.SqlSafety);
        DataAgentNodeToolScope execute = DataAgentToolScopePolicy.ForNode(DataAgentWorkflowNodeNames.ReadOnlyExecute);

        Assert.Multiple(() =>
        {
            Assert.That(validator.AllowsModelCall, Is.False);
            Assert.That(compiler.AllowsModelCall, Is.False);
            Assert.That(safety.AllowsModelCall, Is.False);
            Assert.That(execute.AllowsModelCall, Is.False);
            Assert.That(execute.AllowedCapabilities, Is.EqualTo(new[] { DataAgentNodeCapabilities.ExecuteReadOnlyQuery }));
        });
    }

    [Test]
    public void TerminalAndRejectScopesAreDeterministicAndToolless()
    {
        DataAgentNodeToolScope terminal = DataAgentToolScopePolicy.ForNode(DataAgentWorkflowNodeNames.Terminal);
        DataAgentNodeToolScope reject = DataAgentToolScopePolicy.ForNode(DataAgentWorkflowNodeNames.Reject);

        Assert.Multiple(() =>
        {
            Assert.That(terminal.AllowsModelCall, Is.False);
            Assert.That(terminal.AllowedCapabilities, Is.Empty);
            Assert.That(terminal.Reason, Is.EqualTo("terminal_node_has_no_query_capabilities"));
            Assert.That(reject.AllowsModelCall, Is.False);
            Assert.That(reject.AllowedCapabilities, Is.Empty);
            Assert.That(reject.Reason, Is.EqualTo("reject_node_has_no_query_capabilities"));
        });
    }

    [Test]
    public void UnknownNodeFailsClosed()
    {
        DataAgentNodeToolScope scope = DataAgentToolScopePolicy.ForNode("unknown_node");

        Assert.Multiple(() =>
        {
            Assert.That(scope.NodeName, Is.EqualTo("unknown_node"));
            Assert.That(scope.AllowsModelCall, Is.False);
            Assert.That(scope.AllowedCapabilities, Is.Empty);
            Assert.That(scope.Reason, Is.EqualTo("unknown_node_fail_closed"));
        });
    }

    [TestCase(null)]
    [TestCase("")]
    [TestCase("   ")]
    public void BlankNodeFailsClosed(string? nodeName)
    {
        DataAgentNodeToolScope scope = DataAgentToolScopePolicy.ForNode(nodeName!);

        Assert.Multiple(() =>
        {
            Assert.That(scope.AllowsModelCall, Is.False);
            Assert.That(scope.AllowedCapabilities, Is.Empty);
            Assert.That(scope.Reason, Is.EqualTo("unknown_node_fail_closed"));
            Assert.That(scope.NodeName, Is.Not.Null);
        });
    }

    [Test]
    public void RepeatedPlannerScopesExposeSamePropertyValues()
    {
        DataAgentNodeToolScope first = DataAgentToolScopePolicy.ForNode(DataAgentWorkflowNodeNames.QueryPlanner);
        DataAgentNodeToolScope second = DataAgentToolScopePolicy.ForNode(DataAgentWorkflowNodeNames.QueryPlanner);

        Assert.Multiple(() =>
        {
            Assert.That(first.NodeName, Is.EqualTo(second.NodeName));
            Assert.That(first.AllowsModelCall, Is.EqualTo(second.AllowsModelCall));
            Assert.That(first.AllowedCapabilities, Is.EqualTo(second.AllowedCapabilities));
            Assert.That(first.Reason, Is.EqualTo(second.Reason));
            Assert.That(first.AllowedCapabilities, Is.Not.SameAs(second.AllowedCapabilities));
        });
    }

    [Test]
    public void ScopeTypeDoesNotExposeGeneratedRecordValueEquality()
    {
        IEnumerable<string> equalityOperators = typeof(DataAgentNodeToolScope)
            .GetMethods(BindingFlags.Public | BindingFlags.Static)
            .Where(method => method.Name is "op_Equality" or "op_Inequality")
            .Select(method => method.Name);

        Assert.That(equalityOperators, Is.Empty);
    }

    [Test]
    public void DefaultPolicyContainsNoAmbientAllToolsScope()
    {
        IReadOnlyList<DataAgentNodeToolScope> scopes = DataAgentToolScopePolicy.CreateDefault();

        Assert.Multiple(() =>
        {
            Assert.That(scopes, Is.Not.Empty);
            Assert.That(scopes.All(scope => scope.AllowedCapabilities.Contains("all_tools") == false), Is.True);
            Assert.That(scopes.All(scope => scope.AllowedCapabilities.Contains("*") == false), Is.True);
        });
    }
}
