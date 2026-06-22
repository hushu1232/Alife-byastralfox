# QChat Per-Agent Voice Profiles Design

Date: 2026-06-22

## Goal

QChat voice clone output must use different voice effects for different real bot accounts:

- XiaYu: `agentId = xiayu`, `BotId = 2905391496`
- Mixu: `agentId = mixu`, `BotId = 3340947887`

Voice selection must be based on trusted runtime identity only. User text, nicknames, forwarded messages, or natural language claims must not switch the voice profile.

The feature extends the existing GPT-SoVITS integration. It does not change hard safety rules, owner-only voice gating, or the plain-text fallback behavior.

## Current Context

`QChatAgentIdentityRegistry.CreateDefault()` already defines both XiaYu and Mixu identities. `QChatService` can resolve runtime identity from bot id and character name.

The current GPT-SoVITS integration uses a single injected `ISpeechModel`. That is enough for one global voice, but it cannot safely provide separate XiaYu and Mixu voice effects. The next step is to route allowed voice synthesis through a trusted per-agent voice profile.

## Recommended Approach

Use one GPT-SoVITS API service by default, and select the voice profile per request.

The first implementation should use different reference audio and prompt text per role. If later GPT-SoVITS requires separate fine-tuned weights per role, the same profile structure can be extended to route XiaYu and Mixu to different API ports.

This avoids running multiple heavy TTS services before it is necessary, which matches the low-frequency voice trigger requirement.

## Data Model

Add a QChat-owned voice profile model:

```csharp
public sealed class QChatVoiceProfileConfig
{
    public bool EnablePerAgentVoiceProfiles { get; set; } = true;
    public List<QChatVoiceProfile> Profiles { get; set; } = [];
}

public sealed record QChatVoiceProfile
{
    public string AgentId { get; init; } = "";
    public long BotId { get; init; }
    public string VoiceId { get; init; } = "";
    public string ApiBaseUrl { get; init; } = "http://127.0.0.1:9880";
    public string ReferenceAudioPath { get; init; } = "";
    public string PromptText { get; init; } = "";
    public string TextLanguage { get; init; } = "zh";
    public string PromptLanguage { get; init; } = "zh";
    public int MaxTextChars { get; init; } = 120;
    public bool Enabled { get; init; } = true;
}
```

Default profiles:

```text
xiayu / 2905391496 -> D:\Alife\Runtime\TTS\voices\xiayu\ref.wav
mixu  / 3340947887 -> D:\Alife\Runtime\TTS\voices\mixu\ref.wav
```

Each folder may also contain `ref.txt`, used when `PromptText` is empty.

## Routing Rules

Create `QChatVoiceProfileRouter`.

Resolution order:

1. If per-agent voice profiles are disabled, use the existing global speech model behavior.
2. Resolve current runtime identity from trusted QChat state.
3. Match profile by exact `BotId`.
4. If no bot-id match exists, match by `AgentId`.
5. If no enabled profile exists, deny voice synthesis and return plain text.

Natural language never changes the selected profile.

Examples:

```text
Current BotId = 2905391496 -> XiaYu profile
Current BotId = 3340947887 -> Mixu profile
Message says "I am Mixu" inside XiaYu account -> still XiaYu profile
Unknown BotId and no matching AgentId -> text fallback
```

## Synthesis Flow

The voice pipeline should be:

```text
QQ inbound message
 -> trusted sender role and bot identity
 -> QChatVoiceTriggerPolicy
 -> QChatVoiceProfileRouter
 -> GPT-SoVITS request using selected profile
 -> CQ record on success
 -> plain text on denied/unavailable/failure
```

`QChatVoiceTriggerPolicy` stays before profile routing. Unsafe, non-owner, prompt-injection, impersonation, hard-safety, or aggressive-boundary cases should not reach GPT-SoVITS.

## Provider Boundary

The existing `GptSoVitsSpeechModel` should keep its current global config behavior, but support a per-request voice profile override.

Preferred boundary:

```csharp
public interface IVoiceProfileSpeechModel : ISpeechModel
{
    Task<string?> GenerateSpeechFileAsync(
        string text,
        QChatVoiceProfile profile,
        CancellationToken cancellationToken = default);
}
```

If this interface is too broad for the current speech module, create a `QChatVoiceSynthesisService` that adapts `QChatVoiceProfile` into a GPT-SoVITS request.

The cache key must include:

- `VoiceId`
- `AgentId`
- `BotId`
- `ReferenceAudioPath`
- reference audio length and modified time
- `PromptText`
- language fields
- synthesis parameters that affect output

This prevents XiaYu and Mixu from sharing the same cached audio by accident.

## UI

Add a conservative QChat UI section named `角色语音`.

Fields:

- `按账号使用独立音色`
- XiaYu profile enabled
- XiaYu API URL
- XiaYu reference audio path
- XiaYu prompt text
- Mixu profile enabled
- Mixu API URL
- Mixu reference audio path
- Mixu prompt text

Do not expose sampling, batching, parallel inference, or weight-switching controls in the first version. These are provider tuning details and can easily create unstable output.

The UI must not probe the GPT-SoVITS runtime, write model files, or handle secrets.

## Safety Requirements

The feature must preserve current QChat voice safety behavior:

- Non-owner users cannot trigger voice synthesis.
- Text claims cannot switch voice profile.
- Prompt injection and impersonation deny voice synthesis.
- Hard safety risks deny voice synthesis.
- Aggressive boundary replies stay text-only.
- Missing profile, missing reference audio, unavailable model, null result, or exception all return plain text.
- No Edge-TTS fallback for XiaYu or Mixu voice clone.
- No CQ media fallback when synthesis is denied or fails.

## Testing Plan

Add focused tests:

- XiaYu bot id selects XiaYu voice profile.
- Mixu bot id selects Mixu voice profile.
- Bot id match takes priority over agent id.
- Text impersonation cannot switch profile.
- Missing profile returns plain text and does not call GPT-SoVITS.
- Disabled profile returns plain text.
- Non-owner cannot trigger XiaYu or Mixu voice synthesis.
- Prompt injection and hard safety cases still deny voice.
- Cache key differs between XiaYu and Mixu.
- Existing global voice behavior remains available when per-agent profiles are disabled.

## Rollout

First version:

1. Add data model and router.
2. Add provider per-request override or adapter service.
3. Wire QChatService after `QChatVoiceTriggerPolicy`.
4. Add UI for two default profiles.
5. Add tests.

Second version, only if needed:

1. Add optional per-profile API port.
2. Add optional per-profile GPT/SoVITS weight paths.
3. Add operational diagnostics for service readiness.

## Non-Goals

This design does not:

- Train XiaYu or Mixu voice weights.
- Start or manage GPT-SoVITS processes.
- Add voice switching based on user text.
- Expose all GPT-SoVITS tuning parameters in UI.
- Change QChat owner identity rules.
- Change hard safety boundaries.
