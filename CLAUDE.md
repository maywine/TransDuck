# Repository design constraints

- TransDuck supports Windows 10 x64 and macOS 14 or later on x64 and arm64.
- Keep contracts, provider HTTP behavior, proxy transport, and file persistence in
  platform-neutral projects under `shared/`. Platform projects may depend on shared
  projects; shared projects must not reference WPF, Avalonia, Win32, Apple frameworks,
  OCR runtimes, or platform credential stores.
- Windows remains a self-contained portable ZIP. macOS distributions are unsigned,
  self-contained ZIPs containing one `.app` bundle per architecture. Do not add an
  installer or automatic updater without a separate product decision.
- Store macOS non-secret data below `~/Library/Application Support/TransDuck` and
  credentials in macOS Keychain. Never fall back to plaintext credential files.
- macOS global selection requires Accessibility permission and screenshot OCR requires
  Screen Recording permission. Permission denial must leave manual translation usable.
- Keep `assets/brand-source-icon/icon_source.png` as the only brand icon source and
  generate platform derivatives from it.
