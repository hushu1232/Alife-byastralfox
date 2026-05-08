
下面都是ai写的，很多都错的，目前还在全力开发中，大部分功能都ok，确实是一个可用的状态，不够很多细节我还不满意。还有ai写代码还是太糟糕了，功能性代码还是得人工翻新一遍才好。

如果想看有啥功能，以及框架的使用方法，看sources/Alife.Implement就好。

---

# Alife - 创造赛博生命 (Creating Cyber Life)

![Alife Logo](https://img.shields.io/badge/Alife-AI_Assistant-blue?style=for-the-badge)
![.NET 9](https://img.shields.io/badge/.NET-9.0-512BD4?style=for-the-badge&logo=dotnet)
![Python 3.12](https://img.shields.io/badge/Python-3.12-3776AB?style=for-the-badge&logo=python)

Alife 是一个高度模块化、可扩展的赛博生命（AI 助手）框架。它不仅是一个聊天机器人，更是一个拥有性格、记忆、视觉和语音能力的智能伙伴。

---

## ✨ 软件特色 (Software Features)

*   **🧪 极致环境隔离**：内置私有 Python 3.12 嵌入式环境，完全独立运行，不污染宿主系统。
*   **🧩 插件化架构**：基于 **Semantic Kernel** 与动态 DLL 加载技术，功能模块可独立插拔、极速扩展。
*   **💻 混合动力 UI**：采用 **WPF + Blazor Hybrid** (AntDesign Blazor) 架构，兼具桌面端的高性能与 Web 端的高颜值。
*   **🛡️ 隐私优先**：核心数据（记忆、配置）本地化存储，视觉模型支持本地推理，打造安全的私密空间。
*   **⚡ 零门槛部署**：高度自动化的 `Launch.cmd` 脚本，自动处理所有运行时环境（.NET, VC++, Python）。

---

## 🌟 核心功能 (Core Features)

### 🎭 灵魂赋予：默认角色“真央” (Mao)
系统内置了默认角色 **真央**，一只诞生自数字海洋的橘发猫娘。
- **性格**：活泼好奇，古灵精怪，对主人有着绝对的温柔。
- **交互**：支持 Live2D 表情互动、触摸反馈及跨窗口追踪。

### 👁️ 深度视觉 (Vision)
集成 **InternVL2.5-1B** 视觉大模型，让 AI 能够“看见”并理解你分享的图片。
- **功能**：支持屏幕截图分析（LookScreen）、本地/网络图片分析（LookImage）。
- **兼容模式**：若未检测到 CUDA 环境，系统将自动降级为“基础分析模式”，仅提供窗口元数据分析。

### 🎙️ 情感语音 (Speech)
基于 **edge-tts** 技术，提供自然流畅 of 语音交互，支持多种音色切换。

### 🧠 记忆系统 (Memory)
支持基于向量数据库的长短期记忆（RAG），自动管理和分层压缩对话记忆。

### 💬 社交互联 (QChat)
通过 **OneBot** 协议，轻松接入 QQ 等社交平台进行远程交互。

### 🛠️ 进阶能力 (Skills & MCP)
- **Skill 系统**：支持渐进式工具包加载，通过手册引导 AI 完成复杂任务。
- **MCP 支持**：原生集成 **Model Context Protocol**，可连接外部工具服务器。

---

## ✅ 已完成功能 (Completed Features)

*   [x] **核心框架**：支持插件动态加载、生命周期管理、本地数据持久化。
*   [x] **五层分层架构**：Basic、Framework、Function、Implement、UI 彻底解耦。
*   [x] **深度视觉**：集成 InternVL2.5-1B 视觉大模型，支持本地/实时截图分析。
*   [x] **持久记忆**：基于分层压缩与向量检索的长期记忆系统。
*   [x] **桌面宠物**：独立的桌面挂件模式，支持 Live2D 交互。
*   [x] **社交互联**：通过 OneBot 协议支持 QQ 等聊天平台集成。
*   [x] **隔离运行**：全自动的私有 Python 嵌入式环境管理。
*   [x] **大模型支持**：预设 DeepSeek、OpenAI 兼容协议支持及思考过程解析。

---

## 📅 计划功能 (Roadmap)

*   [ ] **技能工坊**：提供可视化的插件/技能市场，支持在线安装与更新。
*   [ ] **动作捕捉**：为桌面宠物引入更丰富的 3D 动画与表情系统。
*   [ ] **跨平台 UI**：探索基于 **Avalonia** 的 Linux/macOS 端适配。
*   [ ] **多模态增强**：进一步优化视觉与记忆的深度融合。

---

## 🏗️ 项目架构 (Architecture)

Alife 采用五层分层架构以确保高扩展性：

1.  **展现层 (`Alife`)**：WPF 宿主承载 WebView2，运行 Ant Design Blazor 组件。
2.  **业务实现层 (`Alife.Implement`)**：组装能力为 AI 可用的工具服务（如 VisionService、ChatService）。
3.  **功能模块层 (`Alife.Function`)**：特定领域的重型能力（AI 模型推理、桌宠行为、语音、浏览器驱动）。
4.  **核心框架层 (`Alife.Framework`)**：管理插件协议、存储、角色契约与会话流程。
5.  **基础设施层 (`Alife.Basic`)**：封装底层 OS 能力（Win32 P/Invoke、路径管理、屏幕捕获）。

---

## 🛠️ 环境依赖要求

| 类型 | 依赖项 | 管理方式 |
| :--- | :--- | :--- |
| **运行时** | .NET 9, VC++ Redist, Python 3.12 | `Launch.cmd` 自动检测并全自动静默安装 |
| **Python 库** | `torch`, `transformers`, `edge-tts` | 对应插件首次启动时自动通过镜像站安装 |

---

## 🚀 快速开始

1. **克隆仓库**：
   ```bash
   git clone https://github.com/your-repo/Alife.git
   cd Alife
   ```
2. **启动系统**：
   双击根目录下的 **`Launch.cmd`**。
   > **重要**：请勿直接运行 `Outputs\Alife\Alife.exe`，否则会导致 Python 环境变量注入失败。

---

## 💖 鸣谢
- 感谢 **InternVL**、**Semantic Kernel**、**Ant Design Blazor** 提供技术支持。

---
*更新于 2026年5月*  
*维护者：Antigravity & Alife 开发者*
