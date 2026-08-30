# AGENTS.md

## Product scope

TransDuck is an independent Windows and macOS translation product. Windows is
distributed as the self-contained portable ZIP `TransDuck-Windows-x64.zip`.
macOS is distributed as architecture-specific ZIPs containing `TransDuck.app`:
`TransDuck-macOS-x64.zip` and `TransDuck-macOS-arm64.zip`. Do not add MSIX, a
platform installer, or an automatic update channel as a release target. The
macOS baseline is macOS 14 or later on Intel x64 and Apple Silicon arm64.

## Working rules

- Keep changes scoped to the request and preserve unrelated working-tree
  changes.
- Build, test, lint, and package locally. Do not use SSH, remote builders,
  remote CI, or external provider endpoints unless the user explicitly asks.
- Do not configure a Git remote, push, create a release, or commit unless the
  user explicitly authorizes that action. A required local commit uses
  `git commit -s`.
- Do not put API keys, Cookies, proxy credentials, user text, or other secrets
  in source, fixtures, logs, documentation, or release artifacts.
- Treat Bing and Google as unofficial web providers. Do not document or add
  Google Cloud Translation Basic v2 configuration unless a separate request
  explicitly authorizes it.
- Treat `assets/brand-source-icon/icon_source.png` as the approved icon source.
  Regenerate PNG/ICO/ICNS derivatives with `windows/packaging/New-AppIcon.ps1`;
  do not replace it with a downloaded visual asset without explicit approval.

## Local verification

Run the smallest relevant local command first. For the Windows solution, the
usual checks are:

```powershell
dotnet build windows/TransDuck.Windows.sln --configuration Release
dotnet test windows/TransDuck.Windows.sln --configuration Release --no-build
```

For the macOS solution, the usual platform-independent checks are:

```bash
dotnet build macos/TransDuck.MacOS.sln --configuration Release
dotnet test macos/TransDuck.MacOS.sln --configuration Release --no-build
```

Native macOS UI, permission, Keychain, and login-start behavior still requires
final verification in a local interactive macOS session.

Tests must use fakes or task-local loopback endpoints for translation providers;
they must not claim real Bing, Google, proxy, or other external-service
connectivity. Verify Markdown changes with whitespace and relative-link checks.

## Documentation

Keep public English documentation under `docs/user-docs/en/` and Simplified
Chinese documentation under `docs/user-docs/zh/`. Use relative repository links,
describe only verified behavior, and do not record transient artifact hashes or
test counts as long-term product facts.
