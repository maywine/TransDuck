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


Switching providers keeps unsaved fields in the current settings window, including
unfinished values. Save each provider separately; saving one does not discard
another provider's draft. Drafts are held only in memory and are discarded when
the settings window closes or **Reload and discard drafts** is selected on macOS.
Saved credentials are never filled back into the form.

## Translate and copy results

Open the manual translation window from another app with **Ctrl+Alt+T** on
Windows or **Command+Option+T** on macOS. The window focuses and selects its input
so typing replaces the previous text. This shortcut does not read the clipboard
or selection and does not start a query. Change it under **Global shortcuts →
Open translation window** in Settings, then choose **Save window shortcut**.
It is saved separately from the selected-text shortcut; the two combinations
must differ. macOS global shortcuts require Accessibility permission. Without
that permission, the menu-bar action still opens the manual input window.

Type or paste text, then choose **Translate**. The input also supports
**Ctrl+Enter** on Windows and **Command+Return** on macOS; Enter alone inserts a
line break. **Cancel** appears while an operation is running, and **Retry failed
sources** appears when retryable failures are available.

**Recognize** beside Screenshot OCR selects the recognition language, not the
translation target. Configure each provider's target language in Settings; it
appears on that provider's result card when the query starts.

Each result card has a **Copy** button for that card's text. **Copy all results**
combines all nonempty cards with their source names.

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
