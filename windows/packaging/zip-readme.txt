TransDuck for Windows (x64) / TransDuck Windows 版（x64）

English
1. Extract the entire TransDuck-Windows-x64 directory. Do not run from the ZIP or
   move only selected files: TransDuck.exe contains the managed .NET assemblies,
   while the remaining native WPF/Tesseract libraries, tessdata models, licenses,
   and notices must stay together.
2. Run TransDuck.exe from the extracted directory.
3. Move the whole directory to its final location before enabling the app's
   login-startup option; moving it later can leave a stale startup entry.
4. Updates are manual: close TransDuck, replace the whole extracted directory with
   a newly extracted release, then run TransDuck.exe again. User data is kept
   separately in %LocalAppData%\TransDuck.
5. Bing Translator and Google Translate are unofficial web providers. Google does
   not use a credential. A Bing Cookie is optional and, when saved, is protected
   with DPAPI for the current Windows user.
6. Translation requests can use the Windows system proxy, a custom HTTP proxy, or
   a direct connection. A custom proxy must be http://host:port with no credentials.
   Localhost and loopback requests always connect directly.
7. The application icon is embedded in TransDuck.exe; no external Assets directory
   required.

中文
1. 请完整解压 TransDuck-Windows-x64 目录。不要在 ZIP 内运行，也不要只移动部分文件：
   TransDuck.exe 已包含托管 .NET 程序集；其余 WPF/Tesseract 原生库、tessdata 模型、许可证和
   notices 必须保持在一起。
2. 在解压后的目录中运行 TransDuck.exe。
3. 请先将整个目录移动到最终位置，再启用应用的登录启动选项；之后再移动目录可能留下失效的启动项。
4. 更新需手动完成：关闭 TransDuck，完整替换解压目录，再运行 TransDuck.exe。用户数据位于
   %LocalAppData%\TransDuck，不在 ZIP 内，不会因替换应用目录而删除。
5. Bing 翻译和 Google 翻译都是非官方网页提供商。Google 不使用凭据；Bing Cookie 可选，保存时会使用
   当前 Windows 用户的 DPAPI 保护。
6. 翻译请求可使用 Windows 系统代理、自定义 HTTP 代理或直连。自定义代理必须是无凭据的
   http://host:port。localhost 和 loopback 请求始终直连。
7. 应用图标已内嵌在 TransDuck.exe 中，无需外部 Assets 目录。

Offline OCR / 离线 OCR
The ZIP includes the x64 Tesseract/Leptonica runtime and eng/chi_sim tessdata
models. THIRD-PARTY-NOTICES.md and the licenses directory contain the required
third-party notices and license texts.
ZIP 内含 x64 Tesseract/Leptonica 运行时与 eng/chi_sim tessdata 模型；所需第三方
声明和许可证位于 THIRD-PARTY-NOTICES.md 与 licenses 目录。
