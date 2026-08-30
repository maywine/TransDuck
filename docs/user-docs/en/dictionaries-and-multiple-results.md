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

## Use a local dictionary

TransDuck accepts user-supplied UTF-8 CSV or SQLite dictionary files on Windows
and macOS. CSV files must contain `word`, `phonetic`, `definition`, `translation`,
and `pos` columns. SQLite files must contain a `stardict` table with `word`, `sw`,
`phonetic`, `definition`, `translation`, and `pos` columns. CSV and SQLite files
published by the [ECDICT project](https://github.com/skywind3000/ECDICT) are
compatible examples. Dictionary data is not included in TransDuck and is never
downloaded by the application.

1. Obtain a supported CSV or SQLite file from a source you trust. Extract
   compressed files before selecting them.
2. Open Settings and choose the dictionary file.
3. Enable **Local dictionary**, then choose **Save result sources**.

No online provider needs to be configured when the local dictionary is the only
enabled source.

For a CSV file, the first
lookup builds a SQLite query cache below the TransDuck application-data directory;
large files can therefore take longer the first time. Later lookups reuse the
cache. TransDuck rebuilds it after the source file's size or modification time
changes or its content checksum no longer matches, including replacements that
preserve size and timestamps. Each CSV lookup validates that checksum before
reusing the cache, so very large CSV files can add a short local read delay. It
does not modify the selected source file and releases the file after each lookup.

Matched entries show the phonetic value supplied by the file and provide a
**Pronounce** button. Pronunciation uses an installed operating-system voice; it
does not read dictionary audio URLs or access the network.

The local dictionary matches complete words and phrases rather than acting as a
general sentence translator. A result card reports **No entry** when the complete
selected text does not match a dictionary entry.

## Use the macOS system Dictionary

On macOS, enable **macOS system Dictionary** to query the dictionaries active for
the current user. The first matching plain-text definition returned by macOS
Dictionary Services appears in its own result card. Dictionary availability and
language coverage depend on the sources enabled in the macOS Dictionary app.
The macOS system Dictionary can also be saved as the only enabled source.

Local dictionary files and macOS system Dictionary lookups run locally. Lookup terms, definitions,
and dictionary file paths are not written to diagnostics. Completed results can
still appear in TransDuck history according to the configured retention limits.

See also: [Windows installation](install-windows.md) and
[macOS installation](install-macos.md).
