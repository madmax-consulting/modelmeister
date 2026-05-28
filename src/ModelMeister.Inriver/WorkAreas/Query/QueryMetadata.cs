namespace ModelMeister.Inriver.WorkAreas.Query;

/// <summary>
/// The model ids a query references — used to populate the builder's field/entity/link pickers and to flag
/// cross-environment validity (ids that don't exist in the target env). Captured from three cheap model
/// reads rather than a full snapshot.
/// </summary>
public sealed record QueryMetadata(
    IReadOnlyList<string> EntityTypeIds,
    IReadOnlyDictionary<string, IReadOnlyList<string>> FieldTypeIdsByEntityType,
    IReadOnlyList<string> AllFieldTypeIds,
    IReadOnlyList<string> LinkTypeIds)
{
    /// <summary>An empty metadata set — validation is skipped against it (nothing to check).</summary>
    public static QueryMetadata Empty { get; } =
        new([], new Dictionary<string, IReadOnlyList<string>>(), [], []);

    public bool IsEmpty => EntityTypeIds.Count == 0 && AllFieldTypeIds.Count == 0 && LinkTypeIds.Count == 0;

    /// <summary>Field ids for an entity type (or all fields when the entity type is unknown/blank).</summary>
    public IReadOnlyList<string> FieldsFor(string? entityTypeId) =>
        !string.IsNullOrWhiteSpace(entityTypeId) && FieldTypeIdsByEntityType.TryGetValue(entityTypeId, out var fields)
            ? fields
            : AllFieldTypeIds;
}

/// <summary>Reads the connected env's entity types, field types and link types for the query builder.</summary>
public sealed class QueryMetadataService
{
    private readonly InriverClient _client;

    public QueryMetadataService(InriverClient client) => _client = client;

    public QueryMetadata Capture()
    {
        var entityTypes = _client.Read(m => m.ModelService.GetAllEntityTypes() ?? []);
        var fieldTypes = _client.Read(m => m.ModelService.GetAllFieldTypes() ?? []);
        var linkTypes = _client.Read(m => m.ModelService.GetAllLinkTypes() ?? []);

        var byEntity = fieldTypes
            .Where(f => !string.IsNullOrEmpty(f.EntityTypeId))
            .GroupBy(f => f.EntityTypeId!, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(
                g => g.Key,
                g => (IReadOnlyList<string>)g.Select(f => f.Id).OrderBy(x => x, StringComparer.OrdinalIgnoreCase).ToList(),
                StringComparer.OrdinalIgnoreCase);

        return new QueryMetadata(
            entityTypes.Select(e => e.Id).OrderBy(x => x, StringComparer.OrdinalIgnoreCase).ToList(),
            byEntity,
            fieldTypes.Select(f => f.Id).Distinct(StringComparer.OrdinalIgnoreCase).OrderBy(x => x, StringComparer.OrdinalIgnoreCase).ToList(),
            linkTypes.Select(l => l.Id).OrderBy(x => x, StringComparer.OrdinalIgnoreCase).ToList());
    }
}
