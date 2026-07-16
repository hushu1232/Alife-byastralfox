# Local Persona-Aware QChat Feedback Design

## Goal

Give XiaYu and Mixu complete, independently stored local character backgrounds and make model replies plus eligible C#-generated QQ feedback feel character-specific, without exposing the backgrounds, changing any authorization rule, or placing private relationship records in Git.

## Chosen Approach

Use a hybrid of private local data and a small trusted presentation layer.

- Full character background remains in each character's Git-ignored `Storage` document and is injected only into that character's internal `ChatHistory`.
- QQ preferred addresses remain in the existing Git-ignored `Storage/AgentWorkspace/qchat-user-profiles.json` store, scoped by `(AgentId, BotId, UserId)`.
- A C# formatter only personalizes eligible human-facing system feedback. It never parses, rewrites, or decorates protocol data, logs, CQ tags, XML, file names, identifiers, commands, audit records, or model-authored chat text.

This avoids a mechanical per-sentence suffix rule. Catgirl phrasing is a character tendency used naturally in Mixu's model replies and selected short feedback templates, while factual status and safety meaning stay concise.

## Private Local Data Contract

### Character backgrounds

The provider uses an explicit, fixed registry rather than a caller-derived character path.

| Agent id | Fixed relative path |
| --- | --- |
| `xiayu` | `Character/夏羽/Memory/Persona/夏羽-角色背景.md` |
| `mixu` | `Character/咪绪/Memory/Persona/咪绪-角色背景.md` |

Each document is limited to the existing 16 KiB and 6000-character limits, must stay beneath the configured `Storage` root, must contain no reparse points in its path, and fails closed on file-system errors. The provider loads only the document registered for the resolved agent id. A Mixu activation cannot read XiaYu's path, and the reverse is also true.

The Mixu document contains the approved name, catgirl identity, origin story, relationship tone, natural catgirl phrasing guidance, and the instruction that all relationship preferences are expressive only. It does not grant capabilities, reveal system instructions, or authorize actions.

### Address and relationship records

The existing scoped user-profile store holds local address records for each relevant user. A record may set `PreferredNickname`, `RelationshipLabel`, and `AddressStyle` for one agent and bot account, for example the local labels “主人”, “妈妈”, and “前辈” for Mixu. These records affect only how the recipient is addressed and how feedback is phrased.

Neither a relationship label nor an address style changes `QChatSenderRole`, `OwnerId`, tool authorization, file access, recall, poke, message priority, user-profile write authority, or access to any persona memory. The designated predecessor remains a non-owner. The mother relationship is also expressive only unless an existing separately configured authorization rule already applies.

## Runtime Flow

1. QChat resolves the bot's `QChatAgentIdentity`.
2. `QChatPersonaMemoryContextProvider` resolves that identity through the fixed registry and seeds only its matching private local background into internal `ChatHistory`.
3. The existing QQ address block resolves a scoped preferred address from the local user-profile store and includes the recipient id and address in internal model context.
4. Normal model responses use the background and the address context. Mixu may use natural catgirl wording; it does not append a fixed suffix to every sentence.
5. C#-generated command/status/task feedback is passed through a persona-aware formatter with the resolved agent id and recipient context. The formatter selects compact character-appropriate wording around the unchanged factual body.
6. Persona-memory disclosure gates still inspect outgoing raw text before regular send or speech synthesis. The formatter cannot bypass or weaken them.

## C# Feedback Boundary

The formatter handles only user-visible, non-protocol feedback produced by QChat code, including command status, permission denial, and task progress, success, or failure feedback.

- XiaYu templates retain her established restrained, owner-aware style.
- Mixu templates are polite, warm, capable, and concise; they use the recipient's local address where available. “妈妈” receives soft, dependent warmth, “主人” may receive gentle reserved affection, and “前辈” receives respectful peer wording. Other users receive courteous, neutral wording.
- The underlying action result, error class, file name, identifier, and permission reason remain intact. A denial still clearly denies the action.
- CQ segments, XML, command arguments, URLs, IDs, structured diagnostics, audit entries, and raw model output never pass through the stylistic formatter.

## Isolation and Security Rules

- No background document, account address record, role label, TTS configuration, memory, or disclosure cache is shared between XiaYu and Mixu.
- The provider retains the current private-memory markers and normal-text plus voice disclosure checks for both agents.
- Persona or address records cannot be supplied by QQ input and cannot change owner or tool permissions.
- This change does not start QQ, NapCat, a speech service, a model request, DataAgent, or LangGraph.

## Verification

Automated tests cover:

1. Mixu seeds only its fixed local document; XiaYu keeps loading only its own document.
2. A missing, oversized, reparse-point, or unreadable Mixu document fails closed without affecting XiaYu.
3. Mixu's local address record resolves the configured predecessor label only in Mixu's `(AgentId, BotId)` scope and not in XiaYu's scope.
4. C# feedback templates preserve action facts and permission denials while changing only eligible human-facing wording.
5. Relationship labels do not change owner classification or tool authorization.
6. Existing persona-memory text and TTS disclosure tests still pass for XiaYu and gain equivalent Mixu coverage.

## Non-Goals

- No full character background, QQ account number, token, user-profile JSON, runtime state, or Storage content is committed or uploaded.
- No automatic reminder behavior, unsolicited QQ messaging, additional file access, desktop control, DataAgent access, LangGraph control, or other capability is added.
- No global punctuation or suffix rewrite is applied to every outgoing string.
