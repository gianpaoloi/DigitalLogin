using System.Diagnostics;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using DigitalLogin.Http;

namespace DigitalLogin.Auth;

/// <summary>
/// Runs the two-call login: POST /api/v3/nonces, compute the digest, POST /api/v3/tokens.
/// Failures are recorded on the run rather than thrown, so the UI can show which step broke.
/// </summary>
public sealed class AgyoAuthClient
{
    private const int MaxBodyChars = 2000;

    private readonly HttpClient _http;

    public AgyoAuthClient(HttpClient http) => _http = http;

    /// <param name="onChange">Called after every step state change, on the caller's context.</param>
    public async Task RunAsync(AgyoOptions options, AgyoLoginRun run, Action<AgyoLoginRun>? onChange = null,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(run);

        var baseUri = ValidateBaseUrl(options.BaseUrl);
        if (string.IsNullOrWhiteSpace(options.Id)) throw new ArgumentException("Company Id is required.", nameof(options));
        if (string.IsNullOrWhiteSpace(options.Secret)) throw new ArgumentException("Secret is required.", nameof(options));

        var clock = Stopwatch.StartNew();
        var nonceStep = run.Steps[0];
        var digestStep = run.Steps[1];
        var tokenStep = run.Steps[2];
        nonceStep.Url = new Uri(baseUri, "/api/v3/nonces").ToString();
        tokenStep.Url = new Uri(baseUri, "/api/v3/tokens").ToString();

        try
        {
            // 1. Nonce
            var nonce = await CallAsync(nonceStep, run, onChange, async () =>
            {
                var body = await PostAsync(nonceStep, new { id = options.Id }, options, run, ct);
                var value = ReadString(body, "nonce");
                return (value, Redact(body, "nonce"), $"nonce {value}");
            });

            // 2. Digest (local, no network)
            digestStep.Status = AgyoStepStatus.Running;
            onChange?.Invoke(run);
            var sw = Stopwatch.StartNew();
            var digest = AgyoDigest.Compute(options.Id, options.Secret, nonce);
            digestStep.Duration = sw.Elapsed;
            digestStep.Status = AgyoStepStatus.Succeeded;
            digestStep.Detail = $"digest {digest[..8]}… ({digest.Length} hex chars)";
            onChange?.Invoke(run);

            // 3. Access token
            await CallAsync(tokenStep, run, onChange, async () =>
            {
                var body = await PostAsync(tokenStep, new { id = options.Id, digest }, options, run, ct);
                var token = ReadString(body, "accessToken");
                run.AccessToken = token;
                return (token, Redact(body, "accessToken"), $"token {Mask(token)}");
            });

            run.Succeeded = true;
        }
        catch (Exception ex)
        {
            var failed = run.Steps.FirstOrDefault(s => s.Status == AgyoStepStatus.Running)
                         ?? run.Steps.FirstOrDefault(s => s.Status == AgyoStepStatus.Pending);
            if (failed is not null)
            {
                failed.Status = AgyoStepStatus.Failed;
                failed.Detail = ex.Message;
            }

            run.Error = ex.Message;
            run.FailedStep = failed?.Name;
            run.FailedHttpStatus = failed?.HttpStatus;
            run.AccessToken = null;

            // Anything after the failure never ran.
            foreach (var s in run.Steps.Where(s => s.Status == AgyoStepStatus.Pending))
                s.Status = AgyoStepStatus.Skipped;
        }
        finally
        {
            run.Duration = clock.Elapsed;
            run.Finished = true;
            onChange?.Invoke(run);
        }
    }

