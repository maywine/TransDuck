# 在 macOS 上安装和使用 TransDuck

## 开始前

TransDuck 支持 macOS 14 或更高版本。请根据 Mac 的处理器下载对应包：

- Apple Silicon（M 系列）：`TransDuck-macOS-arm64.zip`
- Intel：`TransDuck-macOS-x64.zip`

每个 ZIP 内只有一个自包含的 `TransDuck.app`。当前包未签名、未 notarize，且没有
DMG、PKG、安装程序或自动更新。

## 安装与首次打开

1. 从可信的发布来源取得与处理器匹配的 ZIP，并完整解压。
2. 将 `TransDuck.app` 移到固定位置，例如 `/Applications` 或用户自己的
   `Applications` 目录。启用登录启动后不要再移动它。
3. 在 Finder 中打开应用。若 Gatekeeper 阻止首次启动，请核实下载来源，然后使用
   Finder 的 Control-click ->“打开”，或“系统设置”->“隐私与安全性”中系统提供的
   “仍要打开”。不要关闭 Gatekeeper，也不要删除 quarantine 属性来绕过检查。
4. 从菜单栏的 TransDuck 图标打开 Settings，配置翻译服务。

TransDuck 只在菜单栏显示图标，不在程序坞保留图标。关闭应用窗口只会隐藏窗口，程序仍在
后台运行；需要完全退出时，请从菜单栏菜单选择 **Quit TransDuck**。

## 翻译选中文本

默认快捷键是 `Command+Option+D`。
物理按键与快捷键匹配时，TransDuck 会消费该事件，不再向当前应用输入字符，从而保留终端
选区。

1. 在其他应用中选中文本。
2. 按快捷键，或从 TransDuck 菜单栏选择 **Translate selected text**。
3. 用户在前台启动 TransDuck 时，App 会向 macOS 请求 Accessibility 权限。请确认系统
   授权；返回 App 后，TransDuck 会自动刷新权限并启用快捷键。Settings 中的权限按钮仍可
   用于手动重试。

TransDuck 只读取当前焦点控件公开的 `AXSelectedText`。部分应用不公开该值；遇到这种
情况时可打开 TransDuck 窗口手动粘贴并翻译。

## 截图 OCR 翻译

从窗口或菜单栏选择英文或简体中文 OCR，然后用系统界面框选屏幕区域。首次截图时，
macOS 可能要求 Screen Recording 权限。TransDuck 使用系统 Vision framework 在本机
识别文字。授权后如果系统仍提示权限不足，请退出并重新打开 TransDuck。任务结束或取消
后会删除临时 PNG，再查询所有已启用的翻译和词典来源。

## 翻译服务与代理

Settings 支持 OpenAI-compatible、DeepL、Ollama、火山翻译，以及内置的 Bing 和
Google 网页翻译。Bing 与 Google 使用非官方网页接口，不属于 Azure Translator 或
Google Cloud Translation；服务协议变化可能影响可用性。Google 无需凭据，Bing
Cookie 和 Ollama API Key 可选。

连接方式包括系统默认代理、无凭据的 `http://host:port` 自定义 HTTP 代理和直连。
`localhost` 与 loopback 地址始终直连。修改代理只影响之后创建的请求。

## 数据、Keychain 与登录启动

普通设置、历史和诊断位于：

```text
~/Library/Application Support/TransDuck
```

API Key、火山引擎 AK/SK 和可选 Bing Cookie 作为 generic password 保存在当前用户的
macOS Keychain，不写入普通 JSON、日志或应用目录。翻译文本会分别发送给 Settings 中
已启用的在线服务；采用受支持结构的本地词典文件、系统语音发音和 macOS 系统词典查询
保持本地。详见
[本地词典与多翻译结果](dictionaries-and-multiple-results.md)。

“Start TransDuck when I log in” 会管理当前用户的
`~/Library/LaunchAgents/com.transduck.app.plist`。若该路径已有无法确认归属的文件，
TransDuck 会拒绝覆盖或删除。移动应用后，重新打开 Settings 并保存以刷新旧路径。
通过登录启动时，TransDuck 只常驻菜单栏，不主动打开主窗口。

更新时请先从菜单栏退出 TransDuck，再完整替换 `TransDuck.app`。删除应用不会自动删除
Application Support 数据或 Keychain 凭据。重新启动后，请确认主窗口或 Settings 中显示的
版本与所安装的发布版本一致。

另见：[TransDuck README](../../../README.md)。
