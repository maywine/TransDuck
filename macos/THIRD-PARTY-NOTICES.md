# Third-party notices for TransDuck on macOS

The TransDuck macOS bundle redistributes the following components. The complete
license texts named below are included in the bundle's `Contents/Resources/licenses/`
directory.

## Microsoft .NET

The self-contained application includes the Microsoft .NET runtime. See
`Microsoft-.NET-LICENSE.txt` and `Microsoft-.NET-ThirdPartyNotices.txt`.

## Avalonia UI

Avalonia UI 12.1.1, including its native macOS, Skia, HarfBuzz, Fluent
theme, and Inter font integration packages, is used under the MIT License. See
`Avalonia-MIT.txt`, `SkiaSharp-MIT.txt`, and `HarfBuzzSharp-MIT.txt`. Notices
for third-party material incorporated into the native SkiaSharp and
HarfBuzzSharp binaries are in `SkiaSharp-HarfBuzz-ThirdPartyNotices.txt`.
The embedded Inter font files are licensed under SIL Open Font License 1.1;
see `Inter-OFL-1.1.txt`.

Avalonia's redistributed MicroCom.Runtime dependency is also used under the MIT
License. See `MicroCom-MIT.txt`.

## SharpHook

SharpHook 8.0.0 is used under the MIT License. See `SharpHook-MIT.txt`.

## Microsoft.Data.Sqlite, SQLitePCLRaw, and SQLite

Microsoft.Data.Sqlite 10.0.11 is used under the MIT License. See
`Microsoft.Data.Sqlite-MIT.txt`. SQLitePCLRaw 2.1.12 is used under Apache-2.0;
see `SQLitePCLRaw-Apache-2.0.txt`. The bundled SQLite library is dedicated to
the public domain; see `SQLite-Public-Domain.txt`.

## libuiohook

SharpHook redistributes an unmodified `libuiohook.dylib` built from libuiohook.
libuiohook is licensed under GNU LGPL version 3 and dynamically loaded as a
separate library. See `libuiohook-LGPL-3.0.txt` and
`libuiohook-GPL-3.0.txt`.

The corresponding source used by SharpHook 8.0.0 is available from
<https://github.com/TolikPylypchuk/libuiohook/tree/a41658fb2bef7503a3bcb305ab8bf849755fe906>.
The separate dylib in
`Contents/MacOS/` may be replaced with an interface-compatible modified build
for relinking and debugging.

## Apple system frameworks

The application calls macOS-provided ApplicationServices, CoreFoundation,
CoreServices Dictionary Services, Foundation, Security, and Vision frameworks.
These frameworks are not included in the TransDuck distribution.
