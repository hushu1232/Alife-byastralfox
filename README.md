# Alife - 创造赛博生命 (Creating Cyber Life)

![Alife Logo](https://img.shields.io/badge/Alife-AI_Assistant-blue?style=for-the-badge)
![.NET 9](https://img.shields.io/badge/.NET-9.0-512BD4?style=for-the-badge&logo=dotnet)
![Python 3.12](https://img.shields.io/badge/Python-3.12-3776AB?style=for-the-badge&logo=python)

Alife 是一个高度模块化、可扩展的赛博生命（AI 助手）框架。它不仅是一个聊天机器人，更是一个拥有性格、记忆、视觉和语音能力的智能伙伴。

---

## ✨ 软件特色 (Software Features)

*   **🧪 极致环境隔离**：内置私有 Python 3.12 环境与独立的 `.venv` 虚拟环境，确保不污染宿主系统。
*   **🧩 插件化架构**：基于 **Semantic Kernel** 与动态 DLL 加载技术，功能模块可独立插拔、极速扩展。
*   **💻 混合动力 UI**：采用 **WPF + Blazor Hybrid** (AntDesign Blazor) 架构，兼具桌面端的高性能与 Web 端的高颜值。
*   **🛡️ 隐私优先**：核心数据（记忆、配置）本地化存储，视觉模型支持本地推理，打造安全的私密空间。
*   **⚡ 零门槛部署**：高度自动化的 `Launch.cmd` 脚本，自动处理所有运行时环境。

---

## ✅ 已完成功能 (Completed Features)

*   [x] **核心框架**：支持插件动态加载、生命周期管理、本地数据持久化。
*   [x] **角色管理**：支持多角色定义，内置默认角色“真央 (Mao)”。
*   [x] **深度视觉**：集成 InternVL2.5-1B 视觉大模型，支持本地图片分析。
*   [x] **情感语音**：基于 Edge-TTS 的极速在线语音合成。
*   [x] **桌面宠物**：独立的桌面挂件模式，支持实时互动。
*   [x] **社交互联**：通过 OneBot 协议支持 QQ 等聊天平台集成。
*   [x] **隔离运行**：全自动的私有 Python 虚拟环境管理。

---

## 📅 计划功能 (Roadmap)

*   [ ] **多模态记忆**：支持基于向量数据库的长短期记忆检索（RAG）。
*   [ ] **技能工坊**：提供可视化的插件/技能市场，支持在线安装与更新。
*   [ ] **动作捕捉**：为桌面宠物引入更丰富的 2D/3D 动画与表情系统。
*   [ ] **移动端适配**：探索与移动设备的消息推送与联动。
*   [ ] **国产大模型支持**：预设更多主流大模型（如 DeepSeek, Qwen）的极速配置。

---

## 🖼️ 效果案例 (Showcase)

| 场景 | 描述 |
| :--- | :--- |
| **桌面交互** | 角色常驻桌面一角，支持拖拽、对话与反馈。 |
| **图片理解** | 将屏幕截图或图片发送给角色，它能描述其中的文字与场景。 |
| **语音互动** | 角色用温润的声音对你的话语做出回应。 |
| **多角色切换** | 根据不同需求，在不同性格的角色间快速切换。 |

---

## 🏗️ 项目架构 (Architecture)

### 1. 整体架构
Alife 采用分层架构以确保高扩展性：
- **UI 层 (`Alife`)**：WPF 宿主承载 WebView2，运行 Blazor 组件。
- **框架层 (`Alife.Framework`)**：管理插件、存储、角色与会话。
- **功能层 (`Alife.Function`)**：具体的业务插件实现。
- **基础层 (`Alife.Basic`)**：平台适配与通用工具类。

### 2. 如何开发一个插件？ (Developer Guide)
只需三步即可为 Alife 增加新功能：
1.  **创建项目**：新建一个类库项目，引用 `Alife.Framework`。
2.  **编写逻辑**：继承 `Plugin` 基类，并在 `StartAsync` 中注册你的工具。
3.  **编译发布**：将生成的 DLL 放入 `Plugins` 文件夹即可自动加载。

---

## 🛠️ 环境依赖要求

| 类型 | 依赖项 | 管理方式 |
| :--- | :--- | :--- |
| **运行时** | .NET 9, VC++ Redist, Python 3.12 | `Launch.cmd` 自动静默安装 |
| **Python 库** | `torch`, `transformers`, `edge-tts` | 对应插件首次启动时自动安装 |

---

## ❓ 常见问题 (FAQ)

### Q: 出现黑窗错误 "Python was not found..." 或提示从 Microsoft Store 安装？
**A:** 这通常意味着 Python 环境异常，常见原因及对策：
1.  **错误运行方式**：您可能直接运行了 `Alife.exe`。**请务必通过双击根目录下的 `Launch.cmd` 来启动程序**，因为只有它能正确注入私有 Python 路径。
2.  **环境配置中断**：如果第一次运行 `Launch.cmd` 时被强行关闭或网络超时，可能导致环境残缺。
    *   **解决办法**：关闭所有相关窗口，删除根目录下的 **`Runtime`** 和 **`.venv`** 文件夹，然后重新运行 `Launch.cmd`。

### Q: 提示 `RuntimeError: The NVIDIA driver on your system is too old`？
**A:** 这是由于您当前的显卡驱动版本过低，无法支持视觉模块所需的 CUDA 环境。
*   **解决办法**：请前往 [NVIDIA 官网](https://www.nvidia.com/Download/index.aspx) 下载并安装最新的显卡驱动。

---

## 🚀 快速开始

1. **克隆仓库**：
   ```bash
   git clone https://github.com/your-repo/Alife.git
   cd Alife
   ```
2. **启动系统**：
   双击根目录下的 **`Launch.cmd`**。
   > **重要**：请勿直接运行 `Outputs\Alife\Alife.exe`，否则会导致 Python 环境无法加载。

---

## 💖 鸣谢
- 感谢 **InternVL**、**Torch**、**Microsoft Edge TTS** 提供技术支持。

---
*创建于 2026年5月*  
*维护者：Antigravity & Alife 开发者*
