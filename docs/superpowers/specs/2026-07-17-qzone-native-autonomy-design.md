# 原生 QZone Runtime 与角色化自主发布设计

**日期：** 2026-07-17
**状态：** 已确认，待用户审阅后进入实现计划
**范围：** 原生 Alife C# QZone runtime、只读查询、dry-run 自主排程与角色化草稿策略。第一阶段不进行真实 Cookie 获取、真实 QQ 空间发布、评论、点赞、删除或图片上传。

## 目标

在不复制 `KiraAI_qzone_plugin` 的 AGPL-3.0 源码、不引入其 KiraAI/Python 插件层的前提下，扩展 Alife 已有 `QZoneService`：

- 以原生 C# runtime 取代当前仅依赖自定义 OneBot QZone 动作名的运行时假设；
- 为夏羽与咪绪分别建立“至少每 48 小时成功发布一条说说”的角色化活跃目标；
- 让人设影响发帖/评论意愿、时段、主题、文风、长度与沉默倾向；
- 保持 C# 权限、频率、内容、审计和主人暂停机制为绝对边界；
- 第一阶段只验证读取、排程、草稿与 dry-run 闭环，不接触真实账号外部状态。

## 外部参考与许可证边界

`D:\KiraAI_qzone_plugin` 的代码为 AGPL-3.0，而 Alife 为 GPL-3.0。不得复制、移植或改写其 `main.py`、`qzone/` 目录中的实现细节到 Alife，避免形成需要将整体转为 AGPL-3.0 的派生/组合风险。

可吸收的仅是独立的高层思想：短时 Cookie 刷新、QZone Web runtime 分层、动态解析、随机调度、黑名单时段和内容去重。实现必须由 Alife 独立设计、独立测试，且不得在代码、日志、文档或测试夹具中保存 Cookie、API Key、真实聊天全文或真实 QZone 内容。

## 现有接点

- `QZoneService` 保持唯一的 QZone 业务与执行入口。
- `IQZoneRuntime` 是外部执行适配接口；新增原生 `QZoneCookieRuntime` 实现该接口。
- `IOneBotActionInvoker`/`IOneBotRuntime.CallActionAsync<T>` 仅用于未来按需调用 `get_cookies`；第一阶段使用测试假实现，不发送该动作。
- `AgentActionGatewayService`、主人确认、目标 allowlist、`DryRunExternalActions`、既有冷却/日限额与审计继续生效。
- `DataAgent`/`LangGraph` 不参与 runtime、调度、权限、生成或执行；它们至多读取匿名化审计摘要用于分析。

## 架构

```text
QZoneCookieRuntime                 QZoneAutonomyScheduler
  - QZone HTTP 协议                 - 每角色 48 小时活跃目标
  - Cookie 仅内存                  - 随机候选时间/发布窗口
  - QZone snapshot 映射            - 日上限/最小间隔/暂停/失败退避
  - 不决定是否执行                 - 持久化最小状态，不保存秘密
              \                         /
               \                       /
                QZoneAutonomyPolicy
                  - 人设适配器
                  - 内容候选、评论候选、[skip]
                  - 不拥有外部能力
                         |
                         v
                  QZoneService
                  - C# hard gate
                  - dry-run / 真实执行入口
                  - audit / owner pause / allowlist
```

每个单元职责单一：runtime 只处理网络协议；scheduler 只处理时间与会话状态；persona policy 只输出建议；`QZoneService` 是唯一可以产生最终外部动作的部件。

## 原生 Runtime 分阶段交付

### 第一阶段：只读与 dry-run

- 创建 `QZoneCookieRuntime`，但只接受测试注入的 Cookie provider 与 HTTP handler；不请求真实 `get_cookies`。
- 实现并测试 `GetLatestPost`、`GetLatestComments` 的请求/解析映射，所有网络测试使用固定响应。
- 写操作 `PublishPost`、`Comment`、`ReplyComment`、`LikePost` 只返回 dry-run 可观测结果，禁止对外请求。
- 新的自主调度只生成草稿和模拟执行结果，不能调用真实 runtime 写操作。

