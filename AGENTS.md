# AGENTS.md

## Product scope

TransDuck is an independent Windows and macOS translation product. Windows is
distributed as the self-contained portable ZIP `TransDuck-Windows-x64.zip`.
macOS is distributed as architecture-specific ZIPs containing `TransDuck.app`:
`TransDuck-macOS-x64.zip` and `TransDuck-macOS-arm64.zip`. Do not add MSIX, a
platform installer, or an automatic update channel as a release target. The
Windows baseline is Windows 10 on x64. The macOS baseline is macOS 14 or later
on Intel x64 and Apple Silicon arm64. macOS ZIPs are unsigned and self-contained,
with one `.app` bundle per architecture.

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
- Treat `assets/brand-source-icon/icon_source.png` as the only brand icon source.
  Regenerate PNG/ICO/ICNS derivatives with `windows/packaging/New-AppIcon.ps1`;
  do not replace it with a downloaded visual asset without explicit approval.

## Architecture and presentation

- Keep contracts, provider HTTP behavior, proxy transport, and file persistence
  in platform-neutral projects under `shared/`. Platform projects may depend on
  shared projects; shared projects must not reference WPF, Avalonia, Win32,
  Apple frameworks, OCR runtimes, or platform credential stores.
- Keep the cross-platform Avalonia windows, localization resources, and
  presentation models in `ui/TransDuck.UI`. Platform app projects derive narrow
  adapters from those windows and keep native lifecycle, permission, credential,
  hotkey, and OCR behavior local.
- Keep floating-window surfaces opaque and theme-aware so result text and
  status messages remain readable in both light and dark themes, including
  live theme changes.
- Display the current product version in each platform's primary and settings
  windows, derived from assembly informational metadata rather than hard-coded
  UI text.
- Store macOS non-secret data in the per-user application-support directory and
  credentials in macOS Keychain. Never fall back to plaintext credential files.
- macOS global selection requires Accessibility permission and screenshot OCR
  requires Screen Recording permission. Permission denial must leave manual
  translation usable.

## Translation and dictionaries

- Query every enabled translation provider independently and present
  source-labeled results without letting one provider failure erase another
  provider's output.
- Preserve completed source cards across retries and retry only the retryable
  failed sources; do not repeat successful provider calls or duplicate their
  history entries.
- Present the file-backed source as "Local dictionary", including in internal
  model and provider names. Accept UTF-8 CSV files with `word`, `phonetic`,
  `definition`, `translation`, and `pos` columns, or SQLite `stardict` tables
  with those columns plus `sw`; ECDICT is a compatible external data source,
  not the product-facing provider name. Do not redistribute dictionary data.
  CSV query caches belong below the platform app-data root and must never
  modify or lock the source file after a lookup completes.
- Pronounce matched dictionary terms with the operating system's installed
  speech voices. Pronunciation stays local and must not fetch or play
  dictionary-provided audio URLs.
- On macOS, system dictionary lookup uses the user's active Dictionary Services
  sources. Dictionary text stays local; diagnostics must not include lookup
  terms, definitions, or dictionary file paths.

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

Record durable repository design constraints, assumptions, and accepted user
suggestions in this root `AGENTS.md`. Keep temporary plans, execution notes,
progress updates, and one-off background out of this file.

Keep public English documentation under `docs/user-docs/en/` and Simplified
Chinese documentation under `docs/user-docs/zh/`. Use relative repository links,
describe only verified behavior, and do not record transient artifact hashes or
test counts as long-term product facts.
