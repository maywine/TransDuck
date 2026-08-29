# TransDuck

![TransDuck 图标](assets/brand-source-icon/icon_128x128.png)

TransDuck 是一款便携式 Windows 翻译应用。先在其他应用中选中文本，再按可配置的
快捷键，就能在不离开当前任务的情况下看到翻译结果；也支持截图 OCR 翻译。

目标平台为 Windows 10 x64 桌面会话，当前发布验证基线为单显示器环境。

## 主要功能

- 选中单词、句子或段落后，按默认 `Ctrl+Alt+D` 进行自动翻译。
- 截取屏幕区域，先在本机识别简体中文或英文，再翻译识别结果。
- 在设置中选择 Bing、Google、OpenAI-compatible、DeepL、Ollama 或火山翻译。
- 使用 Windows 系统代理、自定义 HTTP 代理或直连。
- 为当前 Windows 用户启用登录时启动。

## 安装

TransDuck 只发布自包含 single-file x64 便携包 `TransDuck-Windows-x64.zip`。
没有安装程序、MSIX 包或自动更新。请完整解压 ZIP 到固定且可写的目录，再从该目录
运行 `TransDuck.exe`。

详见[中文安装与使用说明](docs/user-docs/zh/install-windows.md)或
[English guide](docs/user-docs/en/install-windows.md)。

## 翻译服务与数据安全

TransDuck 支持 OpenAI-compatible、DeepL、Ollama、火山翻译，以及内置的 Bing 和
Google 网页翻译。Bing 和 Google 使用非官方网页接口，不属于 Azure Translator 或
Google Cloud Translation；网页服务或协议变化可能影响其可用性。Google 网页翻译无需
凭据，Bing Cookie 为可选项。

翻译时，选中文本会发送给当前选定的服务商。API Key、火山引擎 AK/SK 与可选的
Bing Cookie 不写入普通配置文件，而由 Windows DPAPI 按当前用户加密保存。其他设置、
翻译历史和诊断数据位于 `%LocalAppData%\TransDuck`，不写入便携程序目录。

TransDuck 使用原创的“鸭子 + 双语对话气泡”图标。多尺寸图标已嵌入应用，无需运行时
下载外部图标资源。

## 安全提示

便携 ZIP 未签名。Windows 可能显示发布者、SmartScreen 或其他安全提示。运行前请核实
发布来源，不要为了跳过提示而关闭系统安全防护。

## 许可证

TransDuck 使用 [MIT License](LICENSE)。第三方组件的许可证和声明位于 Windows 发布包的
`licenses/` 目录及 `THIRD-PARTY-NOTICES.md`。

## 发布

向 GitHub 推送任意新 tag 会触发 Release 工作流。工作流在 Windows runner 上完成
Release 编译、测试、single-file ZIP 打包和审计，全部通过后创建对应 GitHub Release，
并上传 `TransDuck-Windows-x64.zip`。同一 tag 的工作流重跑会覆盖同名 ZIP 资产。

## 开发

仓库规则见 [AGENTS.md](AGENTS.md)。仓库默认仅本地使用；除非用户明确要求，不配置
remote，也不 push。

English: [README_EN.md](README_EN.md)
