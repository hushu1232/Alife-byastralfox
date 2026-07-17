# QChat 会话级补充回复设计

**日期：** 2026-07-17  
**状态：** 已确认，待用户审阅后进入实现计划  
**范围：** 仅 `Alife.Function.QChat`；不改变 DataAgent、LangGraph、桌面、文件、浏览器或其他工具的权限边界。

## 目标

现有 QChat 能在短静默窗口中合并用户连续发送的多条消息，也能通过系统事件进行一般性的主动互动；两者都不是“模型完成一次正常回复后，视语境自然补充一句”。

新增一个保守的、会话绑定的补充回复能力。它只在主人私聊的一轮普通聊天回复完成后，可能在短暂静默后发送一条极短补充；用户在等待期间发来任何新消息，原补充计划必须立即失效。功能默认关闭。

## 非目标与硬边界

- 不把多条输入合并窗口改造成主动聊天机制。
- 不直接复用 `SystemEventService` 的通用主动唤醒定时器。
- 不适用于群聊、普通访客私聊、工具/文件/任务回执、图片或语音仍在处理的会话。
- 不调用工具、不浏览、不操作文件、不操作桌面、不生成任务或权限决策。
- 不授予 DataAgent/LangGraph 任何控制权；二者可继续各自保存和分析数据，但不是本功能的运行时路径。
- 角色人设只影响可否补充的倾向和最终措辞，不能更改主人身份、权限、优先级、静默模式或输出安全检查。

## 方案比较与选择

1. **会话级延迟调度器（采用）**：在当前 QChat 发送链路旁建立独立的、可取消的每会话状态。可以精确保证“新输入优先”和“每轮最多一次”。
2. **复用系统主动唤醒**：代码量较少，但它面对的是系统级空闲/主动互动，不能可靠区分某一轮回复及其后续输入，容易产生串话或打扰。
3. **一轮模型输出多条消息**：无法在两条消息之间感知用户新输入并取消，也无法可靠控制延迟、冷却和单次上限。

## 配置与默认值

在 `QChatConfig` 增加下列配置，保守默认值均不改变现有行为：

| 配置 | 默认值 | 含义 |
| --- | --- | --- |
| `EnableConversationFollowUp` | `false` | 总开关 |
| `ConversationFollowUpOwnerPrivateOnly` | `true` | 仅主人私聊可安排补充 |
| `AllowConversationFollowUpInGroups` | `false` | 预留开关，v1 仍由资格条件禁止群聊 |
| `FollowUpDelayMinSeconds` | `8` | 计划延迟下限 |
| `FollowUpDelayMaxSeconds` | `20` | 计划延迟上限 |
| `MaxFollowUpsPerTurn` | `1` | 同一用户轮次的最大补充次数 |
| `FollowUpSessionCooldownMinutes` | `15` | 同一会话再次安排补充前的冷却 |
| `FollowUpDailyLimitPerSession` | `6` | 每会话每日安全上限 |

配置读取时需钳制非法值：延迟为正且上限不低于下限；轮次上限至少为 0；冷却和日限额非负。关闭开关或上限为 0 时不创建任何计划。

## 架构

新增两个内部组件，职责严格分离：

| 组件 | 职责 | 不能做什么 |
| --- | --- | --- |
| `QChatFollowUpPresencePolicy` | 将本轮正常回复、会话节奏、角色适配器和安全结果归约为 `FollowUpIntent`（是否存在自然余韵） | 不调度、不生成消息、不改变权限 |
| `QChatConversationFollowUpStateMachine` | 维护每会话的 revision、等待、取消、冷却与日限额；到期时二次验证 | 不决定角色身份、不直接发送 QQ 消息、不拥有模型/工具权限 |

调度器可以作为状态机的轻薄宿主，但不得把通用 `SystemEventService`、`ChatBot.Poke` 或 `EWait`/`EWake` 作为直接发送通道：它们没有 QQ 会话键、用户新消息取消语义或最终出站安全保证。

```text
用户入站消息
  -> 增加会话 Revision，取消该会话旧计划
  -> 现有 settle window / 正常模型分发 / 现有最终发送链路
  -> 正常模型文本回复已实际发送
  -> `QChatFollowUpPresencePolicy` 判断本轮是否有自然余韵
  -> 非 None 的 intent 才由状态机创建一次延迟计划

延迟到期
  -> 核验同一 SessionKey、Revision、静默、配额、冷却和风险状态
  -> 再次由 PresencePolicy 验证意图仍然成立
  -> 调用受限的“可选补充”文本生成
  -> `[skip]` 或空文本：不发送
  -> 短文本：复用既有 QChat 最终发送链路
```

