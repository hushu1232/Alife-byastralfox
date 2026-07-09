# DataAgent V3.14 Cross-Module Planner Manifests

V3.14 defines planner-only advisory manifests for selected non-DataAgent modules. These manifests describe what an agent may suggest, not what it may execute.

## Safety Markers

planner_only=true
cross_module_advisory=true
allows_execution=false
allows_state_write=false
allows_visible_text=false
fallback_required=true

## Planner-Only Capabilities

- qchat.intent_hint
- memory.candidate_summary
- browser.task_plan
- desktop.task_plan
- emotion.expression_hint
- deskpet.expression_hint

## Required Denied Markers

- qchat.send
- qq.ingress
- tool.execute
- sql.execute
- checkpoint.write
- memory.write
- browser.execute
- desktop.execute
- file.write
- voice.output
- tts.output
- audit.write
- progress.write
- diagnostics.write

## Boundary

The manifests are for planning, ranking, summarizing, or hinting only. C# remains responsible for route decisions, visible text publication, memory writes, browser/desktop execution, speech output, DeskPet runtime calls, audit, progress, and diagnostics writes.
