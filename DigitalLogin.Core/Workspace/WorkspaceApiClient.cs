using System.Diagnostics;
using System.Net.Http.Headers;
using System.Text.Json;
using DigitalLogin.Http;

namespace DigitalLogin.Workspace;

/// <summary>Everything one workspace lookup needs. The access token is held in memory only.</summary>
public sealed record WorkspaceRequest
{
    /// <summary>Base URL of the Workspace Read API, without a path.</summary>
    public string BaseUrl { get; init; } = "https://workspace-read.agyo.io";

    /// <summary>Bearer token, e.g. the access token from a successful login test.</summary>
    public string AccessToken { get; init; } = "";

    /// <summary>Workspace to look up. Also sent as X-Workspace-ID, which the gateway requires.</summary>
    public string WorkspaceId { get; init; } = "";

    /// <summary>Local Agyo user making the request, sent as X-User-ID.</summary>
    public string UserId { get; init; } = "";

    public string UserAgent { get; init; } = "DigitalLogin/1.0";
    public string AppName { get; init; } = "DigitalLogin";
    public string AppVersion { get; init; } = "1.0.0";

    /// <summary>Keeps the token out of logs and debugger output.</summary>
    public override string ToString() =>
        $"WorkspaceRequest {{ BaseUrl = {BaseUrl}, WorkspaceId = {WorkspaceId}, UserId = {UserId} }}";
}

/// <summary>The fields of a workspace the app displays. Dates stay as the service sent them.</summary>
public sealed record WorkspaceInfo
{
    public string Id { get; init; } = "";
    public string? ItemId { get; init; }
    public string? OwnerId { get; init; }
    public string? Classifier { get; init; }
    public string? Status { get; init; }
    public bool? Deleted { get; init; }
    public string? TaxIdentifier { get; init; }
    public string? CountryCode { get; init; }
    public string? BaseCurrency { get; init; }
    public string? IntermediaryId { get; init; }
    public string? BusinessLegalForm { get; init; }
    public string? BusinessLegalNature { get; init; }
    public string? EstablishmentDate { get; init; }
    public string? ExpiryDate { get; init; }
    public string? EndDate { get; init; }
    public string? LiquidationDate { get; init; }
    public string? Revision { get; init; }

    public static WorkspaceInfo FromJson(JsonElement root)
    {
        if (root.ValueKind != JsonValueKind.Object)
            throw new InvalidOperationException("Workspace response is not a JSON object.");

        return new WorkspaceInfo
        {
            Id = AgyoHttp.Str(root, "id") ?? "",
            ItemId = AgyoHttp.Str(root, "itemId"),
            OwnerId = AgyoHttp.Str(root, "ownerId"),
            Classifier = AgyoHttp.Str(root, "classifier"),
            Status = AgyoHttp.Str(root, "status"),
            Deleted = AgyoHttp.Bool(root, "deleted"),
            TaxIdentifier = AgyoHttp.Str(root, "taxIdentifier"),
            CountryCode = AgyoHttp.Str(root, "countryCode"),
            BaseCurrency = AgyoHttp.Str(root, "baseCurrency"),
            IntermediaryId = AgyoHttp.Str(root, "intermediaryId"),
            BusinessLegalForm = AgyoHttp.Str(root, "businessLegalForm"),
            BusinessLegalNature = AgyoHttp.Str(root, "businessLegalNature"),
            EstablishmentDate = AgyoHttp.Str(root, "establishmentDate"),
            ExpiryDate = AgyoHttp.Str(root, "expiryDate"),
            EndDate = AgyoHttp.Str(root, "endDate"),
            LiquidationDate = AgyoHttp.Str(root, "liquidationDate"),
            Revision = AgyoHttp.Str(root, "revision"),
        };
    }
}

/// <summary>A successful lookup: the parsed fields plus the pretty-printed body and timing.</summary>
public sealed record WorkspaceResult(WorkspaceInfo Workspace, string PrettyJson, int HttpStatus, string RequestId,
    TimeSpan Duration);

/// <summary>Everything a "my workspaces" lookup needs. The access token is held in memory only.</summary>
public sealed record MyWorkspacesRequest
{
    public string BaseUrl { get; init; } = "https://workspace-read.agyo.io";
    public string AccessToken { get; init; } = "";

