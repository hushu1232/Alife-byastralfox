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
