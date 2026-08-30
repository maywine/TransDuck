# Install and use TransDuck on macOS

## Before you begin

TransDuck supports macOS 14 or later. Download the package matching the Mac's
processor:

- Apple Silicon (M-series): `TransDuck-macOS-arm64.zip`
- Intel: `TransDuck-macOS-x64.zip`

Each ZIP contains one self-contained `TransDuck.app`. The current package is
unsigned and not notarized, and there is no DMG, PKG, installer, or automatic
updater.

## Install and open it for the first time

1. Obtain the matching ZIP from a release source you trust and extract it
   completely.
2. Move `TransDuck.app` to a permanent location such as `/Applications` or your
   user Applications folder. Do not move it after enabling login startup.
3. Open the app in Finder. If Gatekeeper blocks the first launch, verify the
   download source, then use Finder's Control-click -> **Open** or the operating
   system's **Open Anyway** action under System Settings -> Privacy & Security.
   Do not disable Gatekeeper or remove quarantine attributes to bypass the check.
4. Open Settings from the TransDuck menu-bar icon and configure a provider.

## Translate selected text

The default global hotkey is `Command+Option+D`.

1. Select text in another application.
2. Press the hotkey or choose **Translate selected text** from the menu-bar icon.
3. macOS requests Accessibility permission on first use. After granting it,
   return to TransDuck Settings and click the permission refresh button.

TransDuck reads only the focused control's exposed `AXSelectedText` value. Some
applications do not expose that value; use the TransDuck window to paste and
translate manually in that case.

## Screenshot OCR translation

Choose English or Simplified Chinese OCR from the window or menu-bar menu, then
select a screen region with the system capture UI. macOS may request Screen
Recording permission on first use. TransDuck recognizes text locally with the
system Vision framework. Quit and reopen TransDuck if macOS still reports that
permission is unavailable after it is granted. TransDuck deletes the task-local
PNG after completion or cancellation, then queries the enabled translation and
dictionary sources.

## Providers and proxy

Settings supports OpenAI-compatible, DeepL, Ollama, Volcengine Translate, and
built-in Bing and Google web translation. Bing and Google use unofficial web
interfaces rather than Azure Translator or Google Cloud Translation, so service
or protocol changes can affect availability. Google needs no credential; a Bing
Cookie and an Ollama API Key are optional.

Connection modes are system default, a credential-free custom
`http://host:port` HTTP proxy, and direct. `localhost` and loopback destinations
always connect directly. A proxy change applies only to requests created after
the change.

## Data, Keychain, and login startup

Non-secret settings, history, and diagnostics are stored under:

```text
~/Library/Application Support/TransDuck
```

API keys, Volcengine AK/SK, and an optional Bing Cookie are generic-password
items in the current user's macOS Keychain. They are not written to ordinary
JSON, diagnostics, or the app directory. Translation text is sent to every online
provider enabled in Settings. ECDICT and the macOS system Dictionary remain local.
See [Local dictionaries and multiple translation results](dictionaries-and-multiple-results.md).

**Start TransDuck when I log in** manages
`~/Library/LaunchAgents/com.transduck.app.plist` for the current user. TransDuck
refuses to overwrite or delete an existing file it cannot recognize as its own.
If you move the app, open and save Settings again to refresh a stale path.
At login, TransDuck starts in the menu bar without opening its main window.

To update, quit TransDuck from its menu-bar menu and replace `TransDuck.app` as
a whole. Removing the app does not automatically remove Application Support data
or Keychain credentials.

See also: [TransDuck README](../../../README_EN.md).
