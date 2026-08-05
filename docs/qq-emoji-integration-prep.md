# QQ Emoji Integration Preparation

## Purpose

Prepare the local Alife runtime for the external QQEmoji module without
vendoring third-party source code, images, or the ChineseBQB index into this
repository.

The module is an optional runtime plugin. It provides a named local emoji
library, optional online search, and QChat image delivery through the existing
`qimage` tool.

## Deployment Status

The reviewed QQEmoji v1.5.1 source is installed under the ignored runtime paths
`Storage/Plugins/Alife.Plugin.QQEmoji` and
`Storage/PluginsDebug/Alife.Plugin.QQEmoji`. The plugin recognizes the current
`[QChatService]` marker. Online BQB search, Tencent search, automatic online
image caching, and Tencent automatic persistence are all disabled by default.
No third-party image, ChineseBQB index, or local emoji asset is deployed.

## Scope

This preparation covers:

- an English filename convention for local emoji assets;
- the supported capability and safety boundary;
- the minimum plugin compatibility change and acceptance checks;
- optional FunctionCaller/DataAgent governance.

This preparation does not cover:

- importing third-party image assets, the QQEmoji source, generated UI, or the
  ChineseBQB index into Git;
- bypassing QChat, OneBot, or `qimage` authorization;
- unrestricted URL downloading or automatic permanent caching.

## Runtime Layout

Keep runtime emoji files outside Git:

```text
Storage/
  QQEmojis/
    happy_cat_clapping_praise.gif
    sad_dog_teary_comfort.png
```

`Storage` is runtime state. Do not commit images, downloaded caches, plugin
release archives, or account/session state.

After every `Build.ps1` or `Publish.ps1`, restore the externally maintained,
locally adapted QQEmoji source with:

```powershell
powershell -ExecutionPolicy Bypass -File D:\Alife\tools\install-qqemoji-runtime.ps1 `
  -SourceRoot <adapted-qqemoji-source-root> `
  -StorageRoot D:\Alife\Storage\account-b
```

The installer stages and replaces only the runtime plugin files. It rejects an
unadapted upstream source that lacks the local `saveimage` governance and
`SearchEmojis` integration markers.

## English Filename Convention

Use lowercase ASCII labels separated by underscores:

```text
emotion_subject_action_context[_restriction].extension
```

Examples:

```text
happy_cat_clapping_praise.gif
laughing_panda_facepalm_teasing.jpg
sad_dog_teary_comfort.png
speechless_cat_deadpan_silence.webp
shy_girl_blushing_thanks.gif
shocked_panda_wide_eyes_gossip.jpg
cheer_dog_fist_bump_encouragement.png
goodnight_cat_blanket_end_chat.gif
sarcastic_panda_clapping_close_friends.jpg
flirty_cat_blushing_private_only.gif
```

Use a small, stable vocabulary rather than synonyms.

| Field | Preferred labels |
| --- | --- |
| Emotion | `happy`, `laughing`, `sad`, `angry`, `shy`, `shocked`, `confused`, `speechless`, `embarrassed`, `excited` |
| Subject | `cat`, `dog`, `panda`, `girl`, `meme`, `anime` |
| Action | `clapping`, `facepalm`, `crying`, `blushing`, `bowing`, `hugging`, `waving`, `deadpan` |
| Context | `praise`, `thanks`, `comfort`, `encouragement`, `greeting`, `goodnight`, `apology`, `celebration`, `teasing`, `gossip`, `disagree`, `question`, `silence` |
| Restriction | `private_only`, `close_friends`, `use_carefully`, `not_for_group` |

Keep names to three to five meaningful labels. A human verifies the image's
real tone and restrictions; an LLM may propose a candidate name from the
controlled vocabulary but must not rename assets without review.

## Capability Boundary

| Capability | Allowed behavior | Boundary |
| --- | --- | --- |
| List local emojis | Read names from `Storage/QQEmojis` for model selection. | Does not infer the real image meaning beyond its curated filename. |
| Save image | Download a permitted image to the local library. | HTTP(S), image-type, size, timeout, and safe-path checks are required. |
| Send local emoji | Delegate delivery to the existing `qimage`/QChat/OneBot path. | Never bypasses current target, authorization, or audit rules. |
| Automatic suggestion | Use probability, cooldown, burst limit, and emotion labels to suggest an emoji. | It is a suggestion; the model may decline, and it must not spam. |
| Online search | Return or download a result from an explicitly enabled provider. | Provider availability, licenses, and image rights are external and unguaranteed. |
| Tencent direct send | Search and delegate an image to `qimage` for the current explicit target. | Requires a valid session target and external-message policy approval. |

