# DataAgent V1.7 Capability Boundary Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Standardize DataAgent query and analysis tools behind a capability provider boundary without adding PostgreSQL, LangGraph, `IDataAgentStore`, or new business analytics features.

**Architecture:** V1.7 keeps the existing .NET 9 runtime and V1.6 Tool Broker gate. The main change is to introduce a DataAgent-local provider/registry boundary and a shared FunctionCaller manifest factory so Tool Broker routing and DataAgent providers use the same capability metadata. This prepares future multi-agent orchestration without introducing a sidecar or supervisor in V1.7.

**Tech Stack:** .NET 9, NUnit, PowerShell readiness scripts, `Alife.Function.FunctionCaller`, `Alife.Function.DataAgent`, `Alife.Function.QChat`, SQLite for existing V1.x runtime state.

---

## Scope Guard

Implement only the V1.7 capability boundary:

```text
Allowed:
- capability provider contracts
- capability registry validation
- shared DataAgent Tool Broker manifest factory
- query and analysis built-in providers
- DataAgentModuleService provider wiring
- required readiness markers and tests
- future multi-agent coordination notes in docs

Not allowed in V1.7:
- PostgreSQL
- IDataAgentStore
- LangGraph
- Python sidecar
- supervisor runtime
- plugin marketplace
- new business query domains
```

The user's multi-agent coordination guidance is a future constraint, not a V1.7 runtime feature. The boundary should make later decomposition into permission validation, SQL generation, report interpretation, Scenario Knowledge Package mapping, checkpoints, degradation, and streaming progress possible.

## File Structure

- Create `sources/Alife.Function/Alife.Function.FunctionCaller/DataAgentToolCapabilityManifests.cs`: one shared source of DataAgent `ToolCapabilityManifest` records.
- Modify `sources/Alife.Function/Alife.Function.FunctionCaller/ToolCapabilityRouter.cs`: use the shared manifest factory instead of hard-coded manifest construction.
- Create `sources/Alife.Function/Alife.Function.DataAgent/IDataAgentCapabilityProvider.cs`: provider contract.
- Create `sources/Alife.Function/Alife.Function.DataAgent/IDataAgentCapabilityRegistrar.cs`: narrow registration contract.
- Create `sources/Alife.Function/Alife.Function.DataAgent/DataAgentCapabilityRegistry.cs`: deterministic provider/tool validation.
- Create `sources/Alife.Function/Alife.Function.DataAgent/DataAgentCapabilityRegistrar.cs`: adapter from provider registration to `XmlFunctionCaller`.
- Create `sources/Alife.Function/Alife.Function.DataAgent/DataAgentQueryCapabilityProvider.cs`: built-in query provider.
- Create `sources/Alife.Function/Alife.Function.DataAgent/DataAgentAnalysisCapabilityProvider.cs`: built-in analysis provider.
- Modify `sources/Alife.Function/Alife.Function.DataAgent/DataAgentModuleService.cs`: create providers, validate registry, register handlers through provider boundary.
- Modify `tools/check-dataagent-readiness.ps1`: require V1.7 capability boundary evidence.
- Modify `tools/check-qchat-engineering-map.ps1`: require QChat engineering map evidence for the DataAgent plugin boundary.
- Modify `sources/Alife.Function/Alife.Function.DataAgent/DataAgentReadiness.cs`: add a runtime core readiness check for provider metadata.
- Add tests under `Tests/Alife.Test.DataAgent` and update `Tests/Alife.Test.Interpreter`.

---

### Task 1: Shared DataAgent Tool Manifests

**Files:**

- Create: `sources/Alife.Function/Alife.Function.FunctionCaller/DataAgentToolCapabilityManifests.cs`
- Modify: `sources/Alife.Function/Alife.Function.FunctionCaller/ToolCapabilityRouter.cs`
- Test: `Tests/Alife.Test.Interpreter/ToolCapabilityRouterTests.cs`

- [ ] **Step 1: Write the failing shared-manifest test**

Append this test to `Tests/Alife.Test.Interpreter/ToolCapabilityRouterTests.cs`:

```csharp
[Test]
public void DefaultRouterUsesSharedDataAgentManifestFactory()
{
    ToolCapabilityRouter router = ToolCapabilityRouter.CreateDefault();
    IReadOnlyList<ToolCapabilityManifest> manifests = DataAgentToolCapabilityManifests.Create();

    Assert.Multiple(() =>
    {
        Assert.That(router.ToolNames, Is.EqualTo(manifests.Select(manifest => manifest.Name).ToArray()));
        Assert.That(manifests.Select(manifest => manifest.Name), Is.EqualTo(new[]
        {
            "dataagent_query",
            "dataagent_analysis_start",
            "dataagent_analysis_continue",
            "dataagent_analysis_summarize",
            "dataagent_analysis_end"
        }));
        Assert.That(manifests.Single(manifest => manifest.Name == "dataagent_query").StateEffect, Is.EqualTo(ToolStateEffect.ReadsData));
        Assert.That(manifests.Single(manifest => manifest.Name == "dataagent_analysis_end").StateEffect, Is.EqualTo(ToolStateEffect.EndsAnalysis));
    });
}
```

- [ ] **Step 2: Run the focused test and verify it fails**

Run:

```powershell
& "C:\Users\hu shu\.dotnet\dotnet.exe" test Tests\Alife.Test.Interpreter\Alife.Test.Interpreter.csproj --no-restore --filter "Name=DefaultRouterUsesSharedDataAgentManifestFactory" -v:minimal
```

Expected: fail because `DataAgentToolCapabilityManifests` does not exist.

- [ ] **Step 3: Add the shared manifest factory**

