using System.Net;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using DigitalLogin.Auth;
using Xunit;

namespace DigitalLogin.Tests;

public class AgyoAuthClientTests
{
    private const string BaseUrl = "https://b2b-auth-service-test.agyo.io";
    private const string Secret = "TopSecretValue";

    private static AgyoOptions Options(string secret = Secret) => new()
    {
        BaseUrl = BaseUrl,
        Id = "Company-1",
        Secret = secret,
        CompanyVat = "IT12345678901",
        UserAgent = "DigitalLogin-Test/1.0",
        AppName = "DigitalLogin",
        AppVersion = "9.9.9",
    };

    [Fact]
    public async Task Successful_run_calls_nonce_then_tokens_with_the_computed_digest()
    {
        var handler = new ScriptedHandler(
            (HttpStatusCode.OK, """{"nonce":"n-123"}"""),
            (HttpStatusCode.OK, """{"accessToken":"tok-abcdefghijkl","expiresIn":3600}"""));
        var client = new AgyoAuthClient(new HttpClient(handler));
        var run = new AgyoLoginRun();

        await client.RunAsync(Options(), run);

        Assert.True(run.Succeeded);
        Assert.True(run.Finished);
        Assert.Equal("tok-abcdefghijkl", run.AccessToken);
        Assert.Null(run.Error);

        Assert.Equal(2, handler.Requests.Count);
        Assert.Equal(HttpMethod.Post, handler.Requests[0].Method);
        Assert.Equal(BaseUrl + "/api/v3/nonces", handler.Requests[0].Url);
        Assert.Equal(BaseUrl + "/api/v3/tokens", handler.Requests[1].Url);

        var nonceBody = JsonDocument.Parse(handler.Requests[0].Body).RootElement;
        Assert.Equal("Company-1", nonceBody.GetProperty("id").GetString());

        var tokenBody = JsonDocument.Parse(handler.Requests[1].Body).RootElement;
        Assert.Equal("Company-1", tokenBody.GetProperty("id").GetString());
        Assert.Equal(AgyoDigest.Compute("Company-1", Secret, "n-123"), tokenBody.GetProperty("digest").GetString());
    }

    [Fact]
    public async Task Requests_carry_the_same_headers_as_the_bruno_script()
    {
        var handler = new ScriptedHandler(
            (HttpStatusCode.OK, """{"nonce":"n"}"""),
            (HttpStatusCode.OK, """{"accessToken":"t"}"""));
        var run = new AgyoLoginRun();

        await new AgyoAuthClient(new HttpClient(handler)).RunAsync(Options(), run);

        foreach (var request in handler.Requests)
        {
            Assert.Equal("DigitalLogin-Test/1.0", request.Headers["User-Agent"]);
            Assert.Equal("Company-1", request.Headers["X-User-ID"]);
            Assert.Equal("DigitalLogin", request.Headers["X-App-Name"]);
            Assert.Equal("9.9.9", request.Headers["X-App-Version"]);
            Assert.Equal(run.RequestId, request.Headers["X-Request-ID"]);
            Assert.Equal(run.CorrelationId, request.Headers["X-Correlation-ID"]);
            Assert.Equal("application/json", request.ContentType);
        }

        // Request id is the GUID without dashes; correlation id keeps them.
        Assert.Equal(32, run.RequestId.Length);
        Assert.Equal(36, run.CorrelationId.Length);
        Assert.Equal(run.CorrelationId.Replace("-", ""), run.RequestId);
    }

    [Fact]
    public async Task Nonce_failure_stops_the_run_and_skips_the_token_call()
    {
        var handler = new ScriptedHandler(
            (HttpStatusCode.Unauthorized, """{"error":"unknown company"}"""));
        var run = new AgyoLoginRun();

        await new AgyoAuthClient(new HttpClient(handler)).RunAsync(Options(), run);

        Assert.False(run.Succeeded);
        Assert.True(run.Finished);
        Assert.Null(run.AccessToken);
        Assert.Single(handler.Requests);

        Assert.Equal("Request nonce", run.FailedStep);
        Assert.Equal(401, run.FailedHttpStatus);
        Assert.Contains("HTTP 401", run.Error);

        Assert.Equal(AgyoStepStatus.Failed, run.Steps[0].Status);
        Assert.Equal(401, run.Steps[0].HttpStatus);
        Assert.Equal(AgyoStepStatus.Skipped, run.Steps[1].Status);
        Assert.Equal(AgyoStepStatus.Skipped, run.Steps[2].Status);
    }

