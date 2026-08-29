# TransDuck

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
- Choose Bing, Google, OpenAI-compatible, DeepL, or Ollama in Settings.
- Use the Windows system proxy, a custom HTTP proxy, or a direct connection.
- Enable login startup for the current Windows user.

## Install

TransDuck is distributed only as the self-contained x64 portable package
`TransDuck-Windows-x64.zip`. There is no installer, MSIX package, or automatic
updater. Extract the complete ZIP to a permanent writable folder and run the
application from there.

See the [English installation and use guide](docs/user-docs/en/install-windows.md)
or the [Chinese guide](docs/user-docs/zh/install-windows.md).

## Provider and privacy notes

The built-in Bing and Google providers use unofficial web protocols. They are
not Azure Translator, Google Cloud Translation, or Google Cloud Translation
Basic v2. The Google provider has no Google Cloud API-key setting. Web provider
behavior can change outside TransDuck's control.

Translation text is sent to the provider selected in Settings. API keys and an
optional Bing Cookie are protected for the current Windows user with Windows
DPAPI. Settings, history, diagnostics, and protected credentials are stored
outside the portable folder under `%LocalAppData%\TransDuck`.

The bundled application and icon reuse an inherited black-and-white visual
asset; no new generated icon is used.

## Security notice

The portable ZIP is unsigned. Windows may show a publisher, SmartScreen, or
other security warning. Verify the release source before running it and do not
disable security controls merely to bypass a warning.

## Development

Repository rules are in [AGENTS.md](AGENTS.md). The project is local by default:
do not configure a remote or push unless the user explicitly asks.

中文说明：[README_ZH.md](README_ZH.md)