    /// <summary>
    /// Runs one HTTP step: marks it running, awaits the call, and records status, timing and detail.
    /// The call returns (value to pass on, redacted body, short detail text).
    /// </summary>
    private static async Task<string> CallAsync(AgyoStep step, AgyoLoginRun run, Action<AgyoLoginRun>? onChange,
        Func<Task<(string Value, string Body, string Detail)>> call)
    {
        step.Status = AgyoStepStatus.Running;
        onChange?.Invoke(run);

        var sw = Stopwatch.StartNew();
        var (value, body, detail) = await call();
        step.Duration = sw.Elapsed;
        step.ResponseBody = body;
        step.Status = AgyoStepStatus.Succeeded;
        step.Detail = detail;
        onChange?.Invoke(run);
        return value;
    }

    /// <summary>
    /// Sends one POST with the same headers the Bruno script sends. Records the HTTP status on
    /// <paramref name="step"/> whether or not it succeeded, and throws on a non-2xx response.
    /// </summary>
    private async Task<string> PostAsync(AgyoStep step, object payload, AgyoOptions options,
        AgyoLoginRun run, CancellationToken ct)
    {
        var json = JsonSerializer.Serialize(payload);
        using var request = new HttpRequestMessage(HttpMethod.Post, step.Url)
        {
            Content = new StringContent(json, Encoding.UTF8, "application/json"),
        };
        request.Headers.TryAddWithoutValidation("User-Agent", options.UserAgent);
        request.Headers.TryAddWithoutValidation("X-User-ID", options.Id);
        request.Headers.TryAddWithoutValidation("X-App-Name", options.AppName);
        request.Headers.TryAddWithoutValidation("X-App-Version", options.AppVersion);
        request.Headers.TryAddWithoutValidation("X-Request-ID", run.RequestId);
        request.Headers.TryAddWithoutValidation("X-Correlation-ID", run.CorrelationId);

        step.Curl = CurlFormatter.Format(request, json);

        using var response = await _http.SendAsync(request, ct);
        var body = await response.Content.ReadAsStringAsync(ct);
        step.HttpStatus = (int)response.StatusCode;

        if (!response.IsSuccessStatusCode)
        {
            throw new InvalidOperationException(
                $"HTTP {(int)response.StatusCode} {response.ReasonPhrase}: {Truncate(body)}");
        }

        return body;
    }

    /// <summary>Reads a string property from a JSON response, or throws if it is missing.</summary>
    private static string ReadString(string json, string property)
    {
        using var doc = JsonDocument.Parse(json);
        if (doc.RootElement.TryGetProperty(property, out var el)
            && el.ValueKind == JsonValueKind.String
            && !string.IsNullOrEmpty(el.GetString()))
        {
            return el.GetString()!;
        }

        throw new InvalidOperationException($"Response has no '{property}' field: {Truncate(json)}");
    }

    /// <summary>Pretty-prints the response with the named property masked.</summary>
    private static string Redact(string json, string property)
    {
        try
        {
            var node = JsonNode.Parse(json);
            if (node is JsonObject obj && obj.ContainsKey(property))
                obj[property] = "[redacted]";
            return node?.ToJsonString(new JsonSerializerOptions { WriteIndented = true }) ?? Truncate(json);
        }
        catch (JsonException)
        {
            return Truncate(json);
        }
    }

    /// <summary>Shows the start and end of a token, never the whole thing.</summary>
    public static string Mask(string? token) =>
        string.IsNullOrEmpty(token) ? "" : token.Length <= 12 ? "••••" : $"{token[..6]}…{token[^4..]}";

    private static string Truncate(string text) =>
        text.Length <= MaxBodyChars ? text : text[..MaxBodyChars] + $"… ({text.Length - MaxBodyChars} more chars)";

    private static Uri ValidateBaseUrl(string? baseUrl)
    {
        if (!Uri.TryCreate(baseUrl?.Trim(), UriKind.Absolute, out var uri) ||
            (uri.Scheme != Uri.UriSchemeHttps && uri.Scheme != Uri.UriSchemeHttp))
        {
            throw new ArgumentException("Environment URL must be an absolute http(s) URL.", nameof(baseUrl));
        }

        return uri;
    }
}