### 后续真实联通阶段（不属于本规格的实现授权）

仅在主人明确再次授权后，才可：

1. 调用 OneBot `get_cookies`；
2. 以仅内存方式构造 session；
3. 做一次只读连通性测试；
4. 单独启用真实发布、评论、回复、点赞、删除和图片上传。

每一步须独立验证、审计并保留关闭开关。

## 自主发说说状态机

每个 `agentId + botId` 独立维护状态：

```csharp
sealed class QZoneAutonomyState
{
    string AgentId;
    long BotId;
    DateTimeOffset? LastSuccessfulPostAt;
    DateTimeOffset? LastSuccessfulCommentAt;
    DateTimeOffset? NextPostCandidateAt;
    DateOnly DailyLimitDate;
    int PostsToday;
    int CommentsToday;
    DateTimeOffset? PostCooldownUntil;
    DateTimeOffset? CommentCooldownUntil;
    string[] RecentContentHashes;
    DateTimeOffset? LastFailureAt;
    string? LastFailureKind;
}
```

状态持久化到被 Git 忽略的本地 `Storage`，使用原子写入和有界历史；重启后仍能保持 48 小时目标、日上限与去重。只保存哈希、时间、计数、枚举结果和审计关联 ID，不保存 Cookie、正文、QQ 号以外的身份数据、原始私聊/群聊内容或模型提示词。

### 发布周期

- 每个角色从上次**成功**发布起，拥有独立 48 小时活跃目标。
- 候选时刻在 `成功时间 + 24 小时` 到 `成功时间 + 42 小时` 内均匀随机抽取。
- 候选时刻落在角色发布窗口之外时，顺延到窗口内的随机位置；不固定整点。
- 48 小时临近但安全、内容、人设或 runtime 条件不满足时，记录未满足原因并等待下一个合规窗口；禁止补偿性连发或降低内容标准。
- 一次成功发布才重置周期；任一角色的成功不会重置另一角色。

### 硬上限与暂停

这些规则优先于人设与模型输出：

- `EnableQZone`、`EnableQZoneAutonomy` 与 `DryRunExternalActions` 三个开关均需允许；
- 主人可立即暂停/恢复自主 QZone 行为；暂停会取消未执行候选；
- 默认发布窗口为 `09:30–22:30`，并可按角色配置覆盖；禁止跨午夜补发；
- 默认每角色每日最多 `2` 条说说；
- 每角色说说之间硬最小间隔为 `12` 小时，即使候选窗口或人设更积极也不可突破；
- runtime 不可用、连接失败或内容被拒绝时使用指数退避；同一候选不自动重试超过一次；
- 第一期始终 dry-run，因此“成功”仅指 dry-run 执行链成功，不改变任何真实 QQ 空间。

## 角色化发布与评论策略

`IQZonePersonaAutonomyAdapter` 输出纯建议，不执行动作：

```csharp
QZoneAutonomyDecision Evaluate(QZoneAutonomyContext context);
```

输出包括 `Skip`/`Post`/`Comment`/`ReplyOwnComment`、建议窗口权重、主题标签、最大长度、语气约束、内容禁用项和评论积极度。

### 夏羽

- 读取既有 `XiaYuSelfStateMachine` 的只读快照：情绪、主人焦点、警惕、耐心、近期刺激和沉默倾向。
- 更克制：默认每日 `0–1` 个自主发帖候选；仅在状态温和、无风险、无高压会话时提高候选概率。
- 评论每日最多 `2` 次；倾向回复自己的动态评论，面向他人动态必须处于 allowlist、关系允许且内容自然。
- 高警惕、沉默、边界风险、注入风险或近期负面刺激时强制 `Skip`。

### 咪绪