Create `sources/Alife.Function/Alife.Function.FunctionCaller/DataAgentToolCapabilityManifests.cs`:

```csharp
using System;
using System.Collections.Generic;
using System.Linq;

namespace Alife.Function.FunctionCaller;

public static class DataAgentToolCapabilityManifests
{
    static readonly IReadOnlyList<ToolCapabilityPrecondition> TrustedOnlyPreconditions =
    [
        ToolCapabilityPrecondition.TrustedRuntime
    ];

    static readonly IReadOnlyList<ToolCapabilityPrecondition> ActiveAnalysisPreconditions =
    [
        ToolCapabilityPrecondition.ActiveDataAgentAnalysisSession,
        ToolCapabilityPrecondition.TrustedRuntime
    ];

    static readonly IReadOnlyList<ToolCapabilitySurface> DataAgentSurfaces =
    [
        ToolCapabilitySurface.OwnerPrivate,
        ToolCapabilitySurface.TrustedRuntime
    ];

    public static IReadOnlyList<ToolCapabilityManifest> Query { get; } =
    [
        new(
            "dataagent_query",
            ToolCapabilityDomain.DataAgent,
            "query",
            ToolCapabilityRisk.Low,
            TrustedOnlyPreconditions,
            DataAgentSurfaces,
            ToolStateEffect.ReadsData)
    ];

    public static IReadOnlyList<ToolCapabilityManifest> Analysis { get; } =
    [
        new(
            "dataagent_analysis_start",
            ToolCapabilityDomain.DataAgent,
            "analysis_start",
            ToolCapabilityRisk.Low,
            TrustedOnlyPreconditions,
            DataAgentSurfaces,
            ToolStateEffect.AppendsAnalysisTurn),
        new(
            "dataagent_analysis_continue",
            ToolCapabilityDomain.DataAgent,
            "analysis_continue",
            ToolCapabilityRisk.Low,
            ActiveAnalysisPreconditions,
            DataAgentSurfaces,
            ToolStateEffect.AppendsAnalysisTurn),
        new(
            "dataagent_analysis_summarize",
            ToolCapabilityDomain.DataAgent,
            "analysis_summarize",
            ToolCapabilityRisk.Low,
            ActiveAnalysisPreconditions,
            DataAgentSurfaces,
            ToolStateEffect.SummarizesAnalysis),
        new(
            "dataagent_analysis_end",
            ToolCapabilityDomain.DataAgent,
            "analysis_end",
            ToolCapabilityRisk.Low,
            ActiveAnalysisPreconditions,
            DataAgentSurfaces,
            ToolStateEffect.EndsAnalysis)
    ];

    public static IReadOnlyList<ToolCapabilityManifest> Create()
    {
        return Array.AsReadOnly(Query.Concat(Analysis).ToArray());
    }
}
```

- [ ] **Step 4: Replace hard-coded router manifests**

In `sources/Alife.Function/Alife.Function.FunctionCaller/ToolCapabilityRouter.cs`, replace `CreateDefault()` with:

```csharp
public static ToolCapabilityRouter CreateDefault()
{
    return new ToolCapabilityRouter(DataAgentToolCapabilityManifests.Create());
}
```

Then remove the now-unused static `TrustedOnlyPreconditions`, `ActiveAnalysisPreconditions`, and `DataAgentSurfaces` fields from `ToolCapabilityRouter`.

- [ ] **Step 5: Run router tests**

Run:

```powershell
& "C:\Users\hu shu\.dotnet\dotnet.exe" test Tests\Alife.Test.Interpreter\Alife.Test.Interpreter.csproj --no-restore --filter "FullyQualifiedName~ToolCapabilityRouterTests" -v:minimal
```

Expected: all `ToolCapabilityRouterTests` pass.

- [ ] **Step 6: Commit the shared manifest factory**

Run:

```powershell
git add sources/Alife.Function/Alife.Function.FunctionCaller/DataAgentToolCapabilityManifests.cs sources/Alife.Function/Alife.Function.FunctionCaller/ToolCapabilityRouter.cs Tests/Alife.Test.Interpreter/ToolCapabilityRouterTests.cs
git commit -m "Share DataAgent Tool Broker manifests"
```

---

### Task 2: DataAgent Capability Contracts And Registry

**Files:**

- Create: `sources/Alife.Function/Alife.Function.DataAgent/IDataAgentCapabilityProvider.cs`
- Create: `sources/Alife.Function/Alife.Function.DataAgent/IDataAgentCapabilityRegistrar.cs`
- Create: `sources/Alife.Function/Alife.Function.DataAgent/DataAgentCapabilityRegistry.cs`
- Test: `Tests/Alife.Test.DataAgent/DataAgentCapabilityRegistryTests.cs`

- [ ] **Step 1: Write failing registry tests**

Create `Tests/Alife.Test.DataAgent/DataAgentCapabilityRegistryTests.cs`:

```csharp
using Alife.Function.DataAgent;
using Alife.Function.FunctionCaller;

namespace Alife.Test.DataAgent;

[TestFixture]
public sealed class DataAgentCapabilityRegistryTests
{
    [Test]
    public void AddStoresProvidersInRegistrationOrder()
    {
        DataAgentCapabilityRegistry registry = new();

        registry.Add(new FakeProvider("query", [new ToolCapabilityManifest("dataagent_query", ToolCapabilityDomain.DataAgent, "query")]));
        registry.Add(new FakeProvider("analysis", [new ToolCapabilityManifest("dataagent_analysis_start", ToolCapabilityDomain.DataAgent, "analysis_start")]));

        Assert.Multiple(() =>
        {
            Assert.That(registry.ProviderNames, Is.EqualTo(new[] { "query", "analysis" }));
            Assert.That(registry.ToolNames, Is.EqualTo(new[] { "dataagent_query", "dataagent_analysis_start" }));
            Assert.That(registry.ToolManifests.Select(manifest => manifest.Intent), Is.EqualTo(new[] { "query", "analysis_start" }));
        });
    }

    [Test]
    public void AddRejectsDuplicateProviderNames()
    {
        DataAgentCapabilityRegistry registry = new();
        registry.Add(new FakeProvider("query", [new ToolCapabilityManifest("dataagent_query", ToolCapabilityDomain.DataAgent, "query")]));

        InvalidOperationException exception = Assert.Throws<InvalidOperationException>(() =>
            registry.Add(new FakeProvider("query", [new ToolCapabilityManifest("dataagent_other", ToolCapabilityDomain.DataAgent, "query")]))!;

        Assert.That(exception.Message, Does.Contain("Duplicate DataAgent capability provider"));
    }

    [Test]
    public void AddRejectsDuplicateToolNames()
    {
        DataAgentCapabilityRegistry registry = new();
        registry.Add(new FakeProvider("query", [new ToolCapabilityManifest("dataagent_query", ToolCapabilityDomain.DataAgent, "query")]));

        InvalidOperationException exception = Assert.Throws<InvalidOperationException>(() =>
            registry.Add(new FakeProvider("analysis", [new ToolCapabilityManifest("dataagent_query", ToolCapabilityDomain.DataAgent, "analysis")]))!;

        Assert.That(exception.Message, Does.Contain("Duplicate DataAgent tool capability"));
    }

    [Test]
    public void AddRejectsBlankProviderAndToolNames()
    {
        DataAgentCapabilityRegistry registry = new();

        Assert.Multiple(() =>
        {
            Assert.Throws<ArgumentException>(() =>
                registry.Add(new FakeProvider(" ", [new ToolCapabilityManifest("dataagent_query", ToolCapabilityDomain.DataAgent, "query")])));
            Assert.Throws<ArgumentException>(() =>
                registry.Add(new FakeProvider("query", [new ToolCapabilityManifest(" ", ToolCapabilityDomain.DataAgent, "query")])));
        });
    }

    sealed class FakeProvider(string name, IReadOnlyList<ToolCapabilityManifest> toolManifests)
        : IDataAgentCapabilityProvider
    {
        public string Name => name;
        public IReadOnlyList<ToolCapabilityManifest> ToolManifests => toolManifests;
        public void Register(IDataAgentCapabilityRegistrar registrar) { }
    }
}
```

- [ ] **Step 2: Run the focused tests and verify they fail**

Run:

```powershell
& "C:\Users\hu shu\.dotnet\dotnet.exe" test Tests\Alife.Test.DataAgent\Alife.Test.DataAgent.csproj --no-restore --filter "FullyQualifiedName~DataAgentCapabilityRegistryTests" -v:minimal
```

Expected: fail because the capability interfaces and registry do not exist.

- [ ] **Step 3: Add the provider contract**

Create `sources/Alife.Function/Alife.Function.DataAgent/IDataAgentCapabilityProvider.cs`:

```csharp
using Alife.Function.FunctionCaller;

namespace Alife.Function.DataAgent;

public interface IDataAgentCapabilityProvider
{
    string Name { get; }
    IReadOnlyList<ToolCapabilityManifest> ToolManifests { get; }
    void Register(IDataAgentCapabilityRegistrar registrar);
}
```

- [ ] **Step 4: Add the registrar contract**

Create `sources/Alife.Function/Alife.Function.DataAgent/IDataAgentCapabilityRegistrar.cs`:

```csharp
using Alife.Function.Interpreter;

namespace Alife.Function.DataAgent;

public interface IDataAgentCapabilityRegistrar
{
    void RegisterXmlHandlerWithoutStaticDocument(XmlHandler handler, params string[] plainAreas);
}
```

- [ ] **Step 5: Add the registry implementation**

Create `sources/Alife.Function/Alife.Function.DataAgent/DataAgentCapabilityRegistry.cs`:

```csharp
using Alife.Function.FunctionCaller;

namespace Alife.Function.DataAgent;

public sealed class DataAgentCapabilityRegistry
{
    readonly List<IDataAgentCapabilityProvider> providers = [];
    readonly HashSet<string> providerNames = new(StringComparer.Ordinal);
    readonly HashSet<string> toolNames = new(StringComparer.Ordinal);

    public IReadOnlyList<IDataAgentCapabilityProvider> Providers => providers.AsReadOnly();
    public IReadOnlyList<string> ProviderNames => providers.Select(provider => provider.Name).ToArray();
    public IReadOnlyList<string> ToolNames => providers.SelectMany(provider => provider.ToolManifests.Select(manifest => manifest.Name)).ToArray();
    public IReadOnlyList<ToolCapabilityManifest> ToolManifests => providers.SelectMany(provider => provider.ToolManifests).ToArray();

    public void Add(IDataAgentCapabilityProvider provider)
    {
        ArgumentNullException.ThrowIfNull(provider);

        if (string.IsNullOrWhiteSpace(provider.Name))
            throw new ArgumentException("DataAgent capability provider name cannot be blank.", nameof(provider));

        if (providerNames.Add(provider.Name) == false)
            throw new InvalidOperationException($"Duplicate DataAgent capability provider: {provider.Name}");

        IReadOnlyList<ToolCapabilityManifest> manifests = provider.ToolManifests
            ?? throw new ArgumentException($"DataAgent capability provider {provider.Name} returned null manifests.", nameof(provider));

        foreach (ToolCapabilityManifest manifest in manifests)
        {
            if (manifest is null)
                throw new ArgumentException($"DataAgent capability provider {provider.Name} returned a null manifest.", nameof(provider));
            if (string.IsNullOrWhiteSpace(manifest.Name))
                throw new ArgumentException($"DataAgent capability provider {provider.Name} returned a blank tool name.", nameof(provider));
            if (toolNames.Add(manifest.Name) == false)
                throw new InvalidOperationException($"Duplicate DataAgent tool capability: {manifest.Name}");
        }

        providers.Add(provider);
    }
}
```

