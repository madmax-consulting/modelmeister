using IriverEntityTypeStatistics = inRiver.Remoting.Objects.EntityTypeStatistics;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace ModelMeister.Inriver.Statistics;

/// <summary>
/// Per-entity-type instance counts captured from inriver's <c>GetAllEntityTypeStatistics</c>.
/// This is <b>volatile runtime data</b> — how many products/skus/etc. currently exist — and is
/// deliberately kept out of <see cref="Snapshot.LiveModel"/> (which is the model definition and must
/// stay diff-stable). Its job is to give the operator blast-radius context: "this field lives on an
/// entity type with 48,231 instances" is the difference between a safe edit and a data-loss incident.
/// </summary>
public sealed record EntityTypeStat(
    string EntityTypeId,
    string Name,
    int Total,
    int NewLastWeek,
    int UpdatedLastWeek);

/// <summary>A captured set of <see cref="EntityTypeStat"/> with a by-id lookup.</summary>
public sealed class EntityStatistics
{
    /// <summary>Stats per entity type, ordered by <see cref="EntityTypeStat.Total"/> descending.</summary>
    public IReadOnlyList<EntityTypeStat> Types { get; init; } = [];

    /// <summary>UTC instant the counts were read. Counts drift constantly; this dates them.</summary>
    public DateTime CapturedUtc { get; init; }

    private Dictionary<string, EntityTypeStat>? _byId;

    /// <summary>Stat for one entity type id (case-insensitive), or <c>null</c> when not reported.</summary>
    public EntityTypeStat? ForType(string entityTypeId)
    {
        _byId ??= Types
            .GroupBy(t => t.EntityTypeId, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key, g => g.First(), StringComparer.OrdinalIgnoreCase);
        return _byId.GetValueOrDefault(entityTypeId);
    }

    /// <summary>Instance count for one entity type id, or <c>0</c> when not reported.</summary>
    public int CountFor(string entityTypeId) => ForType(entityTypeId)?.Total ?? 0;

    /// <summary>Total instances across every reported entity type.</summary>
    public int TotalEntities => Types.Sum(t => t.Total);

    /// <summary>An empty stats set — used before a capture or when statistics are unavailable.</summary>
    public static EntityStatistics Empty { get; } = new();
}

/// <summary>
/// Reads inriver's entity-type instance statistics. Pure read; cheap; safe to call repeatedly.
/// Separate from <see cref="Snapshot.InriverSnapshot"/> because the counts are volatile and must not
/// pollute the model snapshot used for diffing.
/// </summary>
public sealed class EntityStatisticsService
{
    private readonly InriverClient _remoting;
    private readonly ILogger _log;

    public EntityStatisticsService(InriverClient remoting, ILogger<EntityStatisticsService>? log = null)
    {
        _remoting = remoting;
        _log = (ILogger?)log ?? NullLogger.Instance;
    }

    /// <summary>Capture current per-entity-type instance counts.</summary>
    public EntityStatistics Capture()
    {
        var raw = _remoting.Read(m => m.ModelService.GetAllEntityTypeStatistics() ?? new List<IriverEntityTypeStatistics>());
        _log.LogInformation("Captured statistics for {Count} entity type(s)", raw.Count);
        return new EntityStatistics
        {
            CapturedUtc = DateTime.UtcNow,
            Types = raw
                .Where(s => !string.IsNullOrEmpty(s.EntityTypeId))
                .Select(Map)
                .OrderByDescending(t => t.Total)
                .ThenBy(t => t.EntityTypeId, StringComparer.OrdinalIgnoreCase)
                .ToList(),
        };
    }

    private static EntityTypeStat Map(IriverEntityTypeStatistics s) => new(
        EntityTypeId: s.EntityTypeId ?? string.Empty,
        Name: string.IsNullOrWhiteSpace(s.Name) ? s.EntityTypeId ?? string.Empty : s.Name,
        Total: s.Total,
        NewLastWeek: s.NewLastWeek,
        UpdatedLastWeek: s.UpdatedLastWeek);
}
