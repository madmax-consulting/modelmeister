using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace ModelMeister.Rest;

/// <summary>
/// Thin REST client over the inriver REST API. Used for the features Remoting does not expose
/// (user creation, modern extension management endpoints). Sends <c>X-inRiver-APIKey</c> on every
/// request.
/// </summary>
public sealed class InriverRestClient : IDisposable
{
    /// <summary>Base URL of the REST endpoint, e.g. <c>https://apieuw.productmarketingcloud.com</c>.</summary>
    public string BaseUrl { get; }

    private readonly HttpClient _http;
    private readonly bool _ownsHttp;

    /// <summary>
    /// Creates a client targeting <paramref name="baseUrl"/> with <paramref name="apiKey"/> as the
    /// X-inRiver-APIKey header. If <paramref name="http"/> is supplied the caller owns it and the
    /// client will not dispose it; otherwise the client constructs and owns its own
    /// <see cref="HttpClient"/>.
    /// </summary>
    public InriverRestClient(string baseUrl, string apiKey, HttpClient? http = null)
    {
        if (string.IsNullOrWhiteSpace(baseUrl)) throw new ArgumentException("Base URL is required", nameof(baseUrl));
        if (string.IsNullOrWhiteSpace(apiKey)) throw new ArgumentException("API key is required", nameof(apiKey));
        BaseUrl = baseUrl.TrimEnd('/');
        _http = http ?? new HttpClient();
        _ownsHttp = http is null;
        _http.DefaultRequestHeaders.Accept.Clear();
        _http.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        // The REST API accepts the key under X-inRiver-APIKey (the official header).
        _http.DefaultRequestHeaders.Remove("X-inRiver-APIKey");
        _http.DefaultRequestHeaders.Add("X-inRiver-APIKey", apiKey);
        _http.Timeout = TimeSpan.FromSeconds(60);
    }

    public void Dispose()
    {
        if (_ownsHttp) _http.Dispose();
    }

    /// <summary>Quick liveness check against /api/v1.0.0/channels (smallest known GET).</summary>
    public async Task<bool> PingAsync(CancellationToken ct = default)
    {
        try
        {
            using var resp = await _http.GetAsync(BaseUrl + "/api/v1.0.0/channels", ct).ConfigureAwait(false);
            return resp.IsSuccessStatusCode;
        }
        catch
        {
            return false;
        }
    }

    // ----------- USERS

    /// <summary>
    /// Create a user via the REST admin API. Returns the API's response on success. Throws
    /// <see cref="InriverRestException"/> on failure with full status + body for diagnostics.
    /// </summary>
    public async Task<UserCreated> CreateUserAsync(UserCreate request, CancellationToken ct = default)
    {
        using var resp = await _http.PostAsJsonAsync(BaseUrl + "/api/v1.0.0/users", request, JsonOptions, ct).ConfigureAwait(false);
        return await ReadOrThrowAsync<UserCreated>(resp, ct).ConfigureAwait(false);
    }

    /// <summary>List all users.</summary>
    public async Task<IReadOnlyList<UserSummary>> ListUsersAsync(CancellationToken ct = default)
    {
        var users = await _http.GetFromJsonAsync<UserSummary[]>(
            BaseUrl + "/api/v1.0.0/users", JsonOptions, ct).ConfigureAwait(false);
        return users ?? [];
    }

    // ----------- EXTENSIONS

    /// <summary>List inriver extensions (modern Connector concept; older envs use Remoting).</summary>
    public async Task<IReadOnlyList<ExtensionSummary>> ListExtensionsAsync(CancellationToken ct = default)
    {
        using var resp = await _http.GetAsync(BaseUrl + "/api/v1.0.0/extensions", ct).ConfigureAwait(false);
        if (resp.StatusCode == HttpStatusCode.NotFound) return [];
        if (!resp.IsSuccessStatusCode)
            throw new InriverRestException(resp.StatusCode,
                $"Failed to list extensions: {await resp.Content.ReadAsStringAsync(ct).ConfigureAwait(false)}");
        return await resp.Content.ReadFromJsonAsync<ExtensionSummary[]>(JsonOptions, ct).ConfigureAwait(false)
            ?? [];
    }

