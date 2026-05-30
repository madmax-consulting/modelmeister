using IriverChanges = inRiver.Remoting.Objects.EnvironmentLatestChangesSince;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace ModelMeister.Inriver.Snapshot;

/// <summary>
/// A drift report: what (if anything) changed in an environment's model since a given instant.
/// Produced from inriver's <c>GetEnvironmentLatestChanges</c>. The point is trust — a captured
/// snapshot is only a safe basis for an apply while the live env hasn't moved underneath it.
/// </summary>
public sealed record EnvironmentChanges(
    bool AnyChanges,
    bool ModelReloaded,
    IReadOnlyList<string> ChangedAreas,
    DateTime SinceUtc,
    DateTime CheckedUtc)
{
    /// <summary>A "nothing changed" report for the window ending now.</summary>
    public static EnvironmentChanges None(DateTime sinceUtc, DateTime checkedUtc) =>
        new(false, false, [], sinceUtc, checkedUtc);

    /// <summary>Operator-facing one-liner, e.g. "Entity types, CVL values and Roles changed".</summary>
    public string Summary()
    {
        if (!AnyChanges) return "No model changes since the snapshot was captured.";
        if (ChangedAreas.Count == 0)
            return "The environment's model changed since the snapshot was captured.";
        var areas = ChangedAreas.Count switch
        {
            1 => ChangedAreas[0],
            2 => $"{ChangedAreas[0]} and {ChangedAreas[1]}",
            _ => string.Join(", ", ChangedAreas.Take(ChangedAreas.Count - 1)) + " and " + ChangedAreas[^1],
        };
        return $"{areas} changed since the snapshot was captured.";
    }
}

/// <summary>
/// Reads inriver's "what changed since" feed. Pure read; cheap. Used to detect that a captured
/// snapshot has gone stale (a teammate edited the model) before the operator applies against it.
/// </summary>
public sealed class EnvironmentChangesService
{
    private readonly InriverClient _remoting;
    private readonly ILogger _log;

    public EnvironmentChangesService(InriverClient remoting, ILogger<EnvironmentChangesService>? log = null)
    {
        _remoting = remoting;
        _log = (ILogger?)log ?? NullLogger.Instance;
    }

    /// <summary>Report what changed in the connected env's model since <paramref name="sinceUtc"/>.</summary>
    public EnvironmentChanges Since(DateTime sinceUtc)
    {
        var raw = _remoting.Read(m => m.ModelService.GetEnvironmentLatestChanges(sinceUtc));
        var now = DateTime.UtcNow;
        if (raw is null) return EnvironmentChanges.None(sinceUtc, now);
        return FromFlags(ToFlags(raw), sinceUtc, now);
    }

    /// <summary>
    /// Settable mirror of inriver's read-only change flags. inriver's DTO exposes only getters, so the
    /// projection works against this record to stay unit-testable; <see cref="ToFlags"/> adapts the DTO.
    /// </summary>
    public sealed record ChangeFlags
    {
        public bool AnyChanges { get; init; }
        public bool ModelReloaded { get; init; }
        public bool EntityTypes { get; init; }
        public bool FieldTypes { get; init; }
        public bool Cvl { get; init; }
        public bool CvlValues { get; init; }
        public bool Categories { get; init; }
        public bool FieldSets { get; init; }
        public bool LinkTypes { get; init; }
        public bool Languages { get; init; }
        public bool Roles { get; init; }
        public bool RestrictedFields { get; init; }
        public bool ServerSettings { get; init; }
        public bool Users { get; init; }
    }

    private static ChangeFlags ToFlags(IriverChanges r) => new()
    {
        AnyChanges = r.AnyChanges,
        ModelReloaded = r.IsModelReloaded,
        EntityTypes = r.IsChangedEntityTypes,
        FieldTypes = r.IsChangedFieldTypes,
        Cvl = r.IsChangedCVL,
        CvlValues = r.IsChangedCVLValues,
        Categories = r.IsChangedCategories,
        FieldSets = r.IsChangedFieldSets,
        LinkTypes = r.IsChangedLinkTypes,
        Languages = r.IsChangedLanguages,
        Roles = r.IsChangedRoles,
        RestrictedFields = r.IsChangedRestrictedFieldPermission,
        ServerSettings = r.IsChangedServerSettings,
        Users = r.IsChangedUsers,
    };

    /// <summary>Pure projection of the change flags into a friendly, ordered area list. Exposed for testing.</summary>
    public static EnvironmentChanges FromFlags(ChangeFlags f, DateTime sinceUtc, DateTime checkedUtc)
    {
        ArgumentNullException.ThrowIfNull(f);
        var areas = new List<string>();
        void Add(bool flag, string label) { if (flag) areas.Add(label); }

        // Order from most to least impactful on a model apply.
        Add(f.EntityTypes, "Entity types");
        Add(f.FieldTypes, "Fields");
        Add(f.Cvl, "CVLs");
        Add(f.CvlValues, "CVL values");
        Add(f.Categories, "Categories");
        Add(f.FieldSets, "Fieldsets");
        Add(f.LinkTypes, "Link types");
        Add(f.Languages, "Languages");
        Add(f.Roles, "Roles");
        Add(f.RestrictedFields, "Restricted fields");
        Add(f.ServerSettings, "Server settings");
        Add(f.Users, "Users");

        // AnyChanges is inriver's own roll-up; trust it, but also treat a model reload or any flagged
        // area as a change even if the roll-up somehow lags.
        var any = f.AnyChanges || f.ModelReloaded || areas.Count > 0;
        return new EnvironmentChanges(any, f.ModelReloaded, areas, sinceUtc, checkedUtc);
    }
}
