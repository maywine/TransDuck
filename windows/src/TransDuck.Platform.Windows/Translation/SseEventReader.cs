using System.Runtime.CompilerServices;
using System.IO;
using System.Text;

namespace TransDuck.Platform.Windows.Translation;

/// <summary>
/// Parses line-oriented Server-Sent Events, including CRLF and multiline data fields.
/// </summary>
internal static class SseEventReader
{
    public static async IAsyncEnumerable<ServerSentEvent> ReadAsync(
        Stream stream,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        using var reader = new StreamReader(stream, Encoding.UTF8, detectEncodingFromByteOrderMarks: true,
            bufferSize: 4096, leaveOpen: true);
        var eventName = "message";
        var data = new StringBuilder();

        while (await reader.ReadLineAsync(cancellationToken).ConfigureAwait(false) is { } line)
        {
            if (line.Length == 0)
            {
                if (data.Length > 0)
                {
                    data.Length--;
                    yield return new ServerSentEvent(eventName, data.ToString());
                }

                eventName = "message";
                data.Clear();
                continue;
            }

            if (line[0] == ':')
            {
                continue;
            }

            var separator = line.IndexOf(':');
            var field = separator >= 0 ? line[..separator] : line;
            var valueStart = separator + 1;
            if (separator >= 0 && valueStart < line.Length && line[valueStart] == ' ')
            {
                valueStart++;
            }

            var value = separator >= 0 ? line[valueStart..] : string.Empty;
            switch (field)
            {
                case "event":
                    eventName = value;
                    break;
                case "data":
                    data.Append(value).Append('\n');
                    break;
            }
        }

        if (data.Length > 0)
        {
            data.Length--;
            yield return new ServerSentEvent(eventName, data.ToString());
        }
    }
}

internal sealed record ServerSentEvent(string EventName, string Data);
