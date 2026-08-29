# AGENTS.md

## Product scope

TransDuck is an independent, Windows-only translation product. Its public
distribution is the self-contained portable ZIP
`TransDuck-Windows-x64.zip`; do not add MSIX, an installer, or an automatic
update channel as a release target.

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
- Preserve the inherited black-and-white icon asset; do not replace it with a
  generated or downloaded visual asset without explicit approval.

## Local verification

Run the smallest relevant local command first. For the Windows solution, the
usual checks are:

```powershell
dotnet build windows/TransDuck.Windows.sln --configuration Release
dotnet test windows/TransDuck.Windows.sln --configuration Release --no-build
```

Tests must use fakes or task-local loopback endpoints for translation providers;
they must not claim real Bing, Google, proxy, or other external-service
connectivity. Verify Markdown changes with whitespace and relative-link checks.

## Documentation

Keep public English documentation under `docs/user-docs/en/` and Simplified
Chinese documentation under `docs/user-docs/zh/`. Use relative repository links,
describe only verified behavior, and do not record transient artifact hashes or
test counts as long-term product facts.
