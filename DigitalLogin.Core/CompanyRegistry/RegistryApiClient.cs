using System.Diagnostics;
using System.Net.Http.Headers;
using System.Text.Json;
using DigitalLogin.Http;

namespace DigitalLogin.CompanyRegistry;

/// <summary>Everything one registry lookup needs. The access token is held in memory only.</summary>
public sealed record RegistryRequest
{
    /// <summary>Base URL of the Registry Read API for the environment, without a path.</summary>
    public string BaseUrl { get; init; } = "https://registry-read.agyo.io";

    /// <summary>Bearer token, e.g. the access token from a successful login test.</summary>
    public string AccessToken { get; init; } = "";

    /// <summary>VAT number (partita IVA) of the company to look up.</summary>
    public string VatNumber { get; init; } = "";

    /// <summary>Tax region of the VAT number. The service validates the VAT against it; Italian companies are "IT".</summary>
    public string TaxRegion { get; init; } = "IT";

    public string UserAgent { get; init; } = "DigitalLogin/1.0";
    public string AppName { get; init; } = "DigitalLogin";
    public string AppVersion { get; init; } = "1.0.0";

    public override string ToString() => $"RegistryRequest {{ BaseUrl = {BaseUrl}, VatNumber = {VatNumber} }}";
}

/// <summary>One registry item (company, office, person...) as shown on the page.</summary>
public sealed record RegistryItem
{
    /// <summary>The item's UUID. It is the company ID.</summary>
    public string Id { get; init; } = "";
    public string? Description { get; init; }
    public string? Classifier { get; init; }
    public string? VatNumber { get; init; }
    public string? TaxId { get; init; }
    public string? TaxRegion { get; init; }
    public string? LegalForm { get; init; }
    public bool? Active { get; init; }
    public bool? Deleted { get; init; }
    public IReadOnlyList<string> Addresses { get; init; } = [];

    public static RegistryItem FromItemJson(JsonElement item)
    {
        var baseEl = AgyoHttp.Obj(item, "base");
        var identifier = AgyoHttp.Obj(baseEl, "identifier");
        var details = AgyoHttp.Obj(baseEl, "details");
        var status = AgyoHttp.Obj(baseEl, "status");
        var legalForm = AgyoHttp.Obj(details, "legalForm");

        var addresses = new List<string>();
        if (details.ValueKind == JsonValueKind.Object
            && details.TryGetProperty("addresses", out var list)
            && list.ValueKind == JsonValueKind.Array)
        {
            foreach (var address in list.EnumerateArray())
            {
                var full = AgyoHttp.Str(address, "fullAddress");
                if (!string.IsNullOrWhiteSpace(full)) addresses.Add(full);
            }
        }

        return new RegistryItem
        {
            Id = AgyoHttp.Str(baseEl, "id") ?? "",
            Description = AgyoHttp.Str(details, "description") ?? PersonName(details),
            Classifier = AgyoHttp.Str(details, "classifier"),
            VatNumber = AgyoHttp.Str(identifier, "vatNumber"),
            TaxId = AgyoHttp.Str(identifier, "taxId"),
            TaxRegion = AgyoHttp.Str(identifier, "taxRegion"),
            LegalForm = AgyoHttp.Str(legalForm, "description"),
            Active = AgyoHttp.Bool(status, "active"),
            Deleted = AgyoHttp.Bool(status, "deleted"),
            Addresses = addresses,
        };
    }

    private static string? PersonName(JsonElement details)
    {
        var first = AgyoHttp.Str(details, "firstName");
        var last = AgyoHttp.Str(details, "lastName");
        var name = string.Join(" ", new[] { first, last }.Where(s => !string.IsNullOrWhiteSpace(s)));
        return name.Length == 0 ? null : name;
    }
}

/// <summary>A successful lookup: the matching items plus the pretty-printed body and timing.</summary>
public sealed record RegistryResult(IReadOnlyList<RegistryItem> Items, long TotalItems, string PrettyJson,
    int HttpStatus, string RequestId, TimeSpan Duration);

/// <summary>Finds registry items by VAT number (GET /api/v3/items?identifier.vatNumber=...&amp;packageType=BASE).</summary>
public sealed class RegistryApiClient
{
    private readonly HttpClient _http;

    public RegistryApiClient(HttpClient http) => _http = http;

    public async Task<RegistryResult> FindByVatAsync(RegistryRequest request, CancellationToken ct = default)
    {
        using var message = BuildFind(request, out var requestId);

        var clock = Stopwatch.StartNew();
        using var response = await _http.SendAsync(message, ct);
        var body = await response.Content.ReadAsStringAsync(ct);
        clock.Stop();

        var status = (int)response.StatusCode;
        if (!response.IsSuccessStatusCode)
            throw AgyoHttp.ApiException(status, response.ReasonPhrase, body);

        using var doc = JsonDocument.Parse(body);
        var root = doc.RootElement;

        var items = new List<RegistryItem>();
        if (root.ValueKind == JsonValueKind.Object
            && root.TryGetProperty("items", out var list)
            && list.ValueKind == JsonValueKind.Array)
        {
            foreach (var wrapper in list.EnumerateArray())
            {
                var item = AgyoHttp.Obj(wrapper, "item");
                if (item.ValueKind == JsonValueKind.Object)
                    items.Add(RegistryItem.FromItemJson(item));
            }
        }

        var total = root.ValueKind == JsonValueKind.Object
                    && root.TryGetProperty("totalItems", out var t) && t.ValueKind == JsonValueKind.Number
            ? t.GetInt64()
            : items.Count;

        return new RegistryResult(items, total, AgyoHttp.Pretty(body), status, requestId, clock.Elapsed);
    }

    /// <summary>The curl command for <see cref="FindByVatAsync"/> with these inputs.</summary>
    public string FindByVatCurl(RegistryRequest request)
    {
        using var message = BuildFind(request, out _);
        return CurlFormatter.Format(message);
    }

    private static HttpRequestMessage BuildFind(RegistryRequest request, out string requestId)
    {
        ArgumentNullException.ThrowIfNull(request);

        var baseUri = AgyoHttp.ValidateBaseUrl(request.BaseUrl);
        if (string.IsNullOrWhiteSpace(request.AccessToken))
            throw new ArgumentException("Access token is required. Run a login test first.", nameof(request));
        if (string.IsNullOrWhiteSpace(request.VatNumber))
            throw new ArgumentException("Company VAT is required. Enter it on the Login test page.", nameof(request));

        var vat = request.VatNumber.Trim();
        var url = new Uri(baseUri,
            "/api/v3/items?identifier.taxRegion=" + Uri.EscapeDataString(request.TaxRegion.Trim())
            + "&identifier.vatNumber=" + Uri.EscapeDataString(vat)
            + "&packageType=BASE&pagination.pageNumber=0&pagination.itemsPerPage=100");
        requestId = Guid.NewGuid().ToString("N");

        var message = new HttpRequestMessage(HttpMethod.Get, url);
        message.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        message.Headers.Authorization = new AuthenticationHeaderValue("Bearer", request.AccessToken.Trim());
        message.Headers.TryAddWithoutValidation("User-Agent", request.UserAgent);
        message.Headers.TryAddWithoutValidation("X-App-Name", request.AppName);
        message.Headers.TryAddWithoutValidation("X-App-Version", request.AppVersion);
        message.Headers.TryAddWithoutValidation("X-Request-ID", requestId);
        message.Headers.TryAddWithoutValidation("X-Correlation-ID", Guid.NewGuid().ToString());
        return message;
    }
}