    [Fact]
    public async Task Token_failure_is_reported_on_the_token_step()
    {
        var handler = new ScriptedHandler(
            (HttpStatusCode.OK, """{"nonce":"n"}"""),
            (HttpStatusCode.Forbidden, """{"error":"bad digest"}"""));
        var run = new AgyoLoginRun();

        await new AgyoAuthClient(new HttpClient(handler)).RunAsync(Options(), run);

        Assert.False(run.Succeeded);
        Assert.Equal("Request access token", run.FailedStep);
        Assert.Equal(403, run.FailedHttpStatus);
        Assert.Equal(AgyoStepStatus.Succeeded, run.Steps[0].Status);
        Assert.Equal(AgyoStepStatus.Succeeded, run.Steps[1].Status);
        Assert.Equal(AgyoStepStatus.Failed, run.Steps[2].Status);
    }

    [Fact]
    public async Task Response_without_the_expected_field_fails_that_step()
    {
        var handler = new ScriptedHandler((HttpStatusCode.OK, """{"something":"else"}"""));
        var run = new AgyoLoginRun();

        await new AgyoAuthClient(new HttpClient(handler)).RunAsync(Options(), run);

        Assert.False(run.Succeeded);
        Assert.Equal("Request nonce", run.FailedStep);
        Assert.Contains("'nonce'", run.Error);
    }

    [Fact]
    public async Task Access_token_is_redacted_in_the_step_log()
    {
        const string token = "eyJsecretTOKENvalue123";
        var handler = new ScriptedHandler(
            (HttpStatusCode.OK, """{"nonce":"n"}"""),
            (HttpStatusCode.OK, $$"""{"accessToken":"{{token}}"}"""));
        var run = new AgyoLoginRun();

        await new AgyoAuthClient(new HttpClient(handler)).RunAsync(Options(), run);

        var tokenStep = run.Steps[2];
        Assert.DoesNotContain(token, tokenStep.ResponseBody);
        Assert.Contains("[redacted]", tokenStep.ResponseBody);
        Assert.DoesNotContain(token, tokenStep.Detail);
    }

    [Fact]
    public async Task Secret_never_appears_in_the_step_log_or_error()
    {
        var handler = new ScriptedHandler(
            (HttpStatusCode.OK, """{"nonce":"n"}"""),
            (HttpStatusCode.OK, """{"accessToken":"t"}"""));
        var run = new AgyoLoginRun();

        await new AgyoAuthClient(new HttpClient(handler)).RunAsync(Options(), run);

        Assert.True(run.Succeeded);
        var logged = string.Join("\n",
            run.Steps.Select(s => $"{s.Name} {s.Method} {s.Url} {s.Detail} {s.ResponseBody}"));
        Assert.DoesNotContain(Secret, logged);
        Assert.DoesNotContain(Secret, run.Error ?? "");
    }

    [Theory]
    [InlineData("not a url")]
    [InlineData("ftp://host.example")]
    [InlineData("")]
    public async Task Invalid_base_url_is_rejected_before_any_request(string baseUrl)
    {
        var handler = new ScriptedHandler();
        var options = Options() with { BaseUrl = baseUrl };

        await Assert.ThrowsAsync<ArgumentException>(() =>
            new AgyoAuthClient(new HttpClient(handler)).RunAsync(options, new AgyoLoginRun()));

        Assert.Empty(handler.Requests);
    }

    [Fact]
    public async Task Missing_secret_is_rejected_before_any_request()
    {
        var handler = new ScriptedHandler();

        await Assert.ThrowsAsync<ArgumentException>(() =>
            new AgyoAuthClient(new HttpClient(handler)).RunAsync(Options(secret: ""), new AgyoLoginRun()));

        Assert.Empty(handler.Requests);
    }

    [Fact]
    public void Options_ToString_does_not_show_the_secret()
    {
        Assert.DoesNotContain(Secret, Options().ToString());
    }

    /// <summary>Returns the scripted responses in order and records every request it sees.</summary>
    private sealed class ScriptedHandler(params (HttpStatusCode Status, string Body)[] responses) : HttpMessageHandler
    {
        private readonly Queue<(HttpStatusCode Status, string Body)> _responses = new(responses);

        public List<RecordedRequest> Requests { get; } = [];

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            var body = request.Content is null ? "" : await request.Content.ReadAsStringAsync(ct);
            Requests.Add(new RecordedRequest(
                request.Method,
                request.RequestUri!.ToString(),
                body,
                request.Content?.Headers.ContentType?.MediaType,
                request.Headers.ToDictionary(h => h.Key, h => string.Join(",", h.Value))));

            if (_responses.Count == 0)
                throw new InvalidOperationException($"Unexpected request to {request.RequestUri}");

            var (status, content) = _responses.Dequeue();
            return new HttpResponseMessage(status)
            {
                Content = new StringContent(content, Encoding.UTF8, "application/json"),
            };
        }
    }

    private sealed record RecordedRequest(
        HttpMethod Method,
        string Url,
        string Body,
        string? ContentType,
        Dictionary<string, string> Headers);
}
