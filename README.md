# TransDuck

![TransDuck 图标](assets/brand-source-icon/icon_128x128.png)

TransDuck 是一款面向 Windows 和 macOS 的桌面翻译应用。先在其他应用中选中文本，
再按可配置的快捷键，就能在不离开当前任务的情况下看到翻译结果；也支持截图 OCR
翻译和手动输入。

支持 Windows 10 x64，以及 macOS 14 或更高版本的 Intel x64 和 Apple Silicon
arm64 Mac。Windows 当前发布验证基线为单显示器环境。

## 主要功能

- 选中单词、句子或段落后，按默认快捷键进行自动翻译：Windows 为
  `Ctrl+Alt+D`，macOS 为 `Command+Option+D`。
- 截取屏幕区域，先在本机识别简体中文或英文，再翻译识别结果。
- 在设置中选择 Bing、Google、OpenAI-compatible、DeepL、Ollama 或火山翻译。
- 使用系统代理、自定义 HTTP 代理或直连。
- 为当前用户启用登录时启动，并在窗口关闭后继续从任务栏通知区或 macOS 菜单栏使用。

## 安装

TransDuck 发布三个自包含 ZIP，不提供安装程序或自动更新：

- Windows x64：`TransDuck-Windows-x64.zip`
- Intel Mac：`TransDuck-macOS-x64.zip`
- Apple Silicon Mac：`TransDuck-macOS-arm64.zip`

Windows 包需要完整解压后运行 `TransDuck.exe`。macOS 包需要完整解压，并将其中的
`TransDuck.app` 移到固定位置后再启动。

详见 [Windows 中文说明](docs/user-docs/zh/install-windows.md)、
[macOS 中文说明](docs/user-docs/zh/install-macos.md)，以及对应的
[English Windows guide](docs/user-docs/en/install-windows.md) 和
[English macOS guide](docs/user-docs/en/install-macos.md)。

## 翻译服务与数据安全

TransDuck 支持 OpenAI-compatible、DeepL、Ollama、火山翻译，以及内置的 Bing 和
Google 网页翻译。Bing 和 Google 使用非官方网页接口，不属于 Azure Translator 或
Google Cloud Translation；网页服务或协议变化可能影响其可用性。Google 网页翻译无需
凭据，Bing Cookie 为可选项。

翻译时，选中文本会发送给当前选定的服务商。API Key、火山引擎 AK/SK 与可选的
Bing Cookie 不写入普通配置文件。Windows 使用当前用户的 DPAPI，macOS 使用当前用户的
Keychain。其他设置、翻译历史和诊断数据分别位于
`%LocalAppData%\TransDuck` 和 `~/Library/Application Support/TransDuck`，不写入
应用程序目录。

TransDuck 使用原创的“鸭子 + 双语对话气泡”图标。多尺寸图标已嵌入应用，无需运行时
下载外部图标资源。

## 安全提示

发布 ZIP 未签名；macOS 包也未 notarize。Windows 可能显示发布者或 SmartScreen 提示，
macOS 可能显示 Gatekeeper 提示。运行前请核实发布来源，并使用系统提供的“打开”或
“仍要打开”流程；不要为了跳过提示而关闭系统安全防护。

## 许可证

TransDuck 使用 [MIT License](LICENSE)。每个平台发布包都包含其第三方组件许可证和
声明。

## 发布

向 GitHub 推送任意新 tag 会触发 Release 工作流。Windows 与 macOS 的编译、测试、
打包和审计全部通过后，工作流创建或更新对应 GitHub Release，并上传三个 ZIP。

## 开发

仓库规则见 [AGENTS.md](AGENTS.md)。仓库默认仅本地使用；除非用户明确要求，不配置
remote，也不 push。

English: [README_EN.md](README_EN.md)
