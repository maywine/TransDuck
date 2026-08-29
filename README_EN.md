# TransDuck

![TransDuck icon](assets/brand-source-icon/icon_128x128.png)

TransDuck is a portable Windows translator. Select text in another application,
press a configurable hotkey, and receive a translation without leaving the
current task. It also provides screenshot OCR translation.

TransDuck targets Windows 10 x64 desktop sessions. The release baseline is a
single display.

## Highlights

- Translate a selected word, sentence, or paragraph with the default hotkey
  (`Ctrl+Alt+D` by default).
- Capture a screen region and recognize Simplified Chinese or English locally
  before translating it.
- Choose Bing, Google, OpenAI-compatible, DeepL, Ollama, or Volcengine Translate
  in Settings.
- Use the Windows system proxy, a custom HTTP proxy, or a direct connection.
- Enable login startup for the current Windows user.

## Install

TransDuck is distributed only as the self-contained single-file x64 portable
package `TransDuck-Windows-x64.zip`. There is no installer, MSIX package, or
automatic updater. Extract the complete ZIP to a permanent writable folder and
run `TransDuck.exe` from there.

See the [English installation and use guide](docs/user-docs/en/install-windows.md)
or the [Chinese guide](docs/user-docs/zh/install-windows.md).

## Providers and data protection

TransDuck supports OpenAI-compatible, DeepL, Ollama, Volcengine Translate, and
built-in Bing and Google web translation. Bing and Google use unofficial web
interfaces rather than Azure Translator or Google Cloud Translation, so web
service or protocol changes can affect availability. Google web translation
needs no credential; a Bing Cookie is optional.

Selected text is sent to the provider currently chosen in Settings. API keys,
Volcengine AK/SK, and an optional Bing Cookie are not written to ordinary
configuration files; Windows DPAPI encrypts them for the current user. Other
settings, translation history, and diagnostics are stored under
`%LocalAppData%\TransDuck`, outside the portable application directory.

TransDuck uses an original duck-and-bilingual-speech-bubbles icon. Multi-size
icon resources are embedded in the application and require no runtime download.

## Security notice

The portable ZIP is unsigned. Windows may show a publisher, SmartScreen, or
other security warning. Verify the release source before running it and do not
disable security controls merely to bypass a warning.

## License

TransDuck is licensed under the [MIT License](LICENSE). Third-party licenses and
notices are included in the Windows package under `licenses/` and in
`THIRD-PARTY-NOTICES.md`.

## Release automation

Pushing any new tag to GitHub triggers the Release workflow. A Windows runner
builds and tests the solution, packages and audits the self-contained
single-file ZIP, then creates the matching GitHub Release and uploads
`TransDuck-Windows-x64.zip`. Re-running the workflow for the same tag replaces
the ZIP asset with the newly verified package.

## Development

Repository rules are in [AGENTS.md](AGENTS.md). The project is local by default:
do not configure a remote or push unless the user explicitly asks.

简体中文：[README.md](README.md)