    /// <summary>VAT number (partita IVA) to match against each workspace's vatIdentifier.</summary>
    public string VatNumber { get; init; } = "";

    /// <summary>Local Agyo user making the request, sent as X-User-ID.</summary>
    public string UserId { get; init; } = "";

    public string UserAgent { get; init; } = "DigitalLogin/1.0";
    public string AppName { get; init; } = "DigitalLogin";
    public string AppVersion { get; init; } = "1.0.0";

    public override string ToString() =>
        $"MyWorkspacesRequest {{ BaseUrl = {BaseUrl}, VatNumber = {VatNumber}, UserId = {UserId} }}";
}

/// <summary>One workspace as listed by /api/v2/my-workspaces.</summary>
public sealed record WorkspaceSummary
{
    public string Id { get; init; } = "";
    public string? ItemId { get; init; }
    public string? OwnerId { get; init; }
    public string? VatIdentifier { get; init; }
    public string? TaxIdentifier { get; init; }
    public string? DisplayName { get; init; }
    public string? Classifier { get; init; }
    public string? Status { get; init; }

    public static WorkspaceSummary FromJson(JsonElement el) => new()
    {
        Id = AgyoHttp.Str(el, "id") ?? "",
        ItemId = AgyoHttp.Str(el, "itemId"),
        OwnerId = AgyoHttp.Str(el, "ownerId"),
        VatIdentifier = AgyoHttp.Str(el, "vatIdentifier"),
        TaxIdentifier = AgyoHttp.Str(el, "taxIdentifier"),
        DisplayName = AgyoHttp.Str(el, "displayName"),
        Classifier = AgyoHttp.Str(el, "classifier"),
        Status = AgyoHttp.Str(el, "status"),
    };
}

/// <summary>The workspaces whose VAT matched, plus the pretty-printed body and timing.</summary>
public sealed record MyWorkspacesResult(IReadOnlyList<WorkspaceSummary> Matches, string PrettyJson, int HttpStatus,
    string RequestId, TimeSpan Duration);

/// <summary>Reads workspaces from the Workspace Read API.</summary>
public sealed class WorkspaceApiClient
{
    private readonly HttpClient _http;

    public WorkspaceApiClient(HttpClient http) => _http = http;

    /// <summary>GET /api/v2/workspaces/{id}</summary>
    public async Task<WorkspaceResult> GetWorkspaceAsync(WorkspaceRequest request, CancellationToken ct = default)
    {
        using var message = BuildGetWorkspace(request, out var requestId);

        var clock = Stopwatch.StartNew();
        using var response = await _http.SendAsync(message, ct);
        var body = await response.Content.ReadAsStringAsync(ct);
        clock.Stop();

        var status = (int)response.StatusCode;
        if (!response.IsSuccessStatusCode)
            throw AgyoHttp.ApiException(status, response.ReasonPhrase, body);

        using var doc = JsonDocument.Parse(body);
        return new WorkspaceResult(WorkspaceInfo.FromJson(doc.RootElement), AgyoHttp.Pretty(body), status, requestId,
            clock.Elapsed);
    }

    /// <summary>The curl command for <see cref="GetWorkspaceAsync"/> with these inputs.</summary>
    public string GetWorkspaceCurl(WorkspaceRequest request)
    {
        using var message = BuildGetWorkspace(request, out _);
        return CurlFormatter.Format(message);
    }

    /// <summary>
    /// Lists the workspaces of the caller (GET /api/v2/my-workspaces) and returns the ones whose VAT matches.
    /// The VAT comparison ignores surrounding spaces and case; no match gives an empty list.
    /// </summary>
    public async Task<MyWorkspacesResult> FindByVatAsync(MyWorkspacesRequest request, CancellationToken ct = default)
    {
        using var message = BuildMyWorkspaces(request, out var requestId);

        var clock = Stopwatch.StartNew();
        using var response = await _http.SendAsync(message, ct);
        var body = await response.Content.ReadAsStringAsync(ct);
        clock.Stop();

        var status = (int)response.StatusCode;
        if (!response.IsSuccessStatusCode)
            throw AgyoHttp.ApiException(status, response.ReasonPhrase, body);

        var vat = request.VatNumber.Trim();
        var matches = new List<WorkspaceSummary>();
        using (var doc = JsonDocument.Parse(body))
        {
            if (doc.RootElement.ValueKind == JsonValueKind.Array)
            {
                foreach (var el in doc.RootElement.EnumerateArray())
                {
                    var summary = WorkspaceSummary.FromJson(el);
                    if (string.Equals(summary.VatIdentifier?.Trim(), vat, StringComparison.OrdinalIgnoreCase))
                        matches.Add(summary);
                }
            }
        }

        return new MyWorkspacesResult(matches, AgyoHttp.Pretty(body), status, requestId, clock.Elapsed);
    }

