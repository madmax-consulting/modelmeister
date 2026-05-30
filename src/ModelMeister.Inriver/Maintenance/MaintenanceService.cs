using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace ModelMeister.Inriver.Maintenance;

/// <summary>
/// Safe, idempotent environment maintenance actions exposed by inriver's <c>UtilityService</c>:
/// rebuilding the quick-search index (after a model change the search index can lag) and clearing the
/// image cache. Neither destroys data — they recompute derived state — but both touch the server, so
/// they run through the write path. The convenience of one click is the "save time" win over hunting
/// through the inriver admin UI.
/// </summary>
public sealed class MaintenanceService
{
    private readonly InriverClient _remoting;
    private readonly ILogger _log;

    public MaintenanceService(InriverClient remoting, ILogger<MaintenanceService>? log = null)
    {
        _remoting = remoting;
        _log = (ILogger?)log ?? NullLogger.Instance;
    }

    /// <summary>Rebuild the quick-search index so search reflects the current model and data.</summary>
    public async Task RebuildQuickSearchIndexAsync(CancellationToken ct = default)
    {
        _log.LogInformation("Rebuilding quick-search index");
        await _remoting.WriteAsync(m => { m.UtilityService.RebuildQuickSearchIndex(); return true; }, ct).ConfigureAwait(false);
    }

    /// <summary>Clear the rendered-image cache for the whole environment (thumbnails repopulate on demand).</summary>
    public async Task ClearImageCacheAsync(CancellationToken ct = default)
    {
        _log.LogInformation("Clearing image cache");
        await _remoting.WriteAsync(m => { m.UtilityService.ClearImageCache(); return true; }, ct).ConfigureAwait(false);
    }
}