Default safe configuration:

```text
Enable online BQB search: off
Enable Tencent search: off until governed and tested
Automatically persist downloaded images: off
External URL save/cache: trusted runtime only
```

## Required Compatibility Work

The remote QQEmoji v1.5.1 automatic path currently recognizes only:

```csharp
"消息来源:[QChatService]"
```

Current local QChat model input includes `[QChatService]` through
`InteractiveModule<T>.ChatTextFilter`. Before installation, update the plugin
guard to recognize the current marker, then verify an automatic suggestion is
actually produced. Do not alter local QChat only to preserve an external
plugin's stale marker.

The QQEmoji module must continue to send through the existing `qimage` XML
handler via `XmlFunctionCaller.ExecuteFunctionAsync`. It must not expose the
internal handler table or create a second OneBot send path.

## Optional Governance

When the plugin is ready for production use, declare its XML functions in the
FunctionCaller/DataAgent capability registry and make them governed tools.

| Tool | Proposed risk | Required control |
| --- | --- | --- |
| `ListEmojis` | Low | Trusted runtime. |
| `SearchBqbOnline` | Medium | Route and per-turn budget. |
| `SaveImage` | Medium | Trusted runtime, URL/type/size/path validation. |
| `DownloadToCache` | Medium | Route, budget, and cache-only safe path. |
| `SendTencentEmoji` | High | Explicit QChat target and external-message authorization. |

The current shared state-effect enum covers external sending but does not yet
express `DownloadsExternalContent` or `WritesLocalFile`. Do not falsely label
download or persistence as read-only. Add those state effects only if strict
cross-plugin enforcement is required.

## Performance and Security Baseline

The runtime plugin uses a single image-download path with these constraints:

- QChat messages parse a session target only when Tencent search is enabled;
- image downloads use one shared host client with automatic redirects and proxies
  disabled; BQB background prefetch remains capped at three concurrent transfers;
- downloads require HTTPS, a publicly resolved address, an `image/*` content
  type, a matching PNG/JPEG/GIF/WebP/BMP signature, and a streamed 10 MB hard
  limit;
- online result counts are clamped to 1 through 20;
- user-facing download errors do not reveal filesystem or remote exception
  details.

This blocks the common redirect, private-address, DNS-rebinding, extension-
spoofing, and unbounded-buffer failures by connecting only to an address that
was resolved and classified as public. The downstream OneBot fetch used by a
remote `qimage` remains a separate platform boundary; Tencent direct send stays
disabled until that fetch is equivalently constrained.

## DataAgent Audit Boundary

The plugin does not replace `XmlFunctionExecutionPolicy`, its governed-tool
list, or DataAgent's capability registry. Local emoji discovery uses the
existing QChat runtime-audit bridge and therefore writes to the account's
DataAgent SQLite store without giving DataAgent execution, scheduling, or send
authority.

The stored records are deliberately small:

- `tool.qqemoji.list`: total count, page offset, and returned candidate names;
- `tool.qqemoji.search`: the query and at most 20 candidate names;
- `tool.qimage.send`: the final local filename or remote host, target type, and
  target ID after a successful send.

No image bytes, complete local path, complete remote URL, Cookie, token, or raw
exception is stored. Governed `saveimage` authorization remains in the existing
FunctionCaller route and policy; online BQB/Tencent capabilities remain off.

## Upgrade Roadmap

### Upgrade 1: Verify the final send boundary

Before enabling an external source, locate the `qimage` handler and verify:

- remote URLs receive equivalent redirect, address, media, and size checks;
- the target is the current QChat conversation or an explicitly authorized
  target;
- private/group authorization is enforced at the final OneBot send;
- allowed and denied sends produce an audit record without exposing a full URL,
  filesystem path, token, or exception message.

Do not enable Tencent direct send until this upgrade is complete.

Status: the `qimage` endpoint now reuses `QChatVisionMediaPolicy` for remote
images. It rejects non-HTTPS URLs, URL credentials, non-standard ports, and
obviously unsafe literal/localhost addresses before OneBot is called.
Successful image sends write both a diagnostic and a DataAgent runtime-audit
record with a local filename or remote host only, never the complete path or
image URL. Relative emote names cannot escape `Storage/Emotes`; explicitly
authorized absolute local paths remain supported.

### Upgrade 2: Add an append-only capability bridge

Introduce a generic FunctionCaller registration bridge for runtime plugins.
It must append manifests and governed names to the existing policy rather than
replace DataAgent's registrations.

