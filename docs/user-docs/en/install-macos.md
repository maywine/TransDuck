# Install and use TransDuck on macOS

## Before you begin

TransDuck supports macOS 14 or later. Download the package matching the Mac's
processor:

- Apple Silicon (M-series): `TransDuck-macOS-arm64.zip`
- Intel: `TransDuck-macOS-x64.zip`

Each ZIP contains one self-contained `TransDuck.app`. This repository's packaging
process uses an ad-hoc signature to seal the app's contents, but the app is not
Developer ID signed or notarized. There is no DMG, PKG, installer, or automatic
updater.

## Install and open it for the first time

1. Obtain the matching ZIP from a release source you trust and extract it
   completely.
2. Move `TransDuck.app` to a permanent location such as `/Applications` or your
   user Applications folder. Do not move it after enabling login startup.
3. Open the app in Finder. If Gatekeeper blocks the first launch, verify the
   download source, then use the operating system's **Open Anyway** action under
   System Settings -> Privacy & Security. Available actions vary by macOS version.
   Do not disable Gatekeeper or remove quarantine attributes to bypass the check.
4. Open Settings from the TransDuck menu-bar icon and configure a provider.

TransDuck runs from its menu-bar icon without keeping an icon in the Dock.
Closing an application window hides it while TransDuck continues running; use
**Quit TransDuck** from the menu-bar menu to stop the application.

### “App is damaged and can't be opened”

An incomplete bundle signature or a lost executable permission after extraction
can also cause this warning. Treat it separately from an unidentified-developer
warning. Check the installed app in Terminal, adjusting the path as needed:

```bash
codesign --verify --deep --strict --all-architectures "/Applications/TransDuck.app"
test -x "/Applications/TransDuck.app/Contents/MacOS/TransDuck" && echo executable
```

If signature verification reports `code has no resources but signature indicates
they must be present`, or the second command does not print `executable`, obtain
a complete ZIP generated with the corrected packaging process and replace the
old app as a whole. A valid signature confirms bundle integrity; an app without
notarization may still require the system's **Open Anyway** action.

## Translate selected text

The default global hotkey is `Command+Option+D`.
When a physical shortcut matches, TransDuck consumes it instead of typing the
key into the focused application, preserving terminal selections.

1. Select text in another application.
2. Press the hotkey or choose **Translate selected text** from the menu-bar icon.
3. On a foreground launch, TransDuck asks macOS for Accessibility permission.
   Approve the system request; TransDuck automatically refreshes the permission
   and enables the hotkey when you return to the app. The permission button in
   Settings remains available for a manual retry.

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
provider enabled in Settings. Local dictionary files with the supported schema,
system-voice pronunciation, and the macOS system Dictionary remain local.
See [Local dictionaries and multiple translation results](dictionaries-and-multiple-results.md).

**Start TransDuck when I log in** manages
`~/Library/LaunchAgents/com.transduck.app.plist` for the current user. TransDuck
refuses to overwrite or delete an existing file it cannot recognize as its own.
If you move the app, open and save Settings again to refresh a stale path.
At login, TransDuck starts in the menu bar without opening its main window.

To update, quit TransDuck from its menu-bar menu and replace `TransDuck.app` as
a whole. Removing the app does not automatically remove Application Support data
or Keychain credentials. After restarting, confirm that the version shown in the
main window or Settings matches the release you installed.

See also: [TransDuck README](../../../README_EN.md).