- [ ] **Step 6: Run registry tests**

Run:

```powershell
& "C:\Users\hu shu\.dotnet\dotnet.exe" test Tests\Alife.Test.DataAgent\Alife.Test.DataAgent.csproj --no-restore --filter "FullyQualifiedName~DataAgentCapabilityRegistryTests" -v:minimal
```

Expected: all `DataAgentCapabilityRegistryTests` pass.

- [ ] **Step 7: Commit contracts and registry**

Run:

```powershell
git add sources/Alife.Function/Alife.Function.DataAgent/IDataAgentCapabilityProvider.cs sources/Alife.Function/Alife.Function.DataAgent/IDataAgentCapabilityRegistrar.cs sources/Alife.Function/Alife.Function.DataAgent/DataAgentCapabilityRegistry.cs Tests/Alife.Test.DataAgent/DataAgentCapabilityRegistryTests.cs
git commit -m "Add DataAgent capability registry"
```

---

### Task 3: Built-In Query And Analysis Providers

**Files:**

- Create: `sources/Alife.Function/Alife.Function.DataAgent/DataAgentQueryCapabilityProvider.cs`
- Create: `sources/Alife.Function/Alife.Function.DataAgent/DataAgentAnalysisCapabilityProvider.cs`
- Test: `Tests/Alife.Test.DataAgent/DataAgentCapabilityProviderTests.cs`

- [ ] **Step 1: Write failing provider tests**

Create `Tests/Alife.Test.DataAgent/DataAgentCapabilityProviderTests.cs`:

```csharp
using Alife.Function.DataAgent;
using Alife.Function.FunctionCaller;
using Alife.Function.Interpreter;

namespace Alife.Test.DataAgent;

[TestFixture]
public sealed class DataAgentCapabilityProviderTests
{
    [Test]
    public void QueryProviderDeclaresAndRegistersQueryTool()
    {
        RecordingRegistrar registrar = new();
        DataAgentQueryCapabilityProvider provider = new(new DataAgentService(CreateDatabasePath()));

        provider.Register(registrar);

        Assert.Multiple(() =>
        {
            Assert.That(provider.Name, Is.EqualTo(nameof(DataAgentQueryCapabilityProvider)));
            Assert.That(provider.ToolManifests.Select(manifest => manifest.Name), Is.EqualTo(new[] { "dataagent_query" }));
            Assert.That(provider.ToolManifests.Single().StateEffect, Is.EqualTo(ToolStateEffect.ReadsData));
            Assert.That(registrar.FunctionNames, Is.EqualTo(new[] { "dataagent_query" }));
        });
    }

    [Test]
    public void AnalysisProviderDeclaresAndRegistersAnalysisTools()
    {
        RecordingRegistrar registrar = new();
        DataAgentAnalysisService analysisService = new(
            new DataAgentService(CreateDatabasePath()),
            new InMemoryDataAgentAnalysisSessionStore());
        DataAgentAnalysisCapabilityProvider provider = new(analysisService);

        provider.Register(registrar);

        Assert.Multiple(() =>
        {
            Assert.That(provider.Name, Is.EqualTo(nameof(DataAgentAnalysisCapabilityProvider)));
            Assert.That(provider.ToolManifests.Select(manifest => manifest.Name), Is.EqualTo(new[]
            {
                "dataagent_analysis_start",
                "dataagent_analysis_continue",
                "dataagent_analysis_summarize",
                "dataagent_analysis_end"
            }));
            Assert.That(provider.ToolManifests.Single(manifest => manifest.Name == "dataagent_analysis_continue").Preconditions, Does.Contain(ToolCapabilityPrecondition.ActiveDataAgentAnalysisSession));
            Assert.That(registrar.FunctionNames, Is.EqualTo(new[]
            {
                "dataagent_analysis_start",
                "dataagent_analysis_continue",
                "dataagent_analysis_summarize",
                "dataagent_analysis_end"
            }));
        });
    }

    [Test]
    public void ProvidersUseSharedToolBrokerManifestSource()
    {
        DataAgentQueryCapabilityProvider query = new(new DataAgentService(CreateDatabasePath()));
        DataAgentAnalysisService analysisService = new(
            new DataAgentService(CreateDatabasePath()),
            new InMemoryDataAgentAnalysisSessionStore());
        DataAgentAnalysisCapabilityProvider analysis = new(analysisService);

        string[] providerTools = query.ToolManifests.Concat(analysis.ToolManifests)
            .Select(manifest => manifest.Name)
            .ToArray();
        string[] sharedTools = DataAgentToolCapabilityManifests.Create()
            .Select(manifest => manifest.Name)
            .ToArray();

        Assert.That(providerTools, Is.EqualTo(sharedTools));
    }

    static string CreateDatabasePath()
    {
        string directory = Path.Combine(TestContext.CurrentContext.WorkDirectory, "dataagent-capability-provider-tests");
        Directory.CreateDirectory(directory);
        string databasePath = Path.Combine(directory, $"{Guid.NewGuid():N}.sqlite");
        DataAgentSchemaInitializer.Initialize(databasePath);
        DataAgentFixtureImporter.Import(databasePath);
        return databasePath;
    }

    sealed class RecordingRegistrar : IDataAgentCapabilityRegistrar
    {
        readonly List<string> functionNames = [];

        public IReadOnlyList<string> FunctionNames => functionNames;

        public void RegisterXmlHandlerWithoutStaticDocument(XmlHandler handler, params string[] plainAreas)
        {
            functionNames.AddRange(handler.Functions.Select(function => function.Name));
        }
    }
}
```