    /// <summary>The curl command for <see cref="FindByVatAsync"/> with these inputs.</summary>
    public string FindByVatCurl(MyWorkspacesRequest request)
    {
        using var message = BuildMyWorkspaces(request, out _);
        return CurlFormatter.Format(message);
    }

    private static HttpRequestMessage BuildGetWorkspace(WorkspaceRequest request, out string requestId)
    {
        ArgumentNullException.ThrowIfNull(request);

        var baseUri = AgyoHttp.ValidateBaseUrl(request.BaseUrl);
        if (string.IsNullOrWhiteSpace(request.AccessToken))
            throw new ArgumentException("Access token is required. Run a login test first.", nameof(request));
        if (string.IsNullOrWhiteSpace(request.WorkspaceId))
            throw new ArgumentException("Workspace ID is required.", nameof(request));
        if (string.IsNullOrWhiteSpace(request.UserId))
            throw new ArgumentException("User ID is required.", nameof(request));

        var workspaceId = request.WorkspaceId.Trim();
        var url = new Uri(baseUri, "/api/v2/workspaces/" + Uri.EscapeDataString(workspaceId));
        requestId = Guid.NewGuid().ToString("N");

        var message = new HttpRequestMessage(HttpMethod.Get, url);
        message.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        message.Headers.Authorization = new AuthenticationHeaderValue("Bearer", request.AccessToken.Trim());
        message.Headers.TryAddWithoutValidation("User-Agent", request.UserAgent);
        message.Headers.TryAddWithoutValidation("X-User-ID", request.UserId.Trim());
        message.Headers.TryAddWithoutValidation("X-App-Name", request.AppName);
        message.Headers.TryAddWithoutValidation("X-App-Version", request.AppVersion);
        message.Headers.TryAddWithoutValidation("X-Request-ID", requestId);
        message.Headers.TryAddWithoutValidation("X-Correlation-ID", Guid.NewGuid().ToString());
        message.Headers.TryAddWithoutValidation("X-Workspace-ID", workspaceId);
        return message;
    }

    private static HttpRequestMessage BuildMyWorkspaces(MyWorkspacesRequest request, out string requestId)
    {
        ArgumentNullException.ThrowIfNull(request);

        var baseUri = AgyoHttp.ValidateBaseUrl(request.BaseUrl);
        if (string.IsNullOrWhiteSpace(request.AccessToken))
            throw new ArgumentException("Access token is required. Run a login test first.", nameof(request));
        if (string.IsNullOrWhiteSpace(request.VatNumber))
            throw new ArgumentException("Company VAT is required. Enter it on the Login test page.", nameof(request));
        if (string.IsNullOrWhiteSpace(request.UserId))
            throw new ArgumentException("User ID is required.", nameof(request));

        var url = new Uri(baseUri, "/api/v2/my-workspaces");
        requestId = Guid.NewGuid().ToString("N");

        var message = new HttpRequestMessage(HttpMethod.Get, url);
        message.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        message.Headers.Authorization = new AuthenticationHeaderValue("Bearer", request.AccessToken.Trim());
        message.Headers.TryAddWithoutValidation("User-Agent", request.UserAgent);
        message.Headers.TryAddWithoutValidation("X-User-ID", request.UserId.Trim());
        message.Headers.TryAddWithoutValidation("X-App-Name", request.AppName);
        message.Headers.TryAddWithoutValidation("X-App-Version", request.AppVersion);
        message.Headers.TryAddWithoutValidation("X-Request-ID", requestId);
        message.Headers.TryAddWithoutValidation("X-Correlation-ID", Guid.NewGuid().ToString());
        return message;
    }
}
