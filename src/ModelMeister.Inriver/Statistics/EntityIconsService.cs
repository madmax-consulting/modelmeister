using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace ModelMeister.Inriver.Statistics;

/// <summary>
/// Reads inriver's per-entity-type icons (<c>GetAllEntityIcons</c>) as raw image bytes keyed by entity
/// type id. Pure read. Lets the UI render the same glyphs the inriver workbench shows next to each
/// entity type, so the model browser feels native rather than abstract. Icons are optional decoration —
/// a missing or unreadable icon is simply absent, never an error.
/// </summary>
public sealed class EntityIconsService
{
    private readonly InriverClient _remoting;
    private readonly ILogger _log;

    public EntityIconsService(InriverClient remoting, ILogger<EntityIconsService>? log = null)
    {
        _remoting = remoting;
        _log = (ILogger?)log ?? NullLogger.Instance;
    }

    /// <summary>Capture every entity-type icon as raw bytes, keyed by entity type id (case-insensitive).</summary>
    public IReadOnlyDictionary<string, byte[]> Capture()
    {
        var raw = _remoting.Read(m => m.UtilityService.GetAllEntityIcons());
        var result = new Dictionary<string, byte[]>(StringComparer.OrdinalIgnoreCase);
        if (raw is null) return result;
        foreach (var kvp in raw)
        {
            if (!string.IsNullOrEmpty(kvp.Key) && kvp.Value is { Length: > 0 })
                result[kvp.Key] = kvp.Value;
        }
        _log.LogInformation("Captured {Count} entity icon(s)", result.Count);
        return result;
    }
}
