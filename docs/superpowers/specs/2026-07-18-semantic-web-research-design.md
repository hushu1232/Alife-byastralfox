# 语义主动联网研究设计

**日期：** 2026-07-18

**状态：** 已获方案确认，待用户审阅本文档后进入实现计划

**范围：** `D:\Alife` 的 QChat 问答内主动联网；不包含定时推送、后台主动私聊或 DataAgent 改造。

## 1. 目标与非目标

### 目标

让夏羽（`xiayu`）和咪绪（`mixu`）在合格的聊天回合中，基于消息语义而不是关键词判断是否需要实时外部资料；需要时主动完成检索、必要的网页读取，并将可靠证据作为回答补充。

适用会话范围：

- Owner 私聊；
- 群成员在消息中 @ 对应机器人后的提问。

不确定是否需要检索时，系统偏向执行一次快速检索，以提高信息新鲜度和事实可核验性。检索期间和检索完成后的文字由角色回答生成流程自然组织，不能使用固定的“正在搜索”或“搜索失败”话术。

### 非目标

- 不实现定时新闻、主动推送或未受提问触发的私聊；
- 不让 DataAgent、SQL 查询计划、Graph sidecar 或证据时间线参与聊天检索；
- 不移除公开 URL、私网地址、下载大小、超时、密钥与审计等运行底线；
- 不直接把第三方 SmartWebSearch 作为全局 XML 工具安装。

## 2. 现有架构与约束

当前 QChat 已有以下可复用能力：

- `QChatMessageSecurity` 解析 Owner、群成员和私聊访客；
- `QChatPublicInternetCommandPolicy` 处理 `/search`、`/rag` 和有限的显式联网语义；
- `AgentWebAccessRouter`、`AgentWebAccessService`、`AgentPublicSearchService` 负责受控搜索与网页读取；
- `AgentWebResearchService` 负责编排候选来源、缓存、冷却、网页证据和结果格式；
- `IAgentPublicSearchProvider` 是搜索后端的可替换扩展点；
- `QChatImageRecognitionService` 和 `IQChatImageRecognitionClient` 是视觉服务扩展点。

当前显式搜索路径不能满足普通提问的语义主动检索。DataAgent 的设计目标是本地数据分析、结构化计划和 SQL 治理，调用成本与时延模型不适合聊天回合中的实时 Web 决策，因此本设计与其解耦。

## 3. 推荐架构

新增一条独立的、低延迟的问答前研究路径：

```text
QChat 合格消息
  -> SemanticWebResearchRouter
  -> SemanticWebResearchDecision（受 JSON schema 校验）
  -> WebResearchExecutor
  -> AgentPublicSearchService / AgentWebAccessService
  -> ResearchEvidence
  -> 角色化回答生成
  -> QQ 回复
```

### 3.1 会话资格

资格判断是访问范围，而不是检索语义：

- Owner 的私聊消息有资格；
- 群聊消息必须 @ 到当前 `SelfId` 对应的夏羽或咪绪；
- 未 @ 的群聊、私聊访客、空消息、已被阻断消息均不进入语义路由。

资格通过后才调用语义路由器。正常问答、显式 `/search` 命令和浏览器自动化命令保持各自已有入口；显式命令不应被重复检索。

### 3.2 语义决策器

新增 `ISemanticWebResearchRouter`，以轻量 LLM 客户端为实现细节。输入为：

- 当前消息文本；
- 被截断且脱敏的近期会话上下文；
- `AgentId`、消息类型与发送者角色；
- 配置化的检索预算与最大来源数。

输出必须反序列化并校验为以下结构：

```text
shouldResearch: bool
confidence: low | medium | high
uncertain: bool
query: string
depth: quick | standard | deep
maxSources: 1..5
reasonCategory: temporal | verification | niche | explicit | stable | creative | companion
```

它不依据硬编码关键词判断“最新”“新闻”或“搜索”。系统提示只定义判断标准：是否需要外部、时效、可验证或长尾资料才能高质量作答；纯陪伴、创作、主观交流和稳定知识无需检索。

输出无效、路由超时或置信度不足时，若配置 `ResearchOnUncertainty = true`，执行一次 `quick` 搜索；若问题为空或不满足会话资格，直接跳过。路由器必须有短超时、取消令牌和独立的失败统计，且不得调用 DataAgent。

### 3.3 研究执行器

`WebResearchExecutor` 将语义决策映射到已有联网能力：

| 深度 | 执行行为 |
|---|---|
| `quick` | 搜索并返回有限结果/摘要，不读目标页面 |
| `standard` | 搜索、来源排序、读取少量可读公开页面 |
| `deep` | 多来源搜索、有限页面读取；站点需要浏览器时使用现有 Browser Snapshot |

所有外部文本仍由 `ExternalContextFormatter` 标记为不可信。搜索、抓取和浏览器调用继续服从 URL、响应大小、超时、反爬/登录墙和审计机制。检索执行独立于 DataAgent，不能等待其 planner、SQLite 或 HTTP sidecar。

### 3.4 搜索 Provider