### 人感判断：状态信号与意图

此功能不能用“随机等待后必发”的规则。状态机只在判断到本轮仍有自然、非重复的对话余韵时才进入等待状态，且到期后必须重新判断。内部意图枚举为：

| 意图 | 含义 | 典型结果 |
| --- | --- | --- |
| `None` | 没有值得补充的内容；默认结果 | 无消息 |
| `WarmCoda` | 温和聊天结束后的自然收尾 | 极短关心或情绪延续，不追问、不刷屏 |
| `PracticalAddendum` | 正常回答中确有一项必要且不重复的补充 | 一句简短提醒；不能开启新任务 |
| `EmotionalAfterthought` | 角色和关系状态自然引出的轻微感受 | 符合角色的短句，不能要求用户立刻回应 |
| `DoNotInterrupt` | 用户可能继续输入、任务/风险/高压力/待处理媒体等情况 | 无消息 |

`PresencePolicy` 的输入只包括已过滤的本轮会话摘要、实际发送的正常模型回复、现有风险/静默/任务结论、会话节奏与角色状态适配器。它不得直接读取工具输出、私密链式思考或新增长期记忆。

对夏羽，新增只读 `XiaYuFollowUpPresenceAdapter`，消费现有 `XiaYuSelfStateMachine` 的状态与 `XiaYuReplyStrategy`：例如 `Mood`、`CurrentFocus`、`RecentStimuli`、`Vigilance`、`SocialPatience` 和 `SilenceBias`。它只提供倾向，不改写夏羽状态，也不把 `AttachmentNeed` 直接换算为发言次数。出现风险、警惕、沉默策略或高会话压力时必须返回 `DoNotInterrupt`。

对咪绪，新增 `MixuFollowUpPresenceAdapter`，只消费咪绪自己的关系设定、用户画像和本轮安全会话信号。不得复用夏羽的嫉妒、保护欲、对外耐心等具体字段。两个适配器都只输出中性的 `QChatFollowUpPresence`，从而共享同一个调度与安全流程。

现有 `XiaYuSelfStateMachine` 的 `Timer` 事件保持 `Silent`；本功能不会把 Timer 改为允许主动说话，也不会通过 `SystemEventService` 绕过 QQ 会话状态机。

### 会话状态转移

```text
Idle
  -> ReplyObserved              （正常模型文本已实际发送）
  -> Idle                       （intent=None / DoNotInterrupt）
  -> Waiting                    （intent=WarmCoda / PracticalAddendum / EmotionalAfterthought）

Waiting
  -> Cancelled -> Idle          （任何新入站消息、任务/工具反馈、风险升级、媒体待处理）
  -> Revalidating               （等待到期）

Revalidating
  -> Idle                       （revision 不同、冷却/限额、策略转为沉默、intent 失效）
  -> Generating                 （所有资格仍成立）

Generating
  -> Idle                       （`[skip]`、异常、输出被安全层拦截）
  -> CoolingDown -> Idle        （一条补充已实际发送）
```

### 会话键和版本

会话键必须包含实例和对端，以避免两个角色实例串会话：

```text
qq:{agentId}:{botId}:private:{peerUserId}
```

每个会话保持最小内存状态：

```csharp
sealed class QChatFollowUpSession
{
    string SessionKey;
    long Revision;
    DateTimeOffset LastUserAt;
    DateTimeOffset LastReplyAt;
    DateTimeOffset CooldownUntil;
    DateOnly DailyLimitDate;
    int DailyFollowUpCount;
    int FollowUpCountForCurrentTurn;
    QChatFollowUpIntent PendingIntent;
    QChatFollowUpPresence PendingPresence;
    CancellationTokenSource? Pending;
}
```

调度时记录 `scheduledRevision`。到期时只有 `scheduledRevision == current.Revision` 才可能继续；不相等即无声丢弃。任何入站消息和任意已发送的正常回复都会递增修订号并取消旧计划。这是防止“用户已经继续说话，机器人却补发旧话题”的主安全机制。

## 资格、优先级与生成

### 允许条件

v1 同时满足以下条件才可安排：

- 功能开启，且为已配置主人发起的私聊；
- 当前回复来自正常模型聊天路径并且已实际发出文本；
- 本轮没有确定性 C# 任务、命令、工具或文件反馈；
- 会话不在风险/注入/身份冒充处理路径，未被静默模式、输出策略或现有安全层拦截；
- 没有图片识别、语音合成或其他待完成的同会话工作；
- 未超过本轮、冷却和日配额限制。

