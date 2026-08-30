# Local dictionaries and multiple translation results

## Enable multiple translation services

Settings separates the provider being edited from the result sources that are
enabled. Configure and save each provider first, then select every service whose
result should appear. TransDuck starts the enabled requests concurrently and shows
one labeled result card per service. A timeout or error from one service does not
remove results returned by other services.
**Retry failed sources** keeps completed cards and reruns only retryable failures,
so successful or metered services are not called a second time.

Use **Save result sources** after changing the enabled checkboxes. This is separate
from saving the provider currently being edited.

The same input is sent separately to every enabled online service. Credentials
remain in Windows DPAPI-protected storage or macOS Keychain, but the text must still
be disclosed to each selected provider for translation. Disable any service that
should not receive the text.

## Use ECDICT locally

TransDuck supports the UTF-8 CSV files and SQLite databases produced by the
[ECDICT project](https://github.com/skywind3000/ECDICT) on both Windows and macOS.
The dictionary data is not included in TransDuck and is never downloaded by the
application.

1. Obtain `ecdict.csv`, `ecdict.mini.csv`, or an ECDICT SQLite database from a
   source you trust. Extract compressed files before selecting them.
2. Open Settings and choose the ECDICT data file.
3. Enable **ECDICT local dictionary**, then choose **Save result sources**.

No online provider needs to be configured when ECDICT is the only enabled source.

SQLite files must use ECDICT's `stardict` table shape. For a CSV file, the first
lookup builds a SQLite query cache below the TransDuck application-data directory;
large files can therefore take longer the first time. Later lookups reuse the
cache. TransDuck rebuilds it after the source file's size or modification time
changes or its content checksum no longer matches, including replacements that
preserve size and timestamps. Each CSV lookup validates that checksum before
reusing the cache, so very large CSV files can add a short local read delay. It
does not modify the selected source file and releases the file after each lookup.

ECDICT is an English-to-Chinese word and phrase dictionary, not a general sentence
translator. A result card reports **No entry** when the complete selected text does
not match a dictionary entry.

## Use the macOS system Dictionary

On macOS, enable **macOS system Dictionary** to query the dictionaries active for
the current user. The first matching plain-text definition returned by macOS
Dictionary Services appears in its own result card. Dictionary availability and
language coverage depend on the sources enabled in the macOS Dictionary app.
The macOS system Dictionary can also be saved as the only enabled source.

ECDICT and macOS system Dictionary lookups run locally. Lookup terms, definitions,
and dictionary file paths are not written to diagnostics. Completed results can
still appear in TransDuck history according to the configured retention limits.

See also: [Windows installation](install-windows.md) and
[macOS installation](install-macos.md).
