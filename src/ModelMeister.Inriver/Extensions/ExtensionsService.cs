using inRiver.Remoting.Connect;
using IriverConnectorState = inRiver.Remoting.Objects.ConnectorState;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using ModelMeister.Rest;

namespace ModelMeister.Inriver.Extensions;

/// <summary>
/// Wraps the Remoting <c>Connector</c> surface (and, where available, the REST <c>extensions</c>
/// endpoints) into a single coherent API for the UI's Extensions page and the CLI. inriver uses
/// "Connector" in older Remoting and "Extension" in modern docs — they refer to the same concept.
/// </summary>
/// <remarks>
/// The Remoting methods <c>GetAllConnectors</c>, <c>SetConnectorStarted</c>, etc. are marked
/// obsolete on the server in 8.21 but are still the only programmatic surface available. Use of
/// the REST equivalents (<c>/api/v1.0.0/extensions/{id}:start</c>) is preferred when a REST key
/// is configured for the environment.
/// </remarks>
#pragma warning disable CS0618 // obsolete connector API — still the only available surface
public sealed class ExtensionsService
{
    private readonly InriverClient _remoting;
    private readonly InriverRestClient? _rest;
    private readonly ILogger _log;

    /// <summary>
    /// Wraps a Remoting client; optionally also a REST client so modern <c>:start</c>/<c>:stop</c>
    /// endpoints take precedence when available.
    /// </summary>
    public ExtensionsService(InriverClient remoting, InriverRestClient? rest = null, ILogger<ExtensionsService>? log = null)
    {
        _remoting = remoting;
        _rest = rest;
        _log = (ILogger?)log ?? NullLogger.Instance;
    }

    /// <summary>Snapshot view of a single extension as returned by <see cref="List"/>.</summary>
    public sealed class ExtensionInfo
    {
        public string Id { get; set; } = "";
        public string? TypeName { get; set; }
        public bool IsStarted { get; set; }
        public DateTime? LastEventUtc { get; set; }
        public string? LastEventMessage { get; set; }
        public int RecentErrorCount { get; set; }
        public Dictionary<string, string> Settings { get; set; } = new(StringComparer.Ordinal);
    }

    /// <summary>One log entry from an extension.</summary>
    public sealed record ExtensionEvent(DateTime Utc, bool IsError, string Message, string? ConnectorId = null);

    /// <summary>One connector-state row (free-form per-extension checkpoint blob).</summary>
    public sealed record ExtensionStateRow(int Id, string ConnectorId, string Data, DateTime Created, DateTime Modified);

    // ---------------- Reads ----------------

    /// <summary>List all extensions (Connectors) in the environment.</summary>
    public IReadOnlyList<ExtensionInfo> List()
    {
        var connectors = _remoting.Read(m => m.UtilityService.GetAllConnectors() ?? []);
        var result = new List<ExtensionInfo>();
        foreach (var c in connectors)
        {
            var info = new ExtensionInfo
            {
                Id = c.Id,
                TypeName = c.TypeName,
                IsStarted = c.IsStarted,
                Settings = c.Settings is null
                    ? new Dictionary<string, string>(StringComparer.Ordinal)
                    : new Dictionary<string, string>(c.Settings, StringComparer.Ordinal),
            };
            try
            {
                var events = _remoting.Read(m => m.UtilityService.GetConnectorEvents(c.Id, 20) ?? []);
                var latest = events.FirstOrDefault();
                if (latest is not null)
                {
                    info.LastEventUtc = latest.EventTime;
                    info.LastEventMessage = latest.Message;
                    info.RecentErrorCount = events.Count(e => e.IsError);
                }
            }
            catch (Exception ex) { _log.LogWarning(ex, "Failed to fetch events for connector {Id}", c.Id); }
            result.Add(info);
        }
        return result;
    }

    /// <summary>Read recent events for one extension.</summary>
    public IReadOnlyList<ExtensionEvent> Events(string id, int max = 100)
    {
        var raw = _remoting.Read(m => m.UtilityService.GetConnectorEvents(id, max) ?? []);
        return raw.Select(e => new ExtensionEvent(e.EventTime, e.IsError, e.Message ?? "", e.ConnectorId)).ToList();
    }

    /// <summary>Read the latest events across every extension in the env. Used by the triage feed.</summary>
    public IReadOnlyList<ExtensionEvent> LatestEvents(int max = 200, bool onlyChannelEvents = false)
    {
        var raw = _remoting.Read(m => m.UtilityService.GetLatestConnectorEvents(max, onlyChannelEvents) ?? []);
        return raw.Select(e => new ExtensionEvent(e.EventTime, e.IsError, e.Message ?? "", e.ConnectorId)).ToList();
    }

    // ---------------- Lifecycle ----------------

    /// <summary>Start (resume) an extension by id.</summary>
    public async Task<bool> StartAsync(string id, CancellationToken ct = default)
    {
        if (_rest is not null && await TryRestAsync(() => _rest.StartExtensionAsync(id, ct)).ConfigureAwait(false)) return true;
        try
        {
            await _remoting.WriteAsync(m => m.UtilityService.SetConnectorStarted(id, true), ct).ConfigureAwait(false);
            return true;
        }
        catch (Exception ex) { _log.LogWarning(ex, "Start extension failed: {Id}", id); return false; }
    }