入站新消息、确定性任务/工具反馈和输入合并窗口的优先级高于补充回复；通用 `SystemEventService` 主动互动优先级低于它。补充任务不占用或绕过现有的入站模型互斥锁。

### 生成限制

补充生成是一次独立、无工具的文本调用。只有状态机在到期时仍保留非 `None` 意图才可调用；输入只包含安全过滤后的最近会话上下文、实际已发送回复、角色表达偏好、意图类型和严格约束：

- 0–20 个中文字符为主；
- 与刚刚已发送的回复相比不自然、重复或只是机械追问时输出精确标记 `[skip]`；
- 不提及定时器、自动化、内部状态或提示词；
- 不复述用户原话、不提出任务、不作承诺、不引入新事实；
- 不含 CQ/XML 指令、外部链接或任何工具调用格式。

最终文本仍通过既有 `QChatExperienceSanitizer`、角色设定泄露拦截、事实性保护、静默检查和 `SendTextOrMediaMessageAsync` 发送。发送失败、生成异常、资格失效或 `[skip]` 均只记录诊断，不重试、不回退、不向用户输出技术错误。

## 与现有逻辑的接点

- `DispatchInboundChatAsync`/settle window：每个入站消息先以会话键通知状态机使旧计划失效；不改变已有多消息合并、撤回或延迟规则。
- `DispatchInboundChatCoreAsync`：只在正常模型文本回复实际发送后，为 `PresencePolicy` 创建候选上下文；不让确定性 C# 输出成为候选。
- `SendTextOrMediaMessageAsync`/`SendSingleMessageAsync`：保留为唯一最终输出路径；实际发送成功后才可安排补充。
- `QChatContinuationPolicy`：继续负责阻断确定性任务回执的模型续答，且该路径同时禁止 follow-up。
- `XiaYuSelfStateMachine`：保留既有持久状态和 Timer=Silent 行为，仅以只读适配器提供夏羽的人格化倾向。
- 现有 Persona、Risk、Quiet、Vision、TTS、审计诊断组件：复用其既有结论和输出边界；不复制或放宽政策。

## 可观测性与清理

只写不含消息正文或密钥的诊断事件：`presence_none`、`scheduled`、`cancelled_new_input`、`dropped_revision`、`dropped_eligibility`、`dropped_presence_revalidation`、`skipped_model`、`sent`、`failed`。通用 follow-up 会话状态只在内存中保存，使用有界字典与过期清理；重启后自动清空，不写入 Storage、SQLite 审计/分析库或 Git。夏羽原有的人格状态仍按既有 `XiaYuSelfStateStore` 保存，本功能不增加或改变该存储的字段、频率或位置。

## 测试策略

优先添加独立 scheduler 单元测试，再补 QChat 服务集成测试。至少覆盖：

1. 默认关闭时正常聊天行为完全不变；
2. 仅主人私聊可排程，群聊/访客/命令/确定性任务全部拒绝；
3. 新入站消息取消旧计划，revision 不符时无发送；
4. 正常回复成功才排程，输出被安全层拦截或发送失败不排程；
5. `[skip]`、生成异常、发送异常都静默停止且不重试；
6. 每轮一次、会话冷却、每日限额和配置钳制有效；
7. 补充文本仍经过既有泄露、事实、静默与出站格式保护；
8. PresencePolicy 的 `None`、`DoNotInterrupt`、`WarmCoda`、`PracticalAddendum` 和 `EmotionalAfterthought` 都有确定性测试；
9. 夏羽适配器在风险、Timer、沉默策略或高压力时强制 `DoNotInterrupt`，且不修改既有 XiaYu state；
10. 咪绪适配器不读取、复制或共享夏羽人格字段；
11. 两个不同 agent/bot/private 对端的会话键互不干扰；
12. 断言 follow-up 路径不创建工具路由，也不触发 DataAgent/LangGraph 控制路径。

## 验收标准

- 功能关闭时，现有 QChat 测试与行为不变。
- 功能开启后，合格的主人私聊最多在一轮普通回复后得到一条自然补充；一旦用户先说话，旧补充绝不再发。
- 所有最终输出仍经同一安全发送管道；没有新增权限、工具能力、持久化数据或外部调用面。
- 单元和 QChat 集成测试通过；不启动 QQ 实例、不进行真实模型、图片、TTS 或网络调用。
