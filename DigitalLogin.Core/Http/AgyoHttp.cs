using System.Text.Json;
using System.Text.Json.Nodes;

namespace DigitalLogin.Http;

/// <summary>The service answered with a non-2xx status. <see cref="Exception.Message"/> is user-facing.</summary>
public sealed class AgyoApiException(int statusCode, string message) : Exception(message)
{
    public int StatusCode { get; } = statusCode;
}

/// <summary>Helpers shared by the Agyo API clients.</summary>
public static class AgyoHttp
{
    private const int MaxChars = 2000;

    public static Uri ValidateBaseUrl(string? baseUrl)
    {
        if (!Uri.TryCreate(baseUrl?.Trim(), UriKind.Absolute, out var uri) ||
            (uri.Scheme != Uri.UriSchemeHttps && uri.Scheme != Uri.UriSchemeHttp))
        {
            throw new ArgumentException("Environment URL must be an absolute http(s) URL.", nameof(baseUrl));
        }

        return uri;
    }

    /// <summary>Builds the "HTTP 401 Unauthorized: ..." message from a non-2xx response.</summary>
    public static AgyoApiException ApiException(int status, string? reasonPhrase, string body)
    {
        var detail = ErrorMessage(body) ?? Truncate(body);
        return new AgyoApiException(status, $"HTTP {status} {reasonPhrase}: {detail}");
    }

    /// <summary>The "message" field of an ApiError body, when there is one.</summary>
    public static string? ErrorMessage(string body)
    {
        try
        {
            using var doc = JsonDocument.Parse(body);
            if (doc.RootElement.ValueKind == JsonValueKind.Object
                && doc.RootElement.TryGetProperty("message", out var el)
                && el.ValueKind == JsonValueKind.String)
            {
                return el.GetString();
            }
        }
        catch (JsonException)
        {
            // Not JSON: the caller falls back to the raw body.
        }

        return null;
    }

    public static string Pretty(string json)
    {
        try
        {
            return JsonNode.Parse(json)?.ToJsonString(new JsonSerializerOptions { WriteIndented = true }) ?? json;
        }
        catch (JsonException)
        {
            return json;
        }
    }

    public static string Truncate(string text) => text.Length <= MaxChars ? text : text[..MaxChars] + "…";

    /// <summary>A string property, or null when it is missing or not a string.</summary>
    public static string? Str(JsonElement obj, string property) =>
        obj.ValueKind == JsonValueKind.Object && obj.TryGetProperty(property, out var el) && el.ValueKind == JsonValueKind.String
            ? el.GetString()
            : null;

    /// <summary>A boolean property, or null when it is missing or not a boolean.</summary>
    public static bool? Bool(JsonElement obj, string property) =>
        obj.ValueKind == JsonValueKind.Object && obj.TryGetProperty(property, out var el) && el.ValueKind is JsonValueKind.True or JsonValueKind.False
            ? el.GetBoolean()
            : null;

    /// <summary>A nested object property, or a default element (ValueKind Undefined) when missing.</summary>
    public static JsonElement Obj(JsonElement obj, string property) =>
        obj.ValueKind == JsonValueKind.Object && obj.TryGetProperty(property, out var el) && el.ValueKind == JsonValueKind.Object
            ? el
            : default;
}