    /// <summary>Start/resume an extension by id.</summary>
    public async Task<bool> StartExtensionAsync(string id, CancellationToken ct = default)
        => await PostNoContentAsync($"/api/v1.0.0/extensions/{Uri.EscapeDataString(id)}:start", ct).ConfigureAwait(false);

    /// <summary>Stop/pause an extension by id.</summary>
    public async Task<bool> StopExtensionAsync(string id, CancellationToken ct = default)
        => await PostNoContentAsync($"/api/v1.0.0/extensions/{Uri.EscapeDataString(id)}:stop", ct).ConfigureAwait(false);

    /// <summary>Trigger an extension run.</summary>
    public async Task<bool> RunExtensionAsync(string id, CancellationToken ct = default)
        => await PostNoContentAsync($"/api/v1.0.0/extensions/{Uri.EscapeDataString(id)}:run", ct).ConfigureAwait(false);

    private async Task<bool> PostNoContentAsync(string path, CancellationToken ct)
    {
        using var resp = await _http.PostAsync(BaseUrl + path, content: null, ct).ConfigureAwait(false);
        return resp.IsSuccessStatusCode;
    }

    // ----------- helpers

    private static async Task<T> ReadOrThrowAsync<T>(HttpResponseMessage resp, CancellationToken ct)
    {
        if (!resp.IsSuccessStatusCode)
        {
            var body = await resp.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
            throw new InriverRestException(resp.StatusCode, body);
        }
        var data = await resp.Content.ReadFromJsonAsync<T>(JsonOptions, ct).ConfigureAwait(false);
        return data ?? throw new InriverRestException(resp.StatusCode, "Empty response body where one was expected.");
    }

    public static JsonSerializerOptions JsonOptions { get; } = new()
    {
        PropertyNameCaseInsensitive = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };
}

/// <summary>
/// Raised when an inriver REST call returns a non-success status. <see cref="Status"/> exposes the
/// HTTP status and the <see cref="Exception.Message"/> carries the response body.
/// </summary>
public sealed class InriverRestException : Exception
{
    public HttpStatusCode Status { get; }
    public InriverRestException(HttpStatusCode status, string message) : base($"{(int)status} {status}: {message}")
        => Status = status;
}

// ---------- DTOs (only the fields we use; other properties are tolerated by PropertyNameCaseInsensitive)

/// <summary>Request body for POST /api/v1.0.0/users.</summary>
public sealed class UserCreate
{
    public string Username { get; set; } = "";
    public string? Email { get; set; }
    public string? FirstName { get; set; }
    public string? LastName { get; set; }
    public string? Company { get; set; }
    public string? Language { get; set; }

    /// <summary>
    /// Segment-role memberships. The create endpoint rejects the request when this is absent, so it
    /// is always serialized — even as an empty array. Plain role membership is assigned afterwards
    /// via Remoting (see <c>UserProvisioning.AssignRolesAsync</c>), so this stays empty here.
    /// </summary>
    public List<string> SegmentRoles { get; set; } = [];
}

/// <summary>Response body returned after a successful user creation; carries the newly-issued API key.</summary>
public sealed class UserCreated
{
    public int Id { get; set; }
    public string? Username { get; set; }
    public string? ApiKey { get; set; }
}

/// <summary>Lightweight user record returned by GET /api/v1.0.0/users.</summary>
public sealed class UserSummary
{
    public int Id { get; set; }
    public string? Username { get; set; }
    public string? Email { get; set; }
    public string? FirstName { get; set; }
    public string? LastName { get; set; }
    public string? Company { get; set; }
    public List<string>? Roles { get; set; }
    public bool Active { get; set; } = true;
}

/// <summary>Single inriver extension as returned by GET /api/v1.0.0/extensions.</summary>
public sealed class ExtensionSummary
{
    public string Id { get; set; } = "";
    public string? Name { get; set; }
    public string? Type { get; set; }
    public string? Status { get; set; }
    public bool Enabled { get; set; }
    public bool Paused { get; set; }
    public DateTime? LastRunUtc { get; set; }
    public string? LastResult { get; set; }
}
