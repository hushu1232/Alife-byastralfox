# Persona Test Fixtures Design

## Goal

Make QChat and Framework persona boundary tests stable in isolated worktrees and CI without committing runtime `Storage/Character` state.

The current tests validate important persona and VirtualWorld safety contracts, but they read directly from repository-root `Storage/Character`. That directory is intentionally ignored because it is runtime state and can contain private prompts, account configuration, local paths, voice references, and other machine-specific settings. In a clean worktree, the tests fail before they can validate the contracts.

This design moves required persona verification inputs into source-controlled, sanitized test fixtures.

## Current Problem

The failing tests are:

- `Tests/Alife.Test.QChat/QChatPersonaBoundaryTests.cs`
- `Tests/Alife.Test.Framework/CharacterPersonaRuntimeConfigTests.cs`

They read files like:

```text
Storage/Character/<XiaYu>/index.json
Storage/Character/<XiaYu>/Configuration/Alife.Function.QChat.QChatService.json
Storage/Character/<XiaYu>/Configuration/Alife.Function.VirtualWorld.VirtualWorldService.json
Storage/Character/<Mao>/index.json
Storage/Character/<Mao>/Configuration/Alife.Function.QChat.QChatService.json
Storage/Character/<Mao>/Configuration/Alife.Function.VirtualWorld.VirtualWorldService.json
```

Those files exist on the developer machine but are not tracked. The repository `.gitignore` excludes root runtime state with broad rules, so clean worktrees correctly do not receive those files.

## Decision

Add source-controlled sanitized fixtures under:

```text
Tests/Fixtures/Character/<XiaYu>/index.json
Tests/Fixtures/Character/<XiaYu>/Configuration/Alife.Function.QChat.QChatService.json
Tests/Fixtures/Character/<XiaYu>/Configuration/Alife.Function.VirtualWorld.VirtualWorldService.json
Tests/Fixtures/Character/<Mao>/index.json
Tests/Fixtures/Character/<Mao>/Configuration/Alife.Function.QChat.QChatService.json
Tests/Fixtures/Character/<Mao>/Configuration/Alife.Function.VirtualWorld.VirtualWorldService.json
```

The fixtures should contain only the marker fields and marker phrases required by tests. They should not copy full runtime persona text.

Update the tests to resolve fixture paths from repository root `Tests/Fixtures/Character` instead of `Storage/Character`.

## Non-Goals

This work does not change runtime persona loading.

This work does not commit actual local `Storage/Character` files.

This work does not skip persona tests when fixtures are missing.

This work does not add real QQ account secrets, API keys, authorization tokens, voice model credentials, or local private paths.

This work does not change QChat runtime behavior, VirtualWorld runtime behavior, DataAgent V2 store behavior, or plugin loading.

## Architecture

Use a test-only fixture root:

```text
FindRepositoryRoot()
  -> Tests/Fixtures/Character
  -> character directory
  -> index.json
  -> Configuration/*.json
```

`QChatPersonaBoundaryTests` should expose helper methods:

```csharp
static string GetCharacterFixtureDirectory(string characterName)
static string GetCharacterFixturePath(string characterName)
static string GetCharacterQChatConfigPath(string characterName)
static string GetCharacterVirtualWorldConfigPath(string characterName)
```

`CharacterPersonaRuntimeConfigTests` should use the same fixture root shape, with local helper methods if no shared test utility exists. A shared helper project is not needed for this narrow fix.

## Fixture Content Rules

Sanitized fixtures should be minimal:

- include only fields read by tests;
- include marker phrases that express the safety contract;
- include fake or empty secrets;
- include stable fake local voice paths rather than real machine paths;
- include module lists required for `ModuleSystem.GetModule(...)` resolution tests;
- include VirtualWorld `AllowCharacterInteractionDelivery: false` for both local bot personas.

The fixture files can contain Chinese contract marker phrases because the tests assert those phrases. If encoding is a risk, use JSON Unicode escapes in fixture values. The tests should parse JSON with `System.Text.Json`, so escaped strings compare as normal Unicode strings.

## Runtime Boundary

Runtime `Storage/Character` remains ignored and machine-local. A future live/local-only test can be added behind an environment variable such as:

```text
ALIFE_PERSONA_RUNTIME_STORAGE
```

That optional test would validate a developer's actual local runtime persona configuration. It must not become a default required test because it depends on private machine state.

## Testing Strategy

Use TDD:

1. Add tests proving persona boundary tests resolve paths from `Tests/Fixtures/Character`, not `Storage/Character`.
2. Run focused QChat/Framework tests and observe missing fixture failures.
3. Add minimal sanitized fixture JSON files.
4. Run targeted tests:

```powershell
& "C:\Users\hu shu\.dotnet\dotnet.exe" test Tests\Alife.Test.QChat\Alife.Test.QChat.csproj --no-restore --filter "FullyQualifiedName~QChatPersonaBoundaryTests" -v:minimal
& "C:\Users\hu shu\.dotnet\dotnet.exe" test Tests\Alife.Test.Framework\Alife.Test.Framework.csproj --no-restore --filter "FullyQualifiedName~CharacterPersonaRuntimeConfigTests" -v:minimal
```

5. Run solution verification:

```powershell
& "C:\Users\hu shu\.dotnet\dotnet.exe" test Alife.slnx --no-restore -v:minimal
```

## Acceptance Criteria

- `QChatPersonaBoundaryTests` no longer reads `Storage/Character`.
- `CharacterPersonaRuntimeConfigTests` no longer reads `Storage/Character` for active persona tests.
- New fixture files are tracked under `Tests/Fixtures/Character`.
- No fixture contains `sk-`, `Authorization`, `Bearer `, `OneBotToken`, or a real API key field value.
- QChat persona boundary tests pass in the isolated worktree.
- Framework persona runtime config tests pass in the isolated worktree.
- Full solution test no longer fails because of missing `Storage/Character` files.

## Interview Value

This change is a HarnessEngineering improvement: runtime persona state and source-controlled test evidence become separate. It shows that the project can keep strong persona safety tests without relying on private developer-machine state.

In interview terms:

```text
I separated runtime persona configuration from source-controlled verification fixtures. The tests still validate persona safety boundaries, VirtualWorld delivery policy, and required module wiring, but CI no longer depends on untracked local Storage state. This improved reproducibility while preserving privacy and runtime flexibility.
```
