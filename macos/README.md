# TransDuck for macOS

This directory contains the macOS implementation of TransDuck. It is a .NET 10
Avalonia menu-bar application with narrow adapters for Accessibility, Keychain,
Vision OCR, interactive screen capture, global keyboard hooks, and LaunchAgents.

## Supported systems and packages

- macOS 14 or later
- Intel x64: `TransDuck-macOS-x64.zip`
- Apple Silicon arm64: `TransDuck-macOS-arm64.zip`

Each ZIP contains one unsigned, self-contained `TransDuck.app`. There is no DMG,
PKG, installer, or automatic updater. Signing and notarization require a separate
Apple Developer configuration and are not silently bypassed.

## Build and test locally

From the repository root with a local .NET 10 SDK:

```bash
dotnet restore macos/TransDuck.MacOS.sln
dotnet build macos/TransDuck.MacOS.sln --configuration Release --no-restore
dotnet test macos/TransDuck.MacOS.sln --configuration Release --no-restore --no-build
```

Build and audit both architecture-specific bundles:

```bash
./macos/packaging/package-app.sh osx-x64
./macos/packaging/test-package.sh \
  macos/packaging/artifacts/TransDuck-macOS-x64.zip osx-x64
./macos/packaging/package-app.sh osx-arm64
./macos/packaging/test-package.sh \
  macos/packaging/artifacts/TransDuck-macOS-arm64.zip osx-arm64
```

Set `TRANSDUCK_DOTNET` to an explicit local `dotnet` executable when it is not on
`PATH`. The scripts inherit standard proxy environment variables; they do not
store proxy values.
On macOS, packaging also launches the newly built app in bounded smoke-test mode
when the host architecture matches the target RID. The process must initialize
and cleanly exit before the ZIP is created.

## Platform behavior

- The app sets both `LSUIElement` and Avalonia's `ShowInDock=false`, so it remains
  available from the menu bar without a Dock icon. Closing a window hides it;
  only the explicit menu-bar quit action stops the process.
- The default selected-text hotkey is `Command+Option+D`. The keyboard hook is
  keyboard-only and ignores simulated events; it requires macOS Accessibility
  permission. Foreground launch requests that permission through macOS, and
  application reactivation refreshes the permission and enables the hook.
- Selected text is read through the focused element's Accessibility
  `AXSelectedText` value. Apps that do not expose that value remain usable through
  manual input.
- Interactive capture uses `/usr/sbin/screencapture`; recognized English or
  Simplified Chinese text is produced locally with the macOS Vision framework.
  Task-local PNG files are deleted after recognition or cancellation.
- API keys, Volcengine AK/SK, and an optional Bing Cookie use generic-password
  items in macOS Keychain. They never fall back to the JSON settings directory.
- Non-secret settings, history, and closed structured diagnostics are stored in
  `~/Library/Application Support/TransDuck`.
- Opt-in login startup uses a TransDuck-owned
  `~/Library/LaunchAgents/com.transduck.app.plist` and refuses to overwrite a
  conflicting file. Login startup passes a closed `--background` argument so it
  does not open the main window.

On a real Mac, the test suite also performs noninteractive Vision, Keychain, and
Accessibility-preflight smoke checks; those tests are explicitly skipped on other
hosts. Permission prompts, login behavior, global hotkeys, and window interaction
still require an interactive local macOS session before a public release.
The Release workflow runs the native smoke checks on both arm64 and Intel x64
macOS hosts before publishing either package.

For end-user instructions, see the [English guide](../docs/user-docs/en/install-macos.md)
and [Chinese guide](../docs/user-docs/zh/install-macos.md).