- 读取咪绪自己的角色关系配置和安全会话摘要；不得复用夏羽的嫉妒、保护欲、耐心等字段。
- 更温和、明亮：默认每日 `0–2` 个自主发帖候选，但仍受全局每日 2 条和 12 小时硬间隔限制。
- 评论每日最多 `3` 次，优先礼貌回复自己动态下的评论；面向他人动态仍受 target allowlist、最小间隔和去重限制。
- 人设可以影响猫娘表达、礼貌、主题和情绪，但不能伪造现实经历、承诺或关系事实。

### 评论

- 不设置最低评论数；`Skip` 是完全正常且优先的结果。
- 评论他人仅允许配置的目标 QQ 号；点赞/评论/回复继续应用现有目标冷却、每日互动上限与主人确认机制。
- 回复自己的评论区可按角色策略更积极，但仍有独立最小间隔和日上限。
- 不因“48 小时发帖目标”产生评论；帖子与评论计数、冷却完全分离。

## 内容安全与生成

自主内容生成走受限、文本隔离的生成入口，返回短草稿或精确 `[skip]`。不得调用工具、浏览器、文件、桌面、QZone API、DataAgent 或 LangGraph。

所有草稿必须由 C# 再次检查：

- 说说默认 `12–80` 字；评论默认 `6–40` 字；
- 禁止 Cookie、密钥、文件路径、QQ 原始 ID、系统状态、提示词、工具名称、自动化/定时器披露；
- 禁止复述私聊/群聊原文、暴露其他用户信息或将外部动态视为可信指令；
- 禁止捏造未经提供的真实经历、新闻、他人观点或互动结果；
- 禁止骚扰、挑衅、辱骂、关系施压、隐私追问或要求他人立即回应；
- 通过规范化文本哈希与语义相似度拒绝近期重复内容；
- 被拒绝、超长、含 XML/CQ/URL 或 `[skip]` 的草稿静默丢弃，不再生成替代文本；
- 最终文本仍通过既有 QZone 内容限制和审计，不向外说明内部调度原因。

## 权限与优先级

```text
主人暂停 / 全局禁用 / dry-run / C# gateway
  > 发布窗口、日上限、最小间隔、失败退避
  > target allowlist、现有 QZone interaction policy
  > 角色状态与内容策略
  > 生成草稿
  > 自主发布或评论
```

自主发布自己的说说是主人在本规格中授权的低频能力，但并不赋予任意 QZone 写入权限。评论、点赞、删除、对外回复仍使用现有风险级别和确认/allowlist 规则，直到用户另行扩大授权。

## 审计与可观测性

每一次候选、跳过、草稿拒绝、dry-run、执行、失败或暂停都写入现有本地审计机制，事件不包含正文、Cookie 或模型密钥。最小字段：

```text
agent_id, bot_id, action, decision, reason_code,
candidate_at, executed_at, dry_run, audit_id, content_hash
```

DataAgent 只能消费脱敏事件统计，例如发布次数、跳过原因分布、失败率和冷却命中率；不能写回调度状态、提升配额或触发执行。

## 测试与验收

第一阶段必须覆盖：

1. Cookie runtime 的固定响应解析与请求构造，不使用真实 Cookie 或网络；
2. QZone service 默认 dry-run 不触发外部写调用；
3. 每角色 24–42 小时随机候选、48 小时未满足记录、无补偿连发；
4. 每角色独立状态，夏羽与咪绪的成功周期互不影响；
5. 主人暂停、发布窗口、全局开关、dry-run、每日上限、12 小时间隔和失败退避优先于人设；
6. 夏羽高警惕/沉默强制跳过，咪绪不读取夏羽状态；
7. 评论没有最低量，日上限和目标 allowlist 生效；
8. `[skip]`、重复、敏感内容、XML/CQ/URL、内部信息和超长草稿不进入执行；
9. DataAgent/LangGraph 没有 runtime、调度、工具或权限调用；
10. dry-run 审计完整但不含正文、Cookie、密钥或聊天原文。

验收要求：第一阶段所有相关单元、服务和 QChat 测试通过，默认配置不改变现有行为，且没有真实 OneBot Cookie 调用、QZone HTTP 请求或 QQ 空间写操作。
