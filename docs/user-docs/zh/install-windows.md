# 在 Windows 上安装和使用 TransDuck

## 开始前

TransDuck 支持 Windows 10 x64 桌面会话。它是自包含的便携应用：不使用安装程序、
MSIX 包或自动更新服务。

发布包名称为 `TransDuck-Windows-x64.zip`。

## 安装

1. 从你信任的发布来源获取 `TransDuck-Windows-x64.zip`。
2. 将 ZIP **完整解压**到固定且可写的目录，例如用户目录下的一个专用文件夹。不要在
   ZIP 内直接运行程序。
3. 保持解压出的全部文件在一起。应用、.NET 运行时、OCR 运行时、语言模型、声明和
   许可证共同组成一个便携单元。
4. 从解压目录启动 TransDuck。

ZIP 未签名。Windows 可能显示发布者、SmartScreen 或其他安全提示。请先核实来源和
下载文件，再决定是否运行；不要为了跳过提示而关闭 Windows 安全功能。

## 翻译选中文本

1. 在其他应用中选中单词、句子或段落。
2. 按翻译快捷键，默认是 `Ctrl+Alt+D`。
3. TransDuck 读取选区后，并发查询所有已启用的结果来源，并在悬浮窗口中分别显示结果。

可在设置中修改快捷键和启用的结果来源。一些应用不会通过 Windows 辅助功能 API 提供
选区；此时 TransDuck 会提示无法取得选区，可打开输入窗口手动输入文本。

## 截图 OCR 翻译

在 TransDuck 中打开截图 OCR 功能，拖拽框选文字区域，并在需要时选择 OCR 语言。
TransDuck 会在本机识别简体中文或英文，并将识别结果填入输入窗口；点击“翻译”后查询
所有已启用的结果来源。

OCR 效果取决于原图、文字大小、对比度和语言。尽量紧贴清晰文字框选，可获得更好的
识别结果。

## 选择翻译服务

在设置中选择并配置服务。

- **Bing** 与 **Google** 使用内置的非官方网页接口，不属于 Azure Translator 或
  Google Cloud Translation；网页服务或协议变化可能影响可用性。
- **Google** 网页翻译无需凭据；**Bing** Cookie 为可选项。
- OpenAI-compatible、DeepL、Ollama 和火山翻译按各自要求配置 endpoint、model 或凭据。

每次翻译会将选定文本分别发送给所有已启用的在线服务商。API Key、火山引擎 AK/SK 与可选的
Bing Cookie 由 Windows DPAPI 按当前用户加密，不会写入普通配置文件或便携应用目录。

Windows 也支持用户提供、采用受支持结构的 CSV 或 SQLite 本地词典及系统语音发音，详见
[本地词典与多翻译结果](dictionaries-and-multiple-results.md)。

## 配置代理

在设置中为翻译请求选择以下连接方式之一：

- **系统默认**：使用 Windows/.NET 代理配置。
- **自定义 HTTP 代理**：只接受 `http://host:port`，且不能包含用户名、密码、路径、
  查询参数或片段。
- **直连**：不使用配置的代理。

`localhost` 和 loopback 地址始终直连。代理修改会应用到之后发起的翻译请求；已经开始的
请求会继续使用启动时的连接策略。

## 登录启动与更新

请在将解压目录放到最终位置后，再在设置中启用“登录时启动”。TransDuck 仅为当前
Windows 用户创建启动项，不需要管理员权限。之后如果移动了目录，请关闭并重新启用该
设置，更新启动项路径。

更新需要手动完成：从托盘菜单退出 TransDuck，解压新版 ZIP，然后完整替换旧的便携目录。
不要只复制或替换 executable。

## 数据与隐私

TransDuck 的用户数据与便携目录分开保存：

```text
%LocalAppData%\TransDuck
```

其中可能包含设置、翻译历史、诊断信息和由 Windows DPAPI 保护的凭据。替换或删除便携
目录不会删除这些数据。需要时请在应用内清除历史；如需移除数据目录，请先退出
TransDuck，再自行删除该目录。


另见：[TransDuck README](../../../README.md)。
