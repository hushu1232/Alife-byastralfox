# Agent Control Center Design

## Goal

Add an AstralFox Agent control center inside the existing Alife UI so the bot's self-state, task progress, audit trail, allowed commands, error report, and workspace change proposals can be inspected from one practical panel.

This is not a standalone browser application. The browser mockup is only a design preview. The real feature will be implemented in the existing Blazor and AntDesign module UI structure.

## Integration

The first version should follow the existing module editor pattern:

- Add a dedicated Agent control module under `Alife.Function.MessageFilter`.
- Give the module an `EditorUI` Razor component, for example `AgentControlCenterServiceUI.razor`.
- Render it through the existing `ModuleConfigUI` / `DynamicComponent` flow.
- Keep the UI inside the app's current module management/configuration surface.
- Do not add a separate web server, separate browser page, or unrelated navigation system in the first version.

This keeps the implementation consistent with existing modules such as `QChatServiceUI.razor`, `QZoneServiceUI.razor`, and `MessageFilterServiceUI.razor`.

## Layout

Use the selected "runtime console" layout.

Top area:

- Title: Agent Control Center / AstralFox Agent.
- Short subtitle explaining that it shows runtime state, tasks, audit, commands, and workspace proposals.
- Refresh action.

Metric row:

- Runtime status: chatting or idle, pending poke count, chat history count.
- Last error: current `ChatRuntimeState.LastError` summary.
- Task status: latest task status and goal.
- Risk status: recent high-risk audit count and pending workspace proposals count.

Main left column:

- Current task panel:
  - Latest task id, goal, status, steps, and recent task events.
  - First version can be read-only if wiring task mutation buttons would require extra safety decisions.
- Workspace proposals panel:
  - Pending proposal id, relative path, created time, and diff-style preview.
  - First version may expose view-only proposal details.
  - Applying proposals must remain a high-risk action and must not bypass owner confirmation policy.

Right column:

- Allowed commands:
  - Show command id, description, executable, timeout.
  - First version is read-only or exposes only low-risk copy/view actions.
- Recent audit:
  - Show timestamp, action, actor, risk, succeeded, and error summary.
  - Highlight high-risk failed entries.
- Error report:
  - Show combined runtime error, failed audit entries, and unhealthy modules from `AgentIssueReportService`.

## Data Sources

Reuse existing services instead of inventing duplicate state:

- `AgentDiagnosticsService` for runtime state and capability/health reporting.
- `AgentIssueReportService` for combined error report.
- `AgentProjectStatusService` for workspace roots, allowed commands, and recent audit.
- `AgentTaskService` for task state.
- `AgentAuditLogService` for recent audit entries.
- `AgentWorkspaceService` for allowed roots and pending workspace proposals.

If a service currently does not expose a read API needed by the UI, add a small read-only method rather than reading private fields or parsing rendered text.

## Safety

The UI must not become a shortcut around the security model.

- High-risk command execution remains gated by `AgentPermissionPolicy` and XML high-risk authorization.
- Applying workspace proposals remains high-risk.
- The first version should prefer inspection, visibility, and status refresh.
- Any future UI action that mutates files, runs commands, uploads files, sends QQ messages, or applies proposals must route through the same owner-confirmation model.

## Visual Style

Follow the existing AntDesign module UI style:

- Use compact cards with 8px radius.
- Use clear sections, tight headings, and dense information layout.
- Avoid nested cards.
- Keep copy operational and concise.
- Preserve the AstralFox development mark where useful.

The interface should feel like an operations console for a living agent, not a marketing dashboard.

## First Implementation Scope

Implement the first version as a read-oriented control center:

- Agent state metrics.
- Latest task summary.
- Recent audit list.
- Allowed command list.
- Error report summary.
- Workspace proposal list or count, depending on what `AgentWorkspaceService` exposes.

Out of scope for first version:

- Direct command execution from UI.
- Direct proposal application from UI.
- Editing task lifecycle from UI.
- New global navigation page.
- Standalone browser app.

## Testing

Add focused tests for service-level read APIs where needed:

- pending workspace proposals can be listed without mutating files;
- task state remains available for UI after persistence reload;
- project status includes configured command executable paths;
- audit history reload feeds recent audit display.

Build verification:

- Run focused tests for any new service APIs.
- Run `dotnet test D:\Alife\Alife.slnx --no-restore` before reporting completion.

## Open Decisions

None for the first version. The implementation should be integrated into the existing module UI and remain read-oriented unless the owner explicitly approves high-risk UI actions later.
