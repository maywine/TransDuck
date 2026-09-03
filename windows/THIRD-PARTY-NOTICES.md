# Windows third-party notices

本目录的 Windows 交付物包含下列第三方组件。模型和 native runtime 都在构建/打包时
随应用复制；运行时不会下载模型、许可证或 native 二进制。

## Microsoft .NET runtime

- The self-contained Windows package embeds the managed .NET runtime in `TransDuck.exe`.
- The package includes `licenses/Microsoft-DotNet-Library-License.txt` and
  `licenses/Microsoft-DotNet-Third-Party-Notices.txt` from the .NET distribution used to publish it.
- Upstream license information: <https://github.com/dotnet/core/blob/main/license-information.md>

## Avalonia UI

- Avalonia UI 12.1.1, including its Win32, Skia, HarfBuzz, Fluent theme, and Inter font
  integration packages, is used under the MIT License. See `Avalonia-MIT.txt`,
  `SkiaSharp-MIT.txt`, and `HarfBuzzSharp-MIT.txt`.
- Notices for third-party material incorporated into the native SkiaSharp and HarfBuzzSharp
  binaries are in `SkiaSharp-HarfBuzz-ThirdPartyNotices.txt`.
- The embedded Inter font files are licensed under SIL Open Font License 1.1; see
  `Inter-OFL-1.1.txt`.
- Avalonia's redistributed MicroCom.Runtime dependency is used under the MIT License; see
  `MicroCom-MIT.txt`.
- Avalonia's Windows ANGLE native library is licensed under the BSD 3-Clause terms in
  `Avalonia-ANGLE-BSD-3-Clause.txt`.
- Microsoft.Windows.CsWin32 0.3.287 is an MIT-licensed build-time source generator. It is
  not included as a runtime assembly in the portable ZIP.

## Tesseract .NET wrapper and native runtime

- Component: [`Tesseract` NuGet package 5.2.0](https://www.nuget.org/packages/Tesseract/5.2.0)
- Upstream: [charlesw/tesseract](https://github.com/charlesw/tesseract)
- Copyright: Copyright 2012–2020 Charles Weld
- License: Apache-2.0
- Distributed native files: `tesseract50.dll` and `leptonica-1.82.0.dll` from the package's
  x64 closure.
- `Tesseract.dll` and `tesseract50.dll` are covered by Apache-2.0; the payload includes
  [`third_party/licenses/Apache-2.0.txt`](third_party/licenses/Apache-2.0.txt).
- `leptonica-1.82.0.dll` is covered by the Leptonica BSD-2-Clause license; the payload includes
  [`third_party/licenses/Leptonica-BSD-2-Clause.txt`](third_party/licenses/Leptonica-BSD-2-Clause.txt).

## Microsoft.Data.Sqlite, SQLitePCLRaw, and SQLite

- [`Microsoft.Data.Sqlite` 10.0.11](https://www.nuget.org/packages/Microsoft.Data.Sqlite/10.0.11)
  is used under the MIT License. See `Microsoft.Data.Sqlite-MIT.txt`.
- SQLitePCLRaw 2.1.12 is used under Apache-2.0. Its managed provider and bundled
  `e_sqlite3` native-loader package are covered by `Apache-2.0.txt`.
- The bundled SQLite library is dedicated to the public domain. See
  `SQLite-Public-Domain.txt`.

## System.Speech

- [`System.Speech` 10.0.11](https://www.nuget.org/packages/System.Speech/10.0.11)
  provides Windows Speech API synthesis for local dictionary pronunciation.
- It is used under the MIT License. See `System.Speech-MIT.txt`.
- Pronunciation uses installed Windows voices and does not download or play
  dictionary-provided audio.

## tessdata_best language models

- Upstream: [tesseract-ocr/tessdata_best](https://github.com/tesseract-ocr/tessdata_best)
- Pinned commit: `e12c65a915945e4c28e237a9b52bc4a8f39a0cec`
- License: Apache-2.0; a copy is at
  [`third_party/tesseract/tessdata-best/LICENSE`](third_party/tesseract/tessdata-best/LICENSE).
- Distributed models:
  - `eng.traineddata`: SHA-256
    `8280aed0782fe27257a68ea10fe7ef324ca0f8d85bd2fd145d1c2b560bcb66ba`
  - `chi_sim.traineddata`: SHA-256
    `4fef2d1306c8e87616d4d3e4c6c67faf5d44be3342290cf8f2f0f6e3aa7e735b`

The model provenance and fixed checksums are also recorded in
[`third_party/tesseract/tessdata-best/model-manifest.json`](third_party/tesseract/tessdata-best/model-manifest.json).

The final application payload includes this notice, the component license files listed above, and the
model-specific `tessdata/LICENSE` file.