- [ ] **Step 2: Run focused provider tests and verify they fail**

Run:

```powershell
& "C:\Users\hu shu\.dotnet\dotnet.exe" test Tests\Alife.Test.DataAgent\Alife.Test.DataAgent.csproj --no-restore --filter "FullyQualifiedName~DataAgentCapabilityProviderTests" -v:minimal
```

Expected: fail because the built-in provider classes do not exist.

- [ ] **Step 3: Add the query provider**

Create `sources/Alife.Function/Alife.Function.DataAgent/DataAgentQueryCapabilityProvider.cs`:

```csharp
using Alife.Function.FunctionCaller;
using Alife.Function.Interpreter;

namespace Alife.Function.DataAgent;

public sealed class DataAgentQueryCapabilityProvider(
    DataAgentService service,
    Action<string>? resultPublisher = null) : IDataAgentCapabilityProvider
{
    public string Name => nameof(DataAgentQueryCapabilityProvider);

    public IReadOnlyList<ToolCapabilityManifest> ToolManifests => DataAgentToolCapabilityManifests.Query;

    public void Register(IDataAgentCapabilityRegistrar registrar)
    {
        ArgumentNullException.ThrowIfNull(registrar);
        registrar.RegisterXmlHandlerWithoutStaticDocument(new XmlHandler(new DataAgentToolHandler(service, resultPublisher)));
    }
}
```

- [ ] **Step 4: Add the analysis provider**

Create `sources/Alife.Function/Alife.Function.DataAgent/DataAgentAnalysisCapabilityProvider.cs`:

```csharp
using Alife.Function.FunctionCaller;
using Alife.Function.Interpreter;

namespace Alife.Function.DataAgent;

public sealed class DataAgentAnalysisCapabilityProvider(
    DataAgentAnalysisService service,
    Action<string>? resultPublisher = null) : IDataAgentCapabilityProvider
{
    public string Name => nameof(DataAgentAnalysisCapabilityProvider);

    public IReadOnlyList<ToolCapabilityManifest> ToolManifests => DataAgentToolCapabilityManifests.Analysis;

    public void Register(IDataAgentCapabilityRegistrar registrar)
    {
        ArgumentNullException.ThrowIfNull(registrar);
        registrar.RegisterXmlHandlerWithoutStaticDocument(new XmlHandler(new DataAgentAnalysisToolHandler(service, resultPublisher)));
    }
}
```

- [ ] **Step 5: Run provider tests**

Run:

```powershell
& "C:\Users\hu shu\.dotnet\dotnet.exe" test Tests\Alife.Test.DataAgent\Alife.Test.DataAgent.csproj --no-restore --filter "FullyQualifiedName~DataAgentCapabilityProviderTests" -v:minimal
```

Expected: all provider tests pass.

- [ ] **Step 6: Commit built-in providers**

Run:

```powershell
git add sources/Alife.Function/Alife.Function.DataAgent/DataAgentQueryCapabilityProvider.cs sources/Alife.Function/Alife.Function.DataAgent/DataAgentAnalysisCapabilityProvider.cs Tests/Alife.Test.DataAgent/DataAgentCapabilityProviderTests.cs
git commit -m "Add built-in DataAgent capability providers"
```

---

### Task 4: Wire Providers Through DataAgentModuleService

**Files:**

- Create: `sources/Alife.Function/Alife.Function.DataAgent/DataAgentCapabilityRegistrar.cs`
- Modify: `sources/Alife.Function/Alife.Function.DataAgent/DataAgentModuleService.cs`
- Modify: `Tests/Alife.Test.DataAgent/DataAgentModuleServiceTests.cs`

- [ ] **Step 1: Update the module-service tests before implementation**

Replace `AwakeRegistersDataAgentToolHandler` in `Tests/Alife.Test.DataAgent/DataAgentModuleServiceTests.cs` with:

```csharp
[Test]
public void AwakeRegistersDataAgentCapabilityProviders()
{
    string source = ReadModuleSource();

    Assert.Multiple(() =>
    {
        Assert.That(source, Does.Contain("DataAgentCapabilityRegistry"));
        Assert.That(source, Does.Contain("DataAgentQueryCapabilityProvider"));
        Assert.That(source, Does.Contain("DataAgentAnalysisCapabilityProvider"));
        Assert.That(source, Does.Contain("DataAgentCapabilityRegistrar"));
        Assert.That(source, Does.Contain("provider.Register(registrar)"));
    });
}
```

Also add this test:

```csharp
[Test]
public void ModuleExposesRegisteredProviderAndToolNamesForDiagnostics()
{
    string source = ReadModuleSource();

    Assert.Multiple(() =>
    {
        Assert.That(source, Does.Contain("RegisteredCapabilityProviderNames"));
        Assert.That(source, Does.Contain("RegisteredCapabilityToolNames"));
        Assert.That(source, Does.Contain("capabilityRegistry.ProviderNames"));
        Assert.That(source, Does.Contain("capabilityRegistry.ToolNames"));
    });
}
```

- [ ] **Step 2: Run module-service tests and verify they fail**