The bridge must also extend `ToolCapabilityRouter` route decisions. Registering
an XML handler or adding a manifest alone is insufficient because the current
router rebuilds the governed tool-name set from its built-in manifests each
turn.

Status: `ToolCapabilityRouter.WithAppendedManifests` now provides the immutable
append-only candidate set and rejects duplicate names. Appended manifests remain
denied until a future host-owned route explicitly allows their names; no
QQEmoji online tool is registered or authorized yet.

Status update: the host now owns a single `saveimage` manifest. QQEmoji can only
request that manifest through `EnableQqEmojiSaveImageCapability`; it cannot
register arbitrary names. The route allows it only for a trusted Owner-private
turn that contains both a save instruction and an HTTPS image URL. Bare save
questions, group messages, untrusted turns, and every other QQEmoji tool remain
denied.

Status update: the adapted runtime plugin delegates `qimage` through
`XmlFunctionCaller.ExecuteFunctionAsync`; the host keeps the handler table
private, and the installer rejects adapters that still reference it. Tencent
search and direct send remain disabled by default.

Start with `SearchBqbOnline`, `SaveImage`, `DownloadToCache`, and
`SendTencentEmoji`; keep local `ListEmojis` and the existing `qimage` route
unchanged. Add `DownloadsExternalContent` and `WritesLocalFile` only alongside
real policy enforcement and audit coverage.

### Upgrade 3: Curate the local library

Create `Storage/QQEmojis` at runtime and review a first set of 30 to 80 images.
Name each asset with the English controlled vocabulary in this document. Start
with praise, thanks, comfort, encouragement, laughter, confusion, apology,
celebration, and goodnight; label risky images `use_carefully`,
`private_only`, or `not_for_group`.

Use a conservative initial policy:

```text
AutoProbability: 5 or lower
PolicyMode: Conservative
EnableOnlineSearch: false
EnableTencentSearch: false
EnableOnlineImageCache: false
EnableTencentAutoSave: false
TencentSessionFromAi: true
```

The local QQEmoji plugin provides `<searchemojis keyword="开心 夸奖" />`
and paged `<listemojis offset="0" limit="20" />`. It maps a small controlled
set of common Chinese intent words to the English filename vocabulary, requires
every resulting token to match, sorts ordinally, and returns at most 20 names.
It does not inject the library inventory into the stable prompt, infer arbitrary
image meaning, search subdirectories, or access the network.

### Upgrade 4: Enable online sources one at a time

Only after Upgrades 1 through 3 pass their checks, enable capabilities in this
order:

```text
local library
→ governed SaveImage
→ BQB search returning URLs only
→ BQB temporary cache
→ Tencent search without direct send
→ Tencent direct send
→ automatic permanent persistence
```

For each step, document allowed host names, retention duration, private/group
scope, owner authorization, and the audit evidence required to enable the next
step.

## Acceptance Checklist

- [ ] Permission is confirmed for the QQEmoji code, generated UI, release
      package, and any third-party index or image data to be installed or
      redistributed.
- [x] Plugin is obtained from a reviewed release and installed as an external
      runtime plugin, not copied into tracked source.
- [x] The QChat marker compatibility adjustment is applied to the plugin.
- [x] Module reload discovers the attributed QQEmoji module without compile
      errors.
- [x] A controlled private-chat test lists a locally named emoji and sends it
      through `qimage`.
- [x] Stable prompts contain no emoji inventory; paged list/search candidates
      and the final selection use DataAgent runtime audit without image bytes.
- [x] Identical one-shot tool calls are executed once per model turn and are
      allowed again on the next turn.
- [ ] The probability/cooldown/burst behavior is verified in a live QQ session.
- [ ] Invalid URL, unsupported media, oversize media, and unsafe filenames are
      rejected.
- [ ] Group and private targets obey the existing QChat authorization policy.
- [x] Online sources and automatic persistence remain disabled until their
      licenses, network behavior, and retention policy are approved.
- [ ] If governance is enabled, allowed and denied calls appear in the XML
      function audit.

## Asset Naming Review Sheet

Use this review sheet before any batch rename:

| Original file | Human description | Suggested English filename | Restriction | Approved |
| --- | --- | --- | --- | --- |
| `IMG_001.gif` | Cat clapping to praise someone. | `happy_cat_clapping_praise.gif` | None | [ ] |
| `IMG_002.jpg` | Panda facepalm; light teasing only. | `laughing_panda_facepalm_teasing_use_carefully.jpg` | `use_carefully` | [ ] |
| `IMG_003.png` | Dog crying and asking for comfort. | `sad_dog_teary_comfort.png` | None | [ ] |
