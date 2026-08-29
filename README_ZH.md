# TransDuck

TransDuck 是一款便携式 Windows 翻译应用。先在其他应用中选中文本，再按可配置的
快捷键，就能在不离开当前任务的情况下看到翻译结果；也支持截图 OCR 翻译。

目标平台为 Windows 10 x64 桌面会话，当前发布验证基线为单显示器环境。

## 主要功能

- 选中单词、句子或段落后，按默认 `Ctrl+Alt+D` 进行自动翻译。
- 截取屏幕区域，先在本机识别简体中文或英文，再翻译识别结果。
- 在设置中选择 Bing、Google、OpenAI-compatible、DeepL 或 Ollama。
- 使用 Windows 系统代理、自定义 HTTP 代理或直连。
- 为当前 Windows 用户启用登录时启动。

## 安装

TransDuck 只发布自包含 x64 便携包 `TransDuck-Windows-x64.zip`。没有安装程序、
MSIX 包或自动更新。请完整解压 ZIP 到一个固定且可写的目录，再从该目录运行应用。

详见[中文安装与使用说明](docs/user-docs/zh/install-windows.md)或
[English guide](docs/user-docs/en/install-windows.md)。

## 翻译服务与隐私

内置 Bing 和 Google 使用的是非官方网页协议，不是 Azure Translator、Google Cloud
Translation 或 Google Cloud Translation Basic v2。Google 提供商没有 Google Cloud
API Key 配置；网页协议或服务端行为变更时，它们可能失效。

翻译文本会发送给设置中选定的服务商。API Key 与可选的 Bing Cookie 会使用当前
Windows 用户的 DPAPI 保护。设置、历史记录、诊断信息和受保护凭据存放在便携目录
之外的 `%LocalAppData%\TransDuck`。

应用图标复用既有的黑白视觉资产，作为迁移资产继承；未生成或重绘新的图标。

## 安全提示

便携 ZIP 未签名。Windows 可能显示发布者、SmartScreen 或其他安全提示。运行前请核实
发布来源，不要为了跳过提示而关闭系统安全防护。

## 开发

仓库规则见 [AGENTS.md](AGENTS.md)。仓库默认仅本地使用；除非用户明确要求，不配置
remote，也不 push。

English: [README.md](README.md)
