# QChat Human Conversation and Semantic Tooling Design

## Goal

Make QChat characters sound less scripted while preserving reliable, permission-gated tool use without keyword-triggered capability discovery.

## Scope

- Establish one compact, current runtime persona source for XiaYu.
- Stop seeding the full persona Markdown as a user message in every new chat.
- Keep the complete local persona document protected and retrieve bounded facts only when the model requests an approved category.
- Remove static QQ guidance that conflicts with the safe current-session fallback.
- Reduce dynamic turn context to current facts rather than forced emotional actions.
- Mark internal character state separately from untrusted external data and bound dynamic context.
- Offer bounded conversation/persona reads by semantic model choice, not user-keyword matching.
- Preserve C# gates, account identity checks, OneBot routing, DataAgent's audit-only boundary, and existing XML tool execution.

## Non-goals

- No DataAgent or LangGraph control over routing, permissions, sending, or tool execution.
- No automatic QR login, NapCat change, real QQ action, or credential migration.
- No blanket exposure of all XML functions to every conversation turn.
- No removal of output safety filtering or high-risk confirmation gates.
- No provider-specific native function-calling migration in this change; XML remains the execution syntax.

## Runtime model

```text
compact stable persona + QQ delivery rule + safety boundary
    + per-turn verified facts + bounded recent context + allowed scoped reads
    -> model chooses direct reply or one bounded read
    -> C# validates and reads
    -> result feedback
    -> model writes one natural QQ reply
```

The model never receives a forced `sharp_pushback`, `dependent`, `jealous`, or fixed reply phrase. Code decides whether a reply may be sent, whether a tool is allowed, and whether a confirmation is required. The model decides wording and whether a permitted bounded read is useful.

## Design

### Persona and memory

`QChatPersonaMemoryContextProvider.TrySeed` stops adding the whole approved Markdown document to `ChatHistory`. The document remains protected local storage and remains available through `QChatPersonaFactProvider`.

The stable prefix contains only identity, owner/non-owner social tendency, expression constraints, truthful-tool-result behavior, and non-disclosure. The effective XiaYu append prompt must match the current 19-year-old, polite-but-distant non-owner profile. A known obsolete local prompt override and approved persona Markdown are updated only when their text matches the known legacy revision; local state is never committed.

### Dynamic context

`QChatConversationCognition` emits only relationship, message intent, mention/wake state, and reply eligibility. It no longer emits compulsory social actions, attachment, desire, jealousy, or emotional-distance fields.

`QChatPromptEnvelope` receives an explicit trust classification and maximum content length. Character state and approved persona data are trusted internal context; messages, image OCR, web/research data, and recall are external data. Large external blocks are truncated before wrapping.

Address context is included only when a non-empty preferred address or address style exists.

### Semantic bounded reads

When safe data is available, the normal QChat model call sees a concise capability offer for:
- earlier context from the current QQ conversation;
- approved persona relationship;
- approved persona origin;
- approved persona speech style;
- approved persona behavior boundary;
- approved persona confirmed preference.

The offer describes purpose and privacy boundary. The model chooses one capability by structured marker; the selection is semantic model judgment, not a user phrase matcher. The marker is intercepted before QQ delivery, so normal XML tools remain available for direct replies. C# maps a marker to the bounded provider category, validates it, returns a bounded feedback block, and asks the model for a natural final reply.

### Static tool and output behavior

QChat's static prompt is reduced to one delivery rule and no longer commands an unconditional guide lookup or XML-only delivery. Existing XML functions remain registered and execution policy remains authoritative.

`QChatExperienceSanitizer` continues to remove actual internal routing/identity leaks, but stops broad persona-word rewriting that changes ordinary natural text. `QChatReplyLayoutNormalizer` preserves a single intentional ordinary line break and only collapses excess fragmented lines.

## Acceptance

1. A fresh XiaYu session does not contain the entire persona Markdown, its path, or its raw protected body.
2. The compact stable prompt and local effective configuration consistently describe the current XiaYu profile.
3. Dynamic cognition contains no forced emotional/action labels.
4. Safe scoped reads are offered without user keyword gating and only return bounded, approved data.
5. Tool results are followed by a natural final reply, never internal marker/XML/protocol text.
6. Non-owner and high-risk behavior remain enforced by code; DataAgent/LangGraph retain audit-only roles.
7. QChat focused tests cover paraphrased context/persona requests, no unnecessary capability use, output layout preservation, trust envelopes, and legacy prompt migration behavior.
