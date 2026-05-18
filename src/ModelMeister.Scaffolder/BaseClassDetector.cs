namespace ModelMeister.Scaffolder;

/// <summary>
/// Detects shared field patterns across entity types and suggests abstract base classes that
/// factor them out. Trigger: the same set of <c>(PropertyName, DataType)</c> tuples (size ≥ 2)
/// appears in at least two entity types.
/// </summary>
public static class BaseClassDetector
{
    /// <summary>
    /// Scans the model and returns one <see cref="DetectedBaseClass"/> per group of entity types
    /// that share an identical member set.
    /// </summary>
    public static List<DetectedBaseClass> Detect(InriverModelJson model)
    {
        // For each entity type, derive its "shared-shape" members: fields whose id starts with the
        // entity type id (e.g. ProductName, ProductDescription) projected to (PropertyName, DataType).
        var membersByEntity = model.EntityTypes.ToDictionary(
            e => e.Id,
            e => (e.FieldTypes ?? [])
                .Where(f => f.Id.StartsWith(e.Id, StringComparison.OrdinalIgnoreCase))
                .Select(f => new BaseClassMember(f.Id.Substring(e.Id.Length), f.DataType))
                .ToList(),
            StringComparer.OrdinalIgnoreCase);

        // Group entities by their full ordered member-set signature; keep groups of ≥ 2 entities
        // whose member sets are non-trivial (≥ 2 members).
        return membersByEntity
            .Where(kvp => kvp.Value.Count >= 2)
            .GroupBy(kvp => string.Join(",", kvp.Value
                .Select(m => $"{m.PropertyName}:{m.DataType}")
                .OrderBy(s => s, StringComparer.Ordinal)))
            .Where(g => g.Count() >= 2)
            .Select(g =>
            {
                var entityIds = g.Select(x => x.Key).ToList();
                return new DetectedBaseClass(
                    ClassName: SuggestBaseName(entityIds),
                    Members: membersByEntity[g.First().Key],
                    EntityTypeIds: entityIds);
            })
            .ToList();
    }

    /// <summary>
    /// Heuristic class-name suggestion: longest common prefix of the entity ids, suffixed
    /// "Base"; falls back to <c>SharedEntityN</c> when no useful prefix exists.
    /// </summary>
    private static string SuggestBaseName(IList<string> entityTypeIds)
    {
        var common = LongestCommonPrefix(entityTypeIds);
        return common.Length < 3
            ? "SharedEntity" + entityTypeIds.Count
            : common + "Base";
    }

    private static string LongestCommonPrefix(IList<string> values)
    {
        if (values.Count == 0) return string.Empty;
        var prefix = values[0];
        foreach (var v in values.Skip(1))
        {
            while (prefix.Length > 0 && !v.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                prefix = prefix[..^1];
        }
        return prefix;
    }
}

/// <summary>A detected abstract base class candidate.</summary>
public sealed record DetectedBaseClass(string ClassName, List<BaseClassMember> Members, List<string> EntityTypeIds);

/// <summary>One member of a <see cref="DetectedBaseClass"/>: a property name + inriver datatype.</summary>
public sealed record BaseClassMember(string PropertyName, string DataType);
