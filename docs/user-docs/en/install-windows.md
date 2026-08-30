# Install and use TransDuck on Windows

## Before you begin

TransDuck supports Windows 10 x64 desktop sessions. It is a portable,
self-contained application: it does not use an installer, MSIX package, or
automatic update service.

The release package is named `TransDuck-Windows-x64.zip`.

## Install

1. Obtain `TransDuck-Windows-x64.zip` from the release source you trust.
2. Extract the entire ZIP to a permanent writable folder, such as a folder
   under your user profile. Do not run the program from inside the ZIP.
3. Keep all extracted files together. The application, .NET runtime, OCR
   runtime, language models, notices, and licenses are one portable unit.
4. Start TransDuck from the extracted folder.

The ZIP is unsigned. Windows can display a publisher, SmartScreen, or other
security warning. Check the source and the downloaded file before deciding
whether to run it; do not turn off Windows security features just to bypass a
warning.

## Translate selected text

1. In another application, select a word, sentence, or paragraph.
2. Press the translation hotkey. The default is `Ctrl+Alt+D`.
3. TransDuck reads the selection, queries every enabled result source, and shows
   a separate result card for each source in the floating window.

Change the hotkey and enabled result sources in Settings. Some applications do
not expose their selection to Windows accessibility APIs. In that case,
TransDuck can report that it cannot obtain the selection; use the input window
to enter text manually.

## Screenshot OCR translation

Open the screenshot OCR action in TransDuck, drag over the text region, and
choose the OCR language if needed. TransDuck recognizes Simplified Chinese or
English locally and places the recognized text in the input window. Choose
**Translate** to query the enabled result sources.

OCR quality depends on the source image, text size, contrast, and language.
Use a tight selection around clear text for better results.

## Choose a translation provider

Select and configure a provider in Settings.

- **Bing** and **Google** use built-in unofficial web interfaces rather than
  Azure Translator or Google Cloud Translation. Web service or protocol changes
  can affect availability.
- **Google** web translation needs no credential; a **Bing** Cookie is optional.
- OpenAI-compatible, DeepL, Ollama, and Volcengine Translate use their respective
  endpoint, model, or credential settings.

A translation request sends selected text to every online provider enabled in
Settings. API keys, Volcengine AK/SK, and an optional Bing Cookie are encrypted
for the current user with Windows DPAPI; they are not written to ordinary
configuration files or the portable application directory.

Windows also supports a user-supplied CSV or SQLite local dictionary with the
supported schema, plus pronunciation through an installed system voice. See
[Local dictionaries and multiple translation results](dictionaries-and-multiple-results.md).

## Configure a proxy

Under Settings, select one of these translation connection modes:

- **System default** uses the Windows/.NET proxy configuration.
- **Custom HTTP proxy** accepts only `http://host:port`, with no username,
  password, path, query, or fragment.
- **Direct connection** bypasses the configured proxy.

`localhost` and loopback destinations always connect directly. Proxy changes
apply to new translation requests; an already-running request keeps the
connection policy with which it started.

## Login startup and updates

Enable **Start at login** in Settings only after moving the extracted folder to
its permanent location. TransDuck creates a startup entry for the current
Windows user; it does not need administrator rights. If you move the folder
later, disable and re-enable the setting to refresh that entry.

Updates are manual: exit TransDuck from its tray menu, extract the new ZIP, and
replace the old portable folder as a whole. Do not copy only the executable.
After restarting, confirm that the version shown in the result window or Settings
matches the release you installed.

## Data and privacy

TransDuck keeps user data separately from the portable folder:

```text
%LocalAppData%\TransDuck
```

This location can contain settings, translation history, diagnostics, and
Windows-DPAPI-protected credentials. Replacing or deleting the portable folder
does not remove that data. Clear history in the application when needed, and
remove the data folder yourself only after TransDuck is closed.


See also: [TransDuck README](../../../README_EN.md).