Run:

```powershell
& "C:\Users\hu shu\.dotnet\dotnet.exe" test Tests\Alife.Test.DataAgent\Alife.Test.DataAgent.csproj --no-restore --filter "FullyQualifiedName~DataAgentModuleServiceTests" -v:minimal
```

Expected: fail because `DataAgentModuleService` still registers handlers directly.

- [ ] **Step 3: Add the runtime registrar adapter**

Create `sources/Alife.Function/Alife.Function.DataAgent/DataAgentCapabilityRegistrar.cs`:

```csharp
using Alife.Function.FunctionCaller;
using Alife.Function.Interpreter;

namespace Alife.Function.DataAgent;

public sealed class DataAgentCapabilityRegistrar(XmlFunctionCaller functionService)
    : IDataAgentCapabilityRegistrar
{
    public void RegisterXmlHandlerWithoutStaticDocument(XmlHandler handler, params string[] plainAreas)
    {
        ArgumentNullException.ThrowIfNull(handler);
        functionService.RegisterHandlerWithoutDocument(handler, plainAreas);
    }
}
```

- [ ] **Step 4: Wire DataAgentModuleService through the registry**

Replace the body of `AwakeAsync` in `sources/Alife.Function/Alife.Function.DataAgent/DataAgentModuleService.cs` with:

```csharp
public IReadOnlyList<string> RegisteredCapabilityProviderNames { get; private set; } = [];
public IReadOnlyList<string> RegisteredCapabilityToolNames { get; private set; } = [];

public override async Task AwakeAsync(AwakeContext context)
{
    await base.AwakeAsync(context);

    string databasePath = Path.Combine(AppContext.BaseDirectory, "DataAgent", "dataagent.sqlite");
    DataAgentSchemaInitializer.Initialize(databasePath);
    DataAgentFixtureImporter.Import(databasePath);

    DataAgentService service = new(databasePath);
    InMemoryDataAgentAnalysisSessionStore analysisSessionStore = new();
    DataAgentAnalysisService analysisService = new(service, analysisSessionStore);

    DataAgentCapabilityRegistry capabilityRegistry = new();
    capabilityRegistry.Add(new DataAgentQueryCapabilityProvider(service, Poke));
    capabilityRegistry.Add(new DataAgentAnalysisCapabilityProvider(analysisService, PublishAnalysisContext));

    DataAgentCapabilityRegistrar registrar = new(functionService);
    foreach (IDataAgentCapabilityProvider provider in capabilityRegistry.Providers)
        provider.Register(registrar);

    RegisteredCapabilityProviderNames = capabilityRegistry.ProviderNames;
    RegisteredCapabilityToolNames = capabilityRegistry.ToolNames;

    Prompt($"""
            DataAgent provides governed NL2SQL analytics over local Alife engineering evidence.

            ## Tool Broker contract
            - DataAgent XML tools are governed per turn by Tool Broker route state.
            - Only use DataAgent XML tools when they appear in current [tool_route_context].
            - The input to routed DataAgent query tools is natural language, not raw SQL.
            - Analysis summarize and end actions do not execute SQL.
            - If the current route does not show a DataAgent tool, do not call it from memory.
            """);

    void PublishAnalysisContext(string context)
    {
        functionService.UpdateDataAgentAnalysisRouteSessionFromContext(context);
        Poke(context);
    }
}
```

Keep the existing using directives and remove the no-longer-needed direct `XmlHandler` construction from this file.

- [ ] **Step 5: Run module-service tests**

Run:

```powershell
& "C:\Users\hu shu\.dotnet\dotnet.exe" test Tests\Alife.Test.DataAgent\Alife.Test.DataAgent.csproj --no-restore --filter "FullyQualifiedName~DataAgentModuleServiceTests" -v:minimal
```

Expected: all module-service tests pass.

- [ ] **Step 6: Run DataAgent tool and analysis handler regression tests**

Run:

```powershell
& "C:\Users\hu shu\.dotnet\dotnet.exe" test Tests\Alife.Test.DataAgent\Alife.Test.DataAgent.csproj --no-restore --filter "FullyQualifiedName~DataAgentToolHandlerTests|FullyQualifiedName~DataAgentAnalysisToolHandlerTests" -v:minimal
```

Expected: handler behavior remains unchanged.

- [ ] **Step 7: Commit module wiring**

Run:

```powershell
git add sources/Alife.Function/Alife.Function.DataAgent/DataAgentCapabilityRegistrar.cs sources/Alife.Function/Alife.Function.DataAgent/DataAgentModuleService.cs Tests/Alife.Test.DataAgent/DataAgentModuleServiceTests.cs
git commit -m "Wire DataAgent module through capability providers"
```

---

### Task 5: Required Readiness And Engineering Map Gates

**Files:**

- Modify: `sources/Alife.Function/Alife.Function.DataAgent/DataAgentReadiness.cs`
- Modify: `tools/check-dataagent-readiness.ps1`
- Modify: `tools/check-qchat-engineering-map.ps1`
- Modify: `Tests/Alife.Test.DataAgent/DataAgentReadinessTests.cs`
- Modify: `Tests/Alife.Test.DataAgent/DataAgentV15ReadinessTests.cs`

- [ ] **Step 1: Update readiness tests for V1.7 markers**

In `Tests/Alife.Test.DataAgent/DataAgentReadinessTests.cs`, update the expected core check count from `30` to `31` and add:

```csharp
Assert.That(checks.Select(check => check.Name), Does.Contain("CapabilityBoundaryPresent"));
```

Update the script summary expectation from:

```csharp
"  Summary: 42 required passed, 0 required missing"
```

