# AstralFox Alife Domain

AstralFox Alife runs one character through multiple interaction surfaces while preserving a single identity and strict channel boundaries.

## Language

**Character Core**:
The stable identity shared by every runtime of the same character: persona, relationship definitions, approved knowledge, and durable facts.
_Avoid_: Shared session, global prompt, copied persona

**Channel Runtime**:
An independently running Alife interaction surface for a character, such as QQ or desktop. It owns its transport, short-term context, online state, and channel-specific capabilities.
_Avoid_: Character instance, bot copy

**Channel Session**:
The conversation continuity between one Channel Runtime and one peer or local interaction thread. It never crosses channels merely because the Character Core is shared.
_Avoid_: Global chat history, shared conversation

**Embodiment Adapter**:
The presentation boundary that turns body commands into visible behavior and reports physical interactions back to its Channel Runtime. It does not own AI reasoning, persona, or memory.
_Avoid_: AIRI agent, second AI

**Shared Fact**:
An approved, durable piece of knowledge belonging to the Character Core and safe for reuse across channels. Raw messages and channel diagnostics are not Shared Facts.
_Avoid_: Chat log, full context

**Life Event**:
A bounded description of something the character experienced through a Channel Runtime. A Life Event is only a candidate for durable memory until it is explicitly promoted to a Shared Fact.
_Avoid_: Raw event payload, message transcript