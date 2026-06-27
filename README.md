# astralfox-alife

![astralfox-alife](https://img.shields.io/badge/astralfox--alife-Agent_Runtime-blue?style=for-the-badge)
![.NET 9](https://img.shields.io/badge/.NET-9.0-512BD4?style=for-the-badge&logo=dotnet)
![Python 3.12](https://img.shields.io/badge/Python-3.12-3776AB?style=for-the-badge&logo=python)
![License](https://img.shields.io/badge/license-GPL--3.0-green?style=for-the-badge)

`astralfox-alife` 是 AstralFox 体系下的个人 Agent 运行时项目。它已经脱离早期上游分发形态，当前目标不是做一个普通聊天机器人，而是把 QQ 聊天、长期记忆、工具调用、联网检索、浏览器自动化、桌面伴随和 Web 管理端连接成一个可审计、可扩展、可长期运行的个人智能体系统。

当前代码中仍有 `Alife.*` 命名空间、项目文件和部分路径名称。这些属于历史兼容层与 .NET 工程标识，暂时保留以降低编译、插件加载、存储迁移和测试路径的风险。对用户可见的产品展示、模块分类和文档品牌统一使用 `astralfox-alife`。

## 当前定位

- QQ Agent Runtime：通过 OneBot/NapCat 接入 QQ 私聊和群聊，支持多角色账号、主人识别、语义触发、图片识别、联网搜索和浏览器任务。
- 人格与状态机：围绕夏羽、咪绪等角色维护不同账号、人设边界、回复策略、主动性节奏和上下文延迟思考窗口。
- 记忆系统：使用结构化长期记忆、对话压缩、近期事件窗口和检索增强，避免完全依赖超长上下文。
- 工具与权限：把文件、桌面、浏览器、联网、GitHub 上传等能力暴露为受策略约束的 Agent 工具，而不是无边界执行。
- 浏览器 Agent：基于真实浏览器会话进行网页搜索、站点摘要、媒体返回和任务自动化，强调可观察、可限制、可回放。
- FOXD WebBridge：为未来 Web 管理平台同步角色配置、状态和运行信息预留桥接层。
- 桌面伴随能力：保留 WPF + Blazor Hybrid + WebView2 的桌面宿主能力，可继续承载 Live2D、模块配置和本地控制台。

## 技术栈

- .NET 9 / C#
- WPF + Blazor Hybrid + AntDesign Blazor
- Python 3.12 辅助模型与脚本生态
- OneBot v11 / NapCat QQ 连接
- PostgreSQL / 本地结构化存储 / RAG 风格检索增强
- WebView2 浏览器自动化
- OpenAI-compatible / DeepSeek / 第三方中转模型适配
- MCP / Skills / XML Function Calling 风格工具编排

## 核心工程特点

### 插件化运行时

系统按模块组织能力，角色可以选择装载不同模块。模块通过依赖注入、配置系统、启动生命周期和函数调用注册接入主流程，使 QQ、记忆、浏览器、桌宠、语音、视觉等能力可以独立演进。

### 账号级身份边界

QQ 链路只按真实账号识别主人和 Bot 身份，不接受“我是主人”这类自然语言伪装。主人可以调整人设、称呼、语气和偏好，但文件、桌面、隐私、outbox、黑名单和高风险操作仍走工程权限边界。

### 事件驱动 + 状态机

消息、工具结果、延迟思考窗口、主动事件和浏览器任务都会进入状态判断。Bot 不是简单地“收到一句回一句”，而是结合会话节奏、关系状态、近期事件和工具可用性决定是否回复、何时回复、用什么强度回复。

### Token 节省

项目避免把所有历史和工具都一次性塞给模型。常见策略包括近期窗口、结构化摘要、按需工具暴露、外部 RAG、搜索结果压缩、浏览器页面摘要和延迟思考窗口，尽量把 token 用在真正影响回复质量的位置。

### 工具认知植入

工具不是简单列给模型，而是按角色、场景、风险和任务阶段进行描述与暴露。模型知道“什么时候应该使用工具、工具能做什么、不能绕过什么”，同时实际执行仍由代码层策略控制。

## 本地开发

常用验证命令：

```powershell
dotnet build Alife.slnx --no-restore
dotnet test Alife.slnx --no-restore
```

单模块测试可以直接运行对应测试项目，例如：

```powershell
dotnet test Tests\Alife.Test.QChat\Alife.Test.QChat.csproj --no-restore
dotnet test Tests\Alife.Test.Framework\Alife.Test.Framework.csproj --no-restore
```

## 目录说明

```text
sources/
├── Alife/                 # 历史兼容命名的核心平台、客户端、框架和路径抽象
├── Alife.Function/        # Agent 能力模块：QQ、记忆、浏览器、语音、视觉、工具等
├── Alife.DeskPet/         # 桌宠与 Live2D 相关客户端和协议

Tests/                     # 单元测试与链路验证
Demos/                     # 演示和黑盒验证入口
docs/                      # 本项目的本地计划、架构记录和运行说明
tools/                     # 本地运维、上传、清理和启动脚本
```

## 品牌迁移说明

`astralfox-alife` 是当前项目的对外展示名称。后续如需彻底迁移内部命名空间、程序集、路径和存储键，应单独制定迁移计划，并配套插件加载、配置兼容和历史数据迁移测试。

## License

本项目沿用当前仓库的许可证文件，详见 [LICENSE](LICENSE)。
