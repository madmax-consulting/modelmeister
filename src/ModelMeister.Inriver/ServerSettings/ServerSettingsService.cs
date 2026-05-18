using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace ModelMeister.Inriver.ServerSettings;

/// <summary>
/// Wraps the inriver Remoting <c>UtilityService</c> server-settings surface
/// (<c>GetAllServerSettings</c>, <c>GetServerSetting(s)</c>, <c>SetServerSetting</c>,
/// <c>DeleteServerSetting</c>) into a single facade the UI and CLI can call.
/// </summary>
/// <remarks>
/// Server settings are a flat <c>string → string</c> dictionary scoped to the environment. They
/// configure everything from inbox notifications to integration endpoints. There is no REST
/// equivalent in 8.21, so this service is Remoting-only.
/// </remarks>
public sealed class ServerSettingsService
{
    private readonly InriverClient _remoting;
    private readonly ILogger _log;

    public ServerSettingsService(InriverClient remoting, ILogger<ServerSettingsService>? log = null)
    {
        _remoting = remoting;
        _log = (ILogger?)log ?? NullLogger.Instance;
    }

    /// <summary>Read every server setting in the connected env as an ordinal-keyed dictionary.</summary>
    public IReadOnlyDictionary<string, string> GetAll()
    {
        var raw = _remoting.Read(m => m.UtilityService.GetAllServerSettings() ?? new Dictionary<string, string>());
        return new Dictionary<string, string>(raw, StringComparer.Ordinal);
    }

    /// <summary>Read a single setting by key, or <c>null</c> if not set.</summary>
    public string? Get(string key)
        => _remoting.Read(m => m.UtilityService.GetServerSetting(key));

    /// <summary>Read a subset of settings by key.</summary>
    public IReadOnlyDictionary<string, string> GetMany(IEnumerable<string> keys)
    {
        var list = keys.ToList();
        var raw = _remoting.Read(m => m.UtilityService.GetServerSettings(list) ?? new Dictionary<string, string>());
        return new Dictionary<string, string>(raw, StringComparer.Ordinal);
    }

    /// <summary>Write a single setting.</summary>
    public async Task<bool> SetAsync(string key, string value, CancellationToken ct = default)
    {
        try
        {
            return await _remoting.WriteAsync(m => m.UtilityService.SetServerSetting(key, value), ct).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            _log.LogWarning(ex, "Set server setting failed: {Key}", key);
            return false;
        }
    }

    /// <summary>Delete a single setting.</summary>
    public async Task<bool> DeleteAsync(string key, CancellationToken ct = default)
    {
        try
        {
            return await _remoting.WriteAsync(m => m.UtilityService.DeleteServerSetting(key), ct).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            _log.LogWarning(ex, "Delete server setting failed: {Key}", key);
            return false;
        }
    }

    /// <summary>
    /// Apply a batch of writes and deletes, reporting per-key success. Used by the UI's
    /// promote/transfer flow to push a curated set of differences in one shot.
    /// </summary>
    public async Task<BulkResult> BulkApplyAsync(
        IEnumerable<KeyValuePair<string, string?>> entries,
        CancellationToken ct = default)
    {
        var ok = new List<string>();
        var failed = new List<string>();
        foreach (var kvp in entries)
        {
            ct.ThrowIfCancellationRequested();
            var success = kvp.Value is null
                ? await DeleteAsync(kvp.Key, ct).ConfigureAwait(false)
                : await SetAsync(kvp.Key, kvp.Value, ct).ConfigureAwait(false);
            if (success) ok.Add(kvp.Key); else failed.Add(kvp.Key);
        }
        return new BulkResult(ok, failed);
    }

    /// <summary>Outcome of a <see cref="BulkApplyAsync"/> call.</summary>
    public sealed record BulkResult(IReadOnlyList<string> Applied, IReadOnlyList<string> Failed)
    {
        public int Total => Applied.Count + Failed.Count;
        public bool AllOk => Failed.Count == 0;
    }
}