to:

```csharp
"  Summary: 45 required passed, 0 required missing"
```

In `EngineeringMapDeclaresDataAgentReadinessAsRequired`, add:

```csharp
Assert.That(declaration, Does.Contain("CapabilityBoundaryPresent"));
```

- [ ] **Step 2: Add a V1.7 static readiness test**

Append this test to `Tests/Alife.Test.DataAgent/DataAgentV15ReadinessTests.cs`:

```csharp
[Test]
public void StaticReadinessScriptContainsV17CapabilityBoundaryMarkers()
{
    string repoRoot = FindRepoRoot(TestContext.CurrentContext.TestDirectory);
    string script = File.ReadAllText(Path.Combine(repoRoot, "tools", "check-dataagent-readiness.ps1"));

    Assert.Multiple(() =>
    {
        Assert.That(script, Does.Contain("CapabilityBoundaryPresent"));
        Assert.That(script, Does.Contain("IDataAgentCapabilityProvider"));
        Assert.That(script, Does.Contain("DataAgentCapabilityRegistry"));
        Assert.That(script, Does.Contain("DataAgentQueryCapabilityProvider"));
        Assert.That(script, Does.Contain("DataAgentAnalysisCapabilityProvider"));
        Assert.That(script, Does.Contain("DataAgentToolCapabilityManifests"));
    });
}
```

- [ ] **Step 3: Run readiness tests and verify they fail**

Run:

```powershell
& "C:\Users\hu shu\.dotnet\dotnet.exe" test Tests\Alife.Test.DataAgent\Alife.Test.DataAgent.csproj --no-restore --filter "FullyQualifiedName~DataAgentReadinessTests|FullyQualifiedName~DataAgentV15ReadinessTests" -v:minimal
```

Expected: fail because readiness code and scripts do not yet contain V1.7 markers.

- [ ] **Step 4: Add runtime readiness check**

In `sources/Alife.Function/Alife.Function.DataAgent/DataAgentReadiness.cs`, add this block after `ToolBrokerAuditLogPresent`:

```csharp
DataAgentCapabilityRegistry capabilityRegistry = new();
DataAgentService readinessService = new(databasePath);
DataAgentAnalysisService readinessAnalysisService = new(
    readinessService,
    new InMemoryDataAgentAnalysisSessionStore());
capabilityRegistry.Add(new DataAgentQueryCapabilityProvider(readinessService));
capabilityRegistry.Add(new DataAgentAnalysisCapabilityProvider(readinessAnalysisService));
checks.Add(capabilityRegistry.ProviderNames.SequenceEqual(new[]
           {
               nameof(DataAgentQueryCapabilityProvider),
               nameof(DataAgentAnalysisCapabilityProvider)
           }) &&
           capabilityRegistry.ToolNames.SequenceEqual(DataAgentToolCapabilityManifests.Create().Select(manifest => manifest.Name))
    ? Pass("CapabilityBoundaryPresent", "DataAgent query and analysis providers registered")
    : Fail("CapabilityBoundaryPresent", string.Join(",", capabilityRegistry.ToolNames)));
```

Add this using at the top if the compiler requires it:

```csharp
using Alife.Function.FunctionCaller;
```

- [ ] **Step 5: Add required script checks**

In `tools/check-dataagent-readiness.ps1`, add three required checks in the ToolBroker or Tool group:

```powershell
New-Check -Group "ToolBroker" -Name "CapabilityBoundaryPresent" -Passed ((Test-FileMarker "sources/Alife.Function/Alife.Function.DataAgent/IDataAgentCapabilityProvider.cs" @("IDataAgentCapabilityProvider", "ToolCapabilityManifest", "Register")) -and (Test-FileMarker "sources/Alife.Function/Alife.Function.DataAgent/DataAgentCapabilityRegistry.cs" @("DataAgentCapabilityRegistry", "Duplicate DataAgent capability provider", "Duplicate DataAgent tool capability"))) -Detail "DataAgent capability provider boundary markers"
New-Check -Group "ToolBroker" -Name "CapabilityProvidersPresent" -Passed ((Test-FileMarker "sources/Alife.Function/Alife.Function.DataAgent/DataAgentQueryCapabilityProvider.cs" @("DataAgentQueryCapabilityProvider", "DataAgentToolHandler", "DataAgentToolCapabilityManifests.Query")) -and (Test-FileMarker "sources/Alife.Function/Alife.Function.DataAgent/DataAgentAnalysisCapabilityProvider.cs" @("DataAgentAnalysisCapabilityProvider", "DataAgentAnalysisToolHandler", "DataAgentToolCapabilityManifests.Analysis"))) -Detail "DataAgent built-in capability providers"
New-Check -Group "ToolBroker" -Name "SharedToolManifestPresent" -Passed ((Test-FileMarker "sources/Alife.Function/Alife.Function.FunctionCaller/DataAgentToolCapabilityManifests.cs" @("DataAgentToolCapabilityManifests", "dataagent_query", "dataagent_analysis_end")) -and (Test-FileMarker "sources/Alife.Function/Alife.Function.FunctionCaller/ToolCapabilityRouter.cs" @("DataAgentToolCapabilityManifests.Create"))) -Detail "shared DataAgent Tool Broker manifest markers"
```

Keep the final group list as:

```powershell
foreach ($group in @("Core", "Schema", "Safety", "Query", "Context", "Planner", "Tool", "ToolBroker", "Analysis"))
```

- [ ] **Step 6: Add QChat engineering map check**

In `tools/check-qchat-engineering-map.ps1`, add one required Harness check:

