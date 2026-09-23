# TransDuck for macOS

This directory contains the macOS implementation of TransDuck. It is a .NET 10
Avalonia menu-bar application with narrow adapters for Accessibility, Keychain,
Vision OCR, interactive screen capture, global keyboard hooks, and LaunchAgents.
Its windows, localization resources, and presentation models are shared with Windows
from `../ui/TransDuck.UI`.

## Supported systems and packages

- macOS 14 or later
- Intel x64: `TransDuck-macOS-x64.zip`
- Apple Silicon arm64: `TransDuck-macOS-arm64.zip`

Each ZIP contains one self-contained `TransDuck.app` with an ad-hoc signature
that seals its contents. It is not Developer ID signed or notarized. There is no
DMG, PKG, installer, or automatic updater. Developer ID signing and notarization
require a separate Apple Developer configuration.

## Build and test locally

From the repository root with a local .NET 10 SDK:

```bash
dotnet restore macos/TransDuck.MacOS.sln
dotnet build macos/TransDuck.MacOS.sln --configuration Release --no-restore
dotnet test macos/TransDuck.MacOS.sln --configuration Release --no-restore --no-build
```

Build and audit both architecture-specific bundles on macOS (required for
`codesign` and native ZIP extraction checks):

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
Packaging moves managed assemblies and other non-Mach-O payloads into
`Contents/Resources/Runtime`, with relative links preserving .NET's dependency
lookup paths. Native code stays in `Contents/MacOS`. `sign-app.sh` signs native
dependencies and then seals the complete app, without extended-attribute
signatures on managed DLLs. The ZIP preserves Unix file types, execute bits,
and symbolic links.

After creating the ZIP, packaging runs `test-package.sh`: it audits the archive,
extracts it with macOS `ditto`, checks the executable permission, and verifies
the complete signature with `codesign --verify --deep --strict`. It also runs
the extracted app's bounded smoke test when the host architecture matches the
target RID. These checks verify bundle integrity and local runtime startup;
they do not establish Gatekeeper approval or notarization. On other operating
systems, `test-package.sh` performs only the static archive audit.

## Platform behavior

- The app sets both `LSUIElement` and Avalonia's `ShowInDock=false`, so it remains
  available from the menu bar without a Dock icon. Closing a window hides it;
  only the explicit menu-bar quit action stops the process.
- The default selected-text hotkey is `Command+Option+D`. The keyboard hook is
  keyboard-only and ignores simulated events; it requires macOS Accessibility
  permission. Foreground launch requests that permission through macOS, and
  application reactivation refreshes the permission and enables the hook.
  Matching physical events are suppressed synchronously before selection work
  is dispatched, so configurable chords do not type into the focused app.
- Selected text is read from the focused element's Accessibility text or selected
  range. If that fails, TransDuck briefly copies the selection and restores the
  previous pasteboard contents. Apps that do not support copying the selection
  remain usable through manual input.
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
