namespace ModelMeister.Scaffolder;

/// <summary>How <see cref="ModelMerger"/> handles a per-concept id collision between base and overlay.</summary>
public enum MergeConflictPolicy
{
    /// <summary>The overlay's value wins; the conflict is still reported.</summary>
    OverlayWins,
    /// <summary>The base's value wins; the conflict is still reported.</summary>
    BaseWins,
    /// <summary>Reserved for future use — currently the merger always returns rather than throws.</summary>
    Fail,
}

/// <summary>
/// Merges two inriver JSON model exports. Conflicts are detected by the per-concept ID/key —
/// e.g. <c>EntityType.Id</c>, <c>CvlValue.CvlId + ":" + Key</c> — and reported as human-readable
/// strings. Useful for composing modular model fragments before scaffolding or applying.
/// </summary>
public sealed class ModelMerger
{
    /// <summary>Conflict-resolution policy for this merger.</summary>
    public MergeConflictPolicy Policy { get; }

    public ModelMerger(MergeConflictPolicy policy) => Policy = policy;

    /// <summary>Merge <paramref name="overlay"/> onto <paramref name="base"/> using <see cref="Policy"/>.</summary>
    public (InriverModelJson Merged, List<string> Conflicts) Merge(InriverModelJson @base, InriverModelJson overlay)
    {
        var conflicts = new List<string>();

        var merged = new InriverModelJson
        {
            // Header-level fields prefer the overlay's non-null value (no conflict tracking — these
            // are pure metadata and small enough that "newest wins" is the obvious policy).
            Version = overlay.Version ?? @base.Version,
            DbVersion = overlay.DbVersion ?? @base.DbVersion,
            CustomerName = overlay.CustomerName ?? @base.CustomerName,

            Languages = MergeBy(@base.Languages, overlay.Languages, l => l.Name, "Language", conflicts),
            Categories = MergeBy(@base.Categories, overlay.Categories, c => c.Id, "Category", conflicts),
            LinkTypes = MergeBy(@base.LinkTypes, overlay.LinkTypes, l => l.Id, "LinkType", conflicts),
            EntityTypes = MergeBy(@base.EntityTypes, overlay.EntityTypes, e => e.Id, "EntityType", conflicts),
            FieldSets = MergeBy(@base.FieldSets, overlay.FieldSets, f => f.Id, "FieldSet", conflicts),
            FieldTypes = MergeBy(@base.FieldTypes, overlay.FieldTypes, f => f.Id, "FieldType", conflicts),
            Cvls = MergeBy(@base.Cvls, overlay.Cvls, c => c.Id, "CVL", conflicts),
            CvlValues = MergeBy(@base.CvlValues, overlay.CvlValues, v => $"{v.CvlId}:{v.Key}", "CVLValue", conflicts),
        };

        return (merged, conflicts);
    }

    private List<T> MergeBy<T>(
        IEnumerable<T> @base,
        IEnumerable<T> overlay,
        Func<T, string> key,
        string label,
        List<string> conflicts)
    {
        var baseMap = @base.ToDictionary(key, StringComparer.OrdinalIgnoreCase);
        var overlayMap = overlay.ToDictionary(key, StringComparer.OrdinalIgnoreCase);

        var allKeys = baseMap.Keys
            .Concat(overlayMap.Keys)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        return allKeys
            .OrderBy(k => k, StringComparer.OrdinalIgnoreCase)
            .Select(k =>
            {
                var inBase = baseMap.TryGetValue(k, out var b);
                var inOverlay = overlayMap.TryGetValue(k, out var o);
                if (inBase && inOverlay)
                {
                    conflicts.Add($"{label} '{k}' present in both — policy: {Policy}");
                    return Policy == MergeConflictPolicy.BaseWins ? b! : o!;
                }
                return inOverlay ? o! : b!;
            })
            .ToList();
    }
}
