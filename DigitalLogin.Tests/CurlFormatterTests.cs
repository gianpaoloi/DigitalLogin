using DigitalLogin.Http;
using Xunit;

namespace DigitalLogin.Tests;

public class CurlFormatterTests
{
    [Fact]
    public void Formats_method_url_headers_and_body()
    {
        using var message = new HttpRequestMessage(HttpMethod.Post, "https://example.test/api/v3/tokens?a=1&b=2");
        message.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", "tok");
        message.Headers.TryAddWithoutValidation("X-User-ID", "Company-1");
        message.Content = new StringContent("{}", System.Text.Encoding.UTF8, "application/json");

        var curl = CurlFormatter.Format(message, """{"id":"Company-1"}""");

        Assert.StartsWith("curl -X POST 'https://example.test/api/v3/tokens?a=1&b=2'", curl);
        Assert.Contains("-H 'Authorization: Bearer tok'", curl);
        Assert.Contains("-H 'X-User-ID: Company-1'", curl);
        Assert.Contains("-H 'Content-Type: application/json; charset=utf-8'", curl);
        Assert.EndsWith("--data-raw '{\"id\":\"Company-1\"}'", curl);
    }

    [Fact]
    public void Escapes_single_quotes_for_a_posix_shell()
    {
        using var message = new HttpRequestMessage(HttpMethod.Get, "https://example.test/x");
        message.Headers.TryAddWithoutValidation("X-Name", "O'Brien");

        var curl = CurlFormatter.Format(message);

        Assert.Contains("-H 'X-Name: O'\\''Brien'", curl);
    }
}
