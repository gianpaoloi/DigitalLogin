using System.Text;

namespace DigitalLogin.Http;

/// <summary>Turns a request into a copy-paste curl command, with the same method, URL, headers and body.</summary>
public static class CurlFormatter
{
    /// <param name="message">The request, headers included. Its content is not read.</param>
    /// <param name="body">The request body as sent, or null for none.</param>
    public static string Format(HttpRequestMessage message, string? body = null)
    {
        ArgumentNullException.ThrowIfNull(message);
        ArgumentNullException.ThrowIfNull(message.RequestUri);

        var sb = new StringBuilder();
        sb.Append("curl -X ").Append(message.Method.Method).Append(' ').Append(Quote(message.RequestUri.ToString()));

        foreach (var header in message.Headers)
            foreach (var value in header.Value)
                AppendHeader(sb, header.Key, value);

        if (message.Content is not null)
        {
            foreach (var header in message.Content.Headers)
                foreach (var value in header.Value)
                    AppendHeader(sb, header.Key, value);
        }

        if (!string.IsNullOrEmpty(body))
            sb.Append(" \\\n  --data-raw ").Append(Quote(body));

        return sb.ToString();
    }

    private static void AppendHeader(StringBuilder sb, string name, string value) =>
        sb.Append(" \\\n  -H ").Append(Quote(name + ": " + value));

    /// <summary>Single-quotes a value for a POSIX shell (bash, zsh, Git Bash), escaping embedded quotes.</summary>
    private static string Quote(string value) => "'" + value.Replace("'", "'\\''") + "'";
}
