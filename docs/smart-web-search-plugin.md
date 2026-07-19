# SmartWebSearch 原生插件操作说明

本说明面向手动部署 `L134283/Alife-SmartWebSearch` 的 Alife 管理者。该仓库是一个独立的原生 Alife 插件，不是 QChat 多来源研究的必需依赖。

## 能力与边界

插件面向一般 Agent/LLM 工具调用，提供以下 XML 工具能力：

- `Search`
- `SmartSummary`
- `SmartChatSearch`
- `HotSearch`

QChat Phase 2 使用自己的、结构化的 DuckDuckGo/Bing research provider。QChat 不调用上述 XML 函数，不解析 `Poke(...)`、Markdown 或自然语言输出，也不会把插件输出转换为模型研究证据。这样插件故障、空结果或凭据未配置不会阻塞、减慢或改变 QChat 的研究关键路径。

## 手动安装与更新

按 Alife 原生插件的正常人工部署流程安装该项目的包，然后执行正常的 Alife 模块重载/重启。QChat 不会自动克隆仓库、下载包、安装、升级或更新 SmartWebSearch；版本选择和更新节奏完全由操作者管理。

安装后，若 QChat 的 `semanticWebResearch.multiSourceSearch.detectSmartWebSearchPlugin` 为 `true`（默认值），QChat 模块健康摘要可能显示：

- `smart-web-search=loaded`：当前进程已经加载名为 `Alife.Plugin.SmartWebSearch` 的程序集；
- `smart-web-search=not_loaded`：当前进程没有加载该程序集；
- 不启用检测时不追加插件诊断。

这只是程序集名称存在性的诊断，不能证明插件已经取得有效搜索结果，也不会降低 QChat 的 Healthy/Degraded/Unavailable 连接状态。

## 凭据与配置

SmartWebSearch 自己所需的 Tavily 或百度千帆凭据只在插件自身的部署配置中设置。请使用本地受保护配置、环境变量或部署系统的密钥管理方式；不要把 API key、token、cookie 或个人配置提交到本仓库、示例 JSON 或聊天记录中。

即使插件未安装、插件未加载、Tavily/Baidu 凭据缺失或插件内部调用失败，QChat 仍会根据自己的配置继续使用内置结构化 provider（启用时为并行 DuckDuckGo/Bing，未启用时为既有串行 fallback）。QChat 不会尝试修复插件、请求凭据或提示群用户配置密钥。

## 不包含的行为

- 没有自动下载、自动安装或自动更新；
- 没有从插件文本中提取 URL/摘要作为 QChat 证据；
- 没有 DataAgent 介入 QChat 联网搜索关键路径；
- 没有主动、定时或新闻推送式搜索；
- 没有绕过 QChat 的主人私聊 / 明确 @ Bot 群聊权限边界。

因此，SmartWebSearch 可以作为独立 Agent 的可选工具扩展，而 QChat 的联网研究始终可单独配置、测试和运行。