    /// <summary>Stop (pause) an extension by id.</summary>
    public async Task<bool> StopAsync(string id, CancellationToken ct = default)
    {
        if (_rest is not null && await TryRestAsync(() => _rest.StopExtensionAsync(id, ct)).ConfigureAwait(false)) return true;
        try
        {
            await _remoting.WriteAsync(m => m.UtilityService.SetConnectorStarted(id, false), ct).ConfigureAwait(false);
            return true;
        }
        catch (Exception ex) { _log.LogWarning(ex, "Stop extension failed: {Id}", id); return false; }
    }

    /// <summary>
    /// Trigger an extension run on demand. REST-only — the Remoting surface does not expose a
    /// run trigger. Returns <c>false</c> when no REST client is configured.
    /// </summary>
    public async Task<bool> RunAsync(string id, CancellationToken ct = default)
    {
        if (_rest is null) return false;
        return await TryRestAsync(() => _rest.RunExtensionAsync(id, ct)).ConfigureAwait(false);
    }

    /// <summary>Delete an extension by id.</summary>
    public async Task<bool> DeleteAsync(string id, CancellationToken ct = default)
    {
        try
        {
            return await _remoting.WriteAsync(m => m.UtilityService.DeleteConnector(id), ct).ConfigureAwait(false);
        }
        catch (Exception ex) { _log.LogWarning(ex, "Delete extension failed: {Id}", id); return false; }
    }

    // ---------------- Settings ----------------

    /// <summary>Set a single configuration setting on an extension.</summary>
    public async Task<bool> SetSettingAsync(string id, string key, string value, CancellationToken ct = default)
    {
        try
        {
            await _remoting.WriteAsync(m => m.UtilityService.SetConnectorSetting(id, key, value), ct).ConfigureAwait(false);
            return true;
        }
        catch (Exception ex) { _log.LogWarning(ex, "Set setting failed: {Id} {Key}", id, key); return false; }
    }

    /// <summary>Delete a configuration setting from an extension.</summary>
    public async Task<bool> DeleteSettingAsync(string id, string key, CancellationToken ct = default)
    {
        try
        {
            await _remoting.WriteAsync(m => m.UtilityService.DeleteConnectorSetting(id, key), ct).ConfigureAwait(false);
            return true;
        }
        catch (Exception ex) { _log.LogWarning(ex, "Delete setting failed: {Id} {Key}", id, key); return false; }
    }

    // ---------------- Connector states (extension cursor/checkpoint blobs) ----------------

    /// <summary>Read all connector-state rows across every extension. Diagnostic tool.</summary>
    public IReadOnlyList<ExtensionStateRow> ListAllStates()
    {
        var raw = _remoting.Read(m => m.UtilityService.GetAllConnectorStates() ?? []);
        return raw.Select(ToRow).ToList();
    }

    /// <summary>Read all connector-state rows for one extension.</summary>
    public IReadOnlyList<ExtensionStateRow> ListStates(string connectorId)
    {
        var raw = _remoting.Read(m => m.UtilityService.GetAllConnectorStatesForConnector(connectorId) ?? []);
        return raw.Select(ToRow).ToList();
    }

    /// <summary>Add a connector-state row; returns the persisted row (with server-assigned id).</summary>
    public async Task<ExtensionStateRow?> AddStateAsync(string connectorId, string data, CancellationToken ct = default)
    {
        try
        {
            var stateIn = new IriverConnectorState { ConnectorId = connectorId, Data = data };
            var saved = await _remoting.WriteAsync(m => m.UtilityService.AddConnectorState(stateIn), ct).ConfigureAwait(false);
            return saved is null ? null : ToRow(saved);
        }
        catch (Exception ex) { _log.LogWarning(ex, "Add connector state failed: {Id}", connectorId); return null; }
    }

    /// <summary>Update an existing connector-state row's data.</summary>
    public async Task<bool> UpdateStateAsync(int id, string connectorId, string data, CancellationToken ct = default)
    {
        try
        {
            var stateIn = new IriverConnectorState { Id = id, ConnectorId = connectorId, Data = data };
            var saved = await _remoting.WriteAsync(m => m.UtilityService.UpdateConnectorState(stateIn), ct).ConfigureAwait(false);
            return saved is not null;
        }
        catch (Exception ex) { _log.LogWarning(ex, "Update connector state failed: id={StateId}", id); return false; }
    }

    /// <summary>Delete a single connector-state row by id.</summary>
    public async Task<bool> DeleteStateAsync(int id, CancellationToken ct = default)
    {
        try { return await _remoting.WriteAsync(m => m.UtilityService.DeleteConnectorState(id), ct).ConfigureAwait(false); }
        catch (Exception ex) { _log.LogWarning(ex, "Delete connector state failed: id={StateId}", id); return false; }
    }

    /// <summary>Delete every connector-state row across the environment. Destructive.</summary>
    public async Task<bool> DeleteAllStatesAsync(CancellationToken ct = default)
    {
        try { return await _remoting.WriteAsync(m => m.UtilityService.DeleteAllConnectorStates(), ct).ConfigureAwait(false); }
        catch (Exception ex) { _log.LogWarning(ex, "Delete all connector states failed"); return false; }
    }

    private static ExtensionStateRow ToRow(IriverConnectorState s)
        => new(s.Id, s.ConnectorId ?? "", s.Data ?? "", s.Created, s.Modified);

    // ---------------- helpers ----------------

    /// <summary>Run a REST call and swallow any exception as <c>false</c>, so callers can fall back to Remoting.</summary>
    private static async Task<bool> TryRestAsync(Func<Task<bool>> call)
    {
        try { return await call().ConfigureAwait(false); }
        catch { return false; }
    }
}
#pragma warning restore CS0618
