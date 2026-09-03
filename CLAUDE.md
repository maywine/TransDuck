# Repository design constraints

- TransDuck supports Windows 10 x64 and macOS 14 or later on x64 and arm64.
- Keep contracts, provider HTTP behavior, proxy transport, and file persistence in
  platform-neutral projects under `shared/`. Platform projects may depend on shared
  projects; shared projects must not reference WPF, Avalonia, Win32, Apple frameworks,
  OCR runtimes, or platform credential stores.
- Keep the cross-platform Avalonia windows, localization resources, and presentation
  models in `ui/TransDuck.UI`. Platform app projects derive narrow adapters from those
  windows and keep native lifecycle, permission, credential, hotkey, and OCR behavior local.
- Windows remains a self-contained portable ZIP. macOS distributions are unsigned,
  self-contained ZIPs containing one `.app` bundle per architecture. Do not add an
  installer or automatic updater without a separate product decision.
- Store macOS non-secret data below `~/Library/Application Support/TransDuck` and
  credentials in macOS Keychain. Never fall back to plaintext credential files.
- macOS global selection requires Accessibility permission and screenshot OCR requires
  Screen Recording permission. Permission denial must leave manual translation usable.
- Keep `assets/brand-source-icon/icon_source.png` as the only brand icon source and
  generate platform derivatives from it.
- Query every enabled translation provider independently and present source-labeled
  results without letting one provider failure erase another provider's output.
- Preserve completed source cards across retries and retry only the retryable failed
  sources; do not repeat successful provider calls or duplicate their history entries.
- Present the file-backed source as "Local dictionary", including in internal model and
  provider names. Accept UTF-8 CSV files with `word`, `phonetic`, `definition`,
  `translation`, and `pos` columns, or SQLite `stardict` tables with those columns plus
  `sw`; ECDICT is a compatible external data source, not the product-facing provider name.
  Do not redistribute dictionary data. CSV query caches belong below the platform app-data
  root and must never modify or lock the source file after a lookup completes.
- Pronounce matched dictionary terms with the operating system's installed speech voices.
  Pronunciation stays local and must not fetch or play dictionary-provided audio URLs.
- Display the current product version in each platform's primary and settings windows,
  derived from assembly informational metadata rather than hard-coded UI text.
- On macOS, system dictionary lookup uses the user's active Dictionary Services sources.
  Dictionary text stays local; diagnostics must not include lookup terms, definitions, or
  dictionary file paths.
