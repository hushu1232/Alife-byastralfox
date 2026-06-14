# Agent Control Center Self-Configuration Design

Date: 2026-06-14

## Goal

Upgrade the Agent Control Center from a mostly observational dashboard into a configuration center that the owner can use directly and that the agent can operate through controlled internal tools.

The key behavior is:

- Low-risk self-configuration can be applied by the agent.
- High-risk or protected configuration can only become a pending proposal.
- Owner confirmation remains required before protected changes are applied.
- Every configuration read, proposal, blocked change, and applied change is recorded in the agent audit log.

This preserves the desired human-like self-adjustment behavior without turning the control center into a permissions bypass.

## Existing Context

The current project already has the foundations needed for this feature:

- `ConfigurationSystem` stores module configuration by target type and optional character root.
- `IConfigurable<T>` marks modules that support configuration.
- `AgentControlCenterService` already aggregates runtime state, task state, workspace proposals, command policy, issue reports, and audit entries.
- `AgentAuditLogService` provides persistent audit records.
- `AgentWorkspaceService` already uses a proposal-first pattern for risky file changes.
- `AgentPermissionPolicy` already separates owner authority, group behavior, risk level, and explicit confirmation.
- `AgentControlCenterServiceUI.razor` is already integrated into the existing Blazor/AntDesign module UI.

The implementation should reuse these systems instead of adding a separate configuration store or standalone browser panel.

## Design

### Configuration Model

Add an `AgentControlCenterConfig` class and make `AgentControlCenterService` implement `IConfigurable<AgentControlCenterConfig>`.

The config should include conservative defaults:

- `AllowAgentLowRiskSelfConfiguration`: whether the agent may apply low-risk config changes itself.
- `RequireOwnerConfirmationForHighRiskConfiguration`: whether high-risk config changes must be confirmed by the owner.
- `AllowMentionWakeup`: whether group mentions may wake the agent.
- `AllowPassiveGroupListening`: whether the agent may listen to group messages when not mentioned.
- `AllowProactiveChat`: whether the agent may initiate low-risk chat behavior.
- `ProactiveChatIntensity`: small bounded numeric value for proactive chat intensity.
- `MaxSelfConfigChangesPerHour`: rate limit for direct self-configuration.
- `LowRiskConfigurationKeys`: semicolon/newline separated allowlist.
- `ProtectedConfigurationKeys`: semicolon/newline separated protected list.

This config belongs to the control center module itself. Other modules can later expose their own config adapters, but this phase only writes control-center-owned agent behavior config.

### Agent Configuration Operations

Expose XML-callable operations through `AgentControlCenterService`:

- `agent_config_status`: show current self-configuration state and pending config proposals.
- `agent_config_apply`: attempt to apply a config key/value change.
- `agent_config_propose`: explicitly create a pending config proposal.
- `agent_config_confirmation_text`: build an owner confirmation command for a pending config proposal.
- `agent_config_apply_proposal`: apply a proposal after owner confirmation is provided by the existing permission layer.

The agent-facing operations do not directly bypass the project permission model. They perform local risk checks and write audit entries. High-risk writes stay pending until confirmed.

### Risk Model

Configuration keys are grouped into three categories:

- Low-risk: behavior tuning that can be safely self-adjusted, such as `AllowProactiveChat`, `ProactiveChatIntensity`, UI density, summaries, and low-risk status preferences.
- Protected: authority and safety boundaries, such as owner IDs, command allowlists, workspace roots, QQ high-risk operations, GitHub upload, and direct code execution.
- Unknown: any key not explicitly allowed. Unknown keys are treated as protected.

Direct agent changes are allowed only when:

- `AllowAgentLowRiskSelfConfiguration` is true.
- The key is in the low-risk allowlist.
- The key is not in the protected list.
- The value parses successfully.
- The rate limit is not exceeded.

If any rule fails, the operation is blocked or converted into a proposal depending on the requested operation.

### Proposal Model

Add an `AgentConfigurationChangeProposal` record containing:

- `Id`
- `Key`
- `RequestedValue`
- `CurrentValue`
- `Reason`
- `RiskLevel`
- `CreatedAt`
- `Actor`

Pending proposals are kept in memory for this phase, matching the current workspace proposal style. They are surfaced in the control center snapshot and UI. A later persistence pass can store pending proposals if needed.

Owner confirmation text should look like:

```text
confirm execute <agent_config_apply_proposal id="..." />
```

### Audit

Record all important configuration actions:

- `agent.config.status`
- `agent.config.applied`
- `agent.config.proposed`
- `agent.config.blocked`
- `agent.config.confirmed`
- `agent.config.failed`

Audit entries should include the actor, key, requested value summary, reason, risk level, and failure reason when applicable.

### UI

Update `AgentControlCenterServiceUI.razor` so the panel reads as a configuration center, not just a dashboard.

Add sections:

- Self-configuration status
- Conversation behavior configuration
- Pending configuration proposals
- Recent configuration audit

The UI can expose owner-friendly buttons and display confirmation commands, but it should not apply protected changes directly. Protected changes still need the owner confirmation command path.

### Data Flow

Low-risk self-change flow:

1. Agent calls `agent_config_apply`.
2. Service checks config policy.
3. Service parses and applies the value to `Configuration`.
4. Service persists through `ConfigurationSystem` when available, or updates the in-memory module config if a configuration system is not injected in tests.
5. Service records `agent.config.applied`.
6. Snapshot/UI shows updated config and audit.

High-risk proposal flow:

1. Agent calls `agent_config_propose` or attempts to apply a protected key.
2. Service creates `AgentConfigurationChangeProposal`.
3. Service records `agent.config.proposed` or `agent.config.blocked`.
4. UI displays the proposal and confirmation text.
5. Owner confirms through `confirm execute <agent_config_apply_proposal id="..." />`.
6. Existing permission controls allow or deny the call.
7. Service applies the proposal and records `agent.config.confirmed`.

### Testing

Add focused service-layer tests before implementation:

- `AgentControlCenterExposesConfigurationSnapshot`
- `AgentControlCenterAppliesAllowedLowRiskConfigurationAndAudits`
- `AgentControlCenterBlocksProtectedConfigurationWithoutOwnerConfirmation`
- `AgentControlCenterCreatesHighRiskConfigurationProposal`
- `AgentControlCenterBuildsOwnerConfirmationTextForConfigProposal`
- `AgentControlCenterAppliesConfirmedConfigProposal`

Run the focused framework tests first, then the full solution test command:

```powershell
& 'C:\Users\hu shu\.dotnet\dotnet.exe' test D:\Alife\Alife.slnx --no-restore
```

## Out of Scope

This phase does not add:

- A standalone browser control panel.
- Browser automation that physically clicks the control center.
- Direct agent mutation of owner IDs, command policies, workspace roots, QQ high-risk permissions, or GitHub upload settings.
- Persistent storage for pending config proposals beyond the current process.

## Acceptance Criteria

- The control center is configurable through `IConfigurable<AgentControlCenterConfig>`.
- The agent can inspect self-configuration with an XML tool.
- The agent can directly apply allowed low-risk configuration.
- Protected and unknown configuration keys are not directly applied by the agent.
- High-risk configuration changes can be proposed and displayed in the control center.
- Owner confirmation text is generated for pending config proposals.
- Applying a confirmed proposal updates configuration and records audit.
- Existing control center snapshot continues to include runtime state, tasks, workspace proposals, commands, issues, and audit.
- Focused and full test suites pass.
