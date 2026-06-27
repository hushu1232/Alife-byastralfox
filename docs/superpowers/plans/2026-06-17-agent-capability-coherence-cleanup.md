# Agent Capability Coherence Cleanup Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Make the current QQ bot's existing code and runtime configuration more self-consistent without adding new capabilities.

**Architecture:** Keep the work limited to text/context surfaces that directly affect model cognition and to the current character's enabled module list. Do not redesign QChat, QZone, permissions, or agent tools in this pass. Source files must be read as UTF-8; apparent mojibake from Windows PowerShell default decoding is not treated as source corruption.

**Tech Stack:** C#/.NET 9, NUnit, Alife module system, JSON runtime character configuration.

---

### Task 1: Confirm Core Prompt Text Encoding

**Files:**
- Inspect: `Tests/Alife.Test.QChat/QChatServiceAdapterTests.cs`
- Inspect: `Tests/Alife.Test.Framework/AgentCapabilityServiceTests.cs`
- Inspect: `Tests/Alife.Test.Framework/MessageFilterContextComposerTests.cs`
- Inspect: `sources/Alife.Function/Alife.Function.QChat/QChatService.cs`
- Inspect: `sources/Alife.Function/Alife.Function.MessageFilter/AgentControlCenterService.cs`
- Inspect: `sources/Alife/Alife.Framework/Models/StreamingOutputPolicy.cs`

- [x] Read core files with explicit UTF-8 decoding.
- [x] Confirm QChat prompt, streaming sentence boundaries, and proactive mode labels are readable in source.
- [x] Treat prior mojibake output as PowerShell decoding artifact, not production source corruption.
- [x] Avoid production text churn.

### Task 2: Align Current Character Module List With Existing Agent Capabilities

**Files:**
- Modify: `Storage/Character/真央/index.json`
- Modify: `Tests/Alife.Test.Framework/CharacterPersonaRuntimeConfigTests.cs`

- [x] Inspect the current enabled module list.
- [x] Add existing Agent modules that are already implemented and safe to load for self-knowledge/control-center visibility.
- [x] Keep QZone disabled unless separately requested, because it depends on external bridge action compatibility.
- [x] Enable QChatRelationCache independently so self-model/context contribution can see QQ relation state.
- [x] Validate the JSON parses and module names resolve.

### Task 3: Verification

**Files:**
- No production files beyond Task 1 and Task 2.

- [x] Run focused QChat tests.
- [x] Run focused Framework tests.
- [x] Run `dotnet build D:\Alife\Alife.slnx --no-restore` if focused tests pass.
- [x] Report exact verification results and any remaining gaps.