```powershell
Add-Check -Group "Harness" -Name "DataAgent capability provider boundary" -Path "sources/Alife.Function/Alife.Function.DataAgent/DataAgentModuleService.cs" -Patterns @("DataAgentCapabilityRegistry", "DataAgentQueryCapabilityProvider", "DataAgentAnalysisCapabilityProvider", "RegisteredCapabilityProviderNames", "RegisteredCapabilityToolNames")
```

- [ ] **Step 7: Run readiness tests**

Run:

```powershell
& "C:\Users\hu shu\.dotnet\dotnet.exe" test Tests\Alife.Test.DataAgent\Alife.Test.DataAgent.csproj --no-restore --filter "FullyQualifiedName~DataAgentReadinessTests|FullyQualifiedName~DataAgentV15ReadinessTests" -v:minimal
```

Expected: readiness tests pass.

- [ ] **Step 8: Run readiness scripts**

Run:

```powershell
powershell -ExecutionPolicy Bypass -File tools\check-dataagent-readiness.ps1
powershell -ExecutionPolicy Bypass -File tools\check-qchat-engineering-map.ps1
```

Expected:

```text
DataAgent Readiness
  Summary: 45 required passed, 0 required missing

QChat Engineering Map
  Summary: 42 required passed, 0 required missing, 0 optional present, 0 optional missing
```

- [ ] **Step 9: Commit readiness gates**

Run:

```powershell
git add sources/Alife.Function/Alife.Function.DataAgent/DataAgentReadiness.cs tools/check-dataagent-readiness.ps1 tools/check-qchat-engineering-map.ps1 Tests/Alife.Test.DataAgent/DataAgentReadinessTests.cs Tests/Alife.Test.DataAgent/DataAgentV15ReadinessTests.cs
git commit -m "Require DataAgent V1.7 capability readiness"
```

---

### Task 6: Preserve Multi-Agent Coordination Norms In Documentation

**Files:**

- Modify: `docs/superpowers/specs/2026-06-29-dataagent-v1.7-capability-boundary-design.md`
- Modify: `docs/superpowers/plans/2026-06-29-dataagent-v1.6-v2-roadmap.md`
- Test: documentation and marker scan only

- [ ] **Step 1: Verify the V1.7 design has future multi-agent guidance**

Run:

```powershell
Select-String -Path docs\superpowers\specs\2026-06-29-dataagent-v1.7-capability-boundary-design.md -Pattern "permission validation|SQL generation|report interpretation|Scenario Knowledge Package|checkpoints|stream progress"
```

Expected: each concept appears in the Future Work section.

- [ ] **Step 2: Add roadmap cross-link**

In `docs/superpowers/plans/2026-06-29-dataagent-v1.6-v2-roadmap.md`, add this paragraph under the `## LangGraph Multi-Agent Orchestration Link` section:

```markdown
V1.7 capability metadata should also preserve the later multi-agent coordination norm: split permission validation, SQL generation, and report interpretation into dedicated nodes; normalize business terms through a pre-scheduling Scenario Knowledge Package; persist intermediate state through checkpoints; provide degradation paths for failed nodes; and stream linked-run progress to owner diagnostics or a frontend. These are V2.5/V3 orchestration requirements, not V1.7 runtime dependencies.
```

- [ ] **Step 3: Run documentation marker scan**

Run:

```powershell
Select-String -Path docs\superpowers\plans\2026-06-29-dataagent-v1.6-v2-roadmap.md -Pattern "Scenario Knowledge Package|checkpoints|permission validation|report interpretation"
```

Expected: all four markers appear.

- [ ] **Step 4: Commit documentation cross-link**

Run:

```powershell
git add docs/superpowers/specs/2026-06-29-dataagent-v1.7-capability-boundary-design.md docs/superpowers/plans/2026-06-29-dataagent-v1.6-v2-roadmap.md
git commit -m "Document DataAgent multi-agent coordination constraints"
```

---

### Task 7: Full Verification And Push

**Files:**

- Verify all V1.7 changes
- Push `dataagent-v1.7` to `alife-byastralfox`

- [ ] **Step 1: Run full DataAgent tests**

Run:

```powershell
& "C:\Users\hu shu\.dotnet\dotnet.exe" test Tests\Alife.Test.DataAgent\Alife.Test.DataAgent.csproj --no-restore -v:minimal
```

Expected: all DataAgent tests pass.

- [ ] **Step 2: Run full solution tests**

Run:

```powershell
& "C:\Users\hu shu\.dotnet\dotnet.exe" test Alife.slnx --no-restore -v:minimal
```

Expected: all projects pass. Existing skipped live tests remain skipped.

- [ ] **Step 3: Run required readiness scripts**

Run:

```powershell
powershell -ExecutionPolicy Bypass -File tools\check-dataagent-readiness.ps1
powershell -ExecutionPolicy Bypass -File tools\check-qchat-engineering-map.ps1
```

Expected:

```text
Summary: 45 required passed, 0 required missing
Summary: 42 required passed, 0 required missing, 0 optional present, 0 optional missing
```

- [ ] **Step 4: Confirm no V2 dependencies slipped in**

Run:

```powershell
Select-String -Path sources\Alife.Function\Alife.Function.DataAgent\*.cs -Pattern "Npgsql|Postgres|PostgreSQL|IDataAgentStore|LangGraph"
```

Expected: no matches.

- [ ] **Step 5: Push V1.7 branch**

Run:

```powershell
git status --short --branch
git push alife-byastralfox dataagent-v1.7
git ls-remote alife-byastralfox refs/heads/dataagent-v1.7
```

Expected: remote `refs/heads/dataagent-v1.7` points to the latest local commit.