第一阶段继续支持现有 DuckDuckGo/Bing Provider。后续可增加：

- `TavilySearchProvider`：实现 `IAgentPublicSearchProvider`，产出标题、URL、摘要；
- `BaiduQianfanSearchProvider`：实现相同接口，支持中文搜索、图片/视频元数据和来源格式化；
- `BaiduQianfanResearchClient`：作为 Owner 允许的深度研究增强，而不是全局工具。

第三方 SmartWebSearch 仅作为端点、请求形状和能力清单的参考。其全局 XML 工具注册、任意图片 URL 下载、无统一权限/审计的实现不直接复用。引入其源代码前必须先取得明确许可证或作者授权。

## 4. 角色化联网反馈与最终回答

### 4.1 检索反馈

若研究在 `FeedbackDelay` 内完成，则不发送中间消息。超过延迟时创建 `research-started` 事件，交由角色回复生成器根据：

- 角色身份与已有 persona memory；
- 用户问题和当前会话语气；
- 当前研究状态；
- 群聊或私聊上下文；

自然生成一句简短的过渡反馈。不得用硬编码台词或固定昵称模板。

### 4.2 最终回答

最终回答模型接收结构化的 `ResearchEvidence`，包括来源标题、URL、摘要、抓取状态和失败原因。回答约束：

- 仅使用提供的来源 URL，不得编造引用；
- 用角色自然语言解释“查到的内容”及不确定性；
- 不输出上游模型的 `reasoning_content`；
- 证据不足时自然说明检索局限，仍可给出已知的非实时回答；
- 回复附带可验证的来源，避免把搜索摘要当作无条件事实。

## 5. 性能、缓存与失败策略

### 性能目标

- 语义路由不走 DataAgent；
- 路由使用短超时、极小输入和一次结构化结果；
- 路由、搜索和最终回答全程支持取消令牌；
- 相同会话的重复提问复用短期研究缓存；
- Provider 失败时允许回退到下一个搜索 Provider；
- `quick` 是不确定时的默认代价上限，避免每个不确定问题都做网页深读。

### 失败处理

| 情形 | 行为 |
|---|---|
| 路由器无效/超时 | 对合格且有内容的问题执行 `quick` 搜索 |
| 搜索 Provider 失败 | 依次回退其他已配置 Provider |
| 无搜索结果 | 最终回答获取“无可靠实时证据”状态，不伪造联网结果 |
| 网页读取失败/反爬/登录墙 | 使用已得搜索摘要，记录站点经验，不做绕过 |
| 研究被取消 | 不发送过期的中间反馈或最终研究结果 |
| 角色生成失败 | 使用结构化、最小化的来源回复作为最后的可见回退 |

## 6. 配置建议

新增 `QChatConfig` 或独立的 `SemanticWebResearchConfig`：

```text
Enabled
EnableOwnerPrivate
EnableMentionedGroup
ResearchOnUncertainty
RouterTimeoutMilliseconds
FeedbackDelayMilliseconds
QuickMaxSources
StandardMaxSources
DeepMaxSources
MaxConcurrentResearch
SessionCacheSeconds
ProviderOrder
EnableTavily
EnableBaiduQianfan
```

API Key 只通过受控配置/环境变量/凭据提供器解析；不得把密钥写入日志、回复、研究缓存或 persona context。

## 7. 测试矩阵

### 语义路由

- 实时、核验、长尾问题生成正确的检索决策；
- 创作、陪伴、稳定知识问题不检索；
- 不确定、无效 JSON、低置信度和超时按 `ResearchOnUncertainty` 走 `quick`；
- 查询重写、深度和来源数满足 schema 边界。

### QChat 集成

- Owner 私聊可触发；
- 群成员仅在 @ 当前角色后触发；
- 未 @ 群聊和私聊访客不触发；
- 显式搜索命令不产生重复研究；
- 夏羽与咪绪按各自身份、会话键独立处理。

### 执行与体验

- 路由路径不会调用 DataAgent；
- 快速完成不发送中间反馈；
- 慢检索发送自然人设化反馈；
- 最终回答只引用实际返回 URL；
- Provider 回退、缓存、取消、网页读取失败均有覆盖。

### 安全与凭据

- 私网/回环 URL、过大响应、不可读页面被拒绝或降级；
- API Key 不进入日志、缓存、提示词或回复；
- 远端图片下载在引入百度视觉 Provider 前必须通过统一的安全下载器。

## 8. 实施分期

1. 语义路由、结构化决策、QChat 资格判断与 quick 搜索接入；
2. 角色化检索反馈、研究证据注入、缓存/取消/指标；
3. Tavily 与百度千帆 Provider；
4. 百度深度研究和视觉 Provider（需密钥与 URL 下载安全设计）；
5. 根据真实会话数据调节路由阈值、Provider 顺序与成本预算。

第一期验收标准：Owner 私聊或群成员 @ 夏羽/咪绪后，对实时/核验/长尾问题能在不走 DataAgent 的条件下自动检索；对陪伴和稳定问答不多余出网；检索完成后按角色人设给出自然反馈与可验证来源。
