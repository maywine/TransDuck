# TransDuck for Windows

This directory contains the Windows implementation of TransDuck: a WPF desktop
application for selected-text translation and screenshot OCR translation.

TransDuck targets Windows 10 x64 desktop sessions. The validated release
baseline is a single display. Public distribution is the self-contained
portable ZIP `TransDuck-Windows-x64.zip`; MSIX is not a TransDuck release
format.

## Build and test locally

Use a local .NET 10 SDK from the repository root:

```powershell
dotnet restore windows/TransDuck.Windows.sln
dotnet build windows/TransDuck.Windows.sln --configuration Release --no-restore
dotnet test windows/TransDuck.Windows.sln --configuration Release --no-restore --no-build
```

For an interactive desktop session, run:

```powershell
dotnet run --project windows/src/TransDuck.App/TransDuck.App.csproj
```

Unit and integration tests use fakes or task-local loopback endpoints. They do
not establish connectivity to Bing, Google, a proxy, or any other external
service.

## Product behavior

- Select text in another application and press the configurable translation
  hotkey (default: `Ctrl+Alt+D`) to translate it with the selected default
  provider.
- Screenshot OCR recognizes Simplified Chinese or English locally before the
  recognized text is translated.
- Bing and Google use built-in unofficial web interfaces rather than Azure
  Translator or Google Cloud Translation. Google web translation needs no
  credential; a Bing Cookie is optional.
- Translation connections can use the system proxy, a credential-free custom
  `http://host:port` proxy, or direct mode. Loopback destinations always bypass
  the proxy.
- Login startup is explicit and per-user. It must be enabled again after the
  portable directory is moved.

## Package and user data

Package only with the ZIP workflow under `windows/packaging/`. Keep every
published file from the self-contained single-file output together. Managed
.NET assemblies are bundled into `TransDuck.exe`; required native WPF and
Tesseract libraries remain external. Users must not launch only a copied
executable. The portable package is unsigned and can trigger Windows publisher
or SmartScreen warnings.

Runtime settings, history, diagnostics, and protected credentials are kept
outside the portable directory under `%LocalAppData%\TransDuck`. API keys,
Volcengine AK/SK, and an optional Bing Cookie use CurrentUser Windows DPAPI.
Translation text is sent to the provider currently selected by the user.

TransDuck is licensed under the repository-root MIT License. The portable ZIP
includes that license together with the Microsoft .NET, Tesseract, Leptonica,
and model notices required by its binary payload.

The application and tray use the original duck-and-bilingual-speech-bubbles icon
generated for TransDuck. Multi-size resources are embedded in the executable.

For end-user instructions, see the [English guide](../docs/user-docs/en/install-windows.md)
and [Chinese guide](../docs/user-docs/zh/install-windows.md).
