# Repository design constraints

- Publish the Windows portable ZIP as a self-contained single-file application so managed .NET assemblies are bundled into `TransDuck.exe`.
- Keep the required native WPF runtime, x64 Tesseract/Leptonica libraries, `tessdata`, licenses, and notices as external package files; do not force all content into self-extraction.
- Keep temporary .NET SDKs, CLI home directories, downloads, and package caches used for local verification under the repository-root `.local-dotnet/` directory, which must remain gitignored.
- License TransDuck under the MIT License. Keep the root `README.md` in Simplified Chinese and the full English version in `README_EN.md`; preserve `README_ZH.md` as a compatibility pointer.
- Treat every new Git tag pushed to GitHub as a release trigger. The workflow must build and test on Windows, create and audit only `TransDuck-Windows-x64.zip`, and publish that ZIP to the matching GitHub Release.
