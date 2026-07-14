# Persona-Aware Owner Engineering Replies

## Goal

Add a compact, deterministic reply contract for owner-facing engineering events.
The contract gives XiaYu and Mixu a short owner-appropriate introduction while
preserving engineering facts, verification results, uncertainty, and failures
without alteration.

## Scope

This change applies only to owner engineering events sent through the QChat
owner-event dispatch path. It does not alter ordinary owner command replies,
permission denials, model-generated chat, or the existing
`QChatCommandPersonaFormatter` behavior.

## Existing Boundary

`QChatCommandPersonaFormatter` currently gives command and event text a short
persona lead. It accepts only an agent id, sender role, and one unstructured
text value. It cannot distinguish engineering facts from a verification result
or an uncertainty statement, so it cannot make the required fact/persona
separation explicit or testable.

## Architecture

Introduce a small immutable `QChatOwnerEngineeringReply` contract and a
dedicated `QChatOwnerEngineeringReplyFormatter`. The contract separates the
stage from the factual payload:

- `Stage`: `Intake`, `Hypothesis`, `Blocked`, or `Complete`.
- `Facts`: confirmed engineering observations; required for a sendable reply.
- `Verification`: an optional, caller-provided test, build, or runtime result.
- `UncertaintyOrFailure`: an optional, caller-provided statement of missing
  evidence, uncertainty, or failure.

The formatter may add only an owner address and compact stage lead. It copies
the three supplied payload fields verbatim after trimming outer whitespace. It
does not infer success, change permissions, suppress a failure, or rewrite a
verification result.

## Data Flow

```text
owner engineering event
  -> QChatOwnerEngineeringReply
  -> QChatOwnerEngineeringReplyFormatter
  -> QChatOwnerEventDispatcher
  -> OneBot private owner message
```

The dispatcher is the sole initial integration point. Existing callers that
send generic owner events keep their existing `QChatCommandPersonaFormatter`
path until they opt into the typed engineering contract.

## Output Rules

- XiaYu owner replies begin with `术术` and Mixu owner replies begin with
  `主人`; other agents receive a neutral lead.
- `Intake` and `Hypothesis` may state the next bounded investigation step, but
  the supplied facts remain unchanged.
- `Blocked` emits a blocking lead and always includes any supplied
  `UncertaintyOrFailure` text. It never uses complete/success wording.
- `Complete` may use a completion lead, but it retains the exact supplied
  verification result and any supplied failure text.
- A reply with blank `Facts` is not sendable and formats as an empty string.
- Non-owner formatter input is rendered neutrally and never receives the
  owner-specific XiaYu or Mixu address.

## Error Handling

Formatting is pure and has no permission, event-store, or transport side
effects. Blank optional fields are omitted. Invalid/empty primary facts result
in no formatted text, allowing the caller to avoid a contentless outgoing
event. The formatter does not sanitize internal labels; callers must continue
to apply the existing visible-text policy at the normal outbound boundary.

## Tests

Add test-first coverage for the formatter and its dispatcher integration:

1. An owner XiaYu update includes the compact address while retaining exact
   facts and verification text.
2. A blocked update retains the supplied uncertainty/failure text and contains
   no complete-stage lead.
3. A complete update retains its exact verification statement rather than
   replacing it with an inferred result.
4. Mixu uses its owner address and does not receive XiaYu's address.
5. Non-owner input is neutral, and blank facts produce no sendable output.
6. An owner-event dispatcher test proves a typed engineering event uses the new
   formatter while generic event formatting is unchanged.

## Non-Goals

- No long persona prompt or model-facing persona injection.
- No changes to action authorization, test execution, diagnostic collection,
  or visible-text sanitization policy.
- No retroactive conversion of generic owner events to engineering events.
- No storage of engineering transcripts or private diagnostic payloads.

## Acceptance Criteria

- Persona styling is compact and limited to owner address plus stage lead.
- Facts, verification outcomes, uncertainty, and failures remain caller-owned
  text and are visible when supplied.
- Blocked output does not imply success or completion.
- Existing generic owner command/event formatting remains stable.
- Focused formatter and dispatcher tests demonstrate the contract.
