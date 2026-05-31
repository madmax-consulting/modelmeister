namespace ModelMeister.Inriver.WorkAreas.Query;

/// <summary>
/// The model ids a query references — used to populate the builder's field/entity/link pickers and to flag
/// cross-environment validity (ids that don't exist in the target env). Captured from cheap model reads
/// rather than a full snapshot.
/// <para><see cref="FieldDataTypeById"/> / <see cref="CvlIdByFieldId"/> let the builder pick a typed value
/// editor (bool toggle, date picker, number box, free text) and validate datatype mismatches; both are
/// derived from the same <c>GetAllFieldTypes</c> read, so they add no new remoting surface. CVL and segment
/// values stay validated free-text in v1 (no live CVL/segment dropdown), so the coverage gate is untouched.</para>
/// </summary>
public sealed record QueryMetadata(
    IReadOnlyList<string> EntityTypeIds,
    IReadOnlyDictionary<string, IReadOnlyList<string>> FieldTypeIdsByEntityType,
    IReadOnlyList<string> AllFieldTypeIds,
    IReadOnlyList<string> LinkTypeIds,
    IReadOnlyDictionary<string, string>? FieldDataTypeById = null,
    IReadOnlyDictionary<string, string>? CvlIdByFieldId = null)
{
    /// <summary>An empty metadata set — validation is skipped against it (nothing to check).</summary>
    public static QueryMetadata Empty { get; } =
        new([], new Dictionary<string, IReadOnlyList<string>>(), [], []);

    /// <summary>Field-type id → inriver data type ("String", "CVL", "Boolean", "DateTime", "Integer",
    /// "Double", "LocaleString", …). Empty when datatype info wasn't captured (legacy callers).</summary>
    public IReadOnlyDictionary<string, string> FieldDataTypeById { get; init; } =
        FieldDataTypeById ?? new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

    /// <summary>Field-type id → owning CVL id, for fields whose data type is a CVL. Empty otherwise.</summary>
    public IReadOnlyDictionary<string, string> CvlIdByFieldId { get; init; } =
        CvlIdByFieldId ?? new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

    public bool IsEmpty => EntityTypeIds.Count == 0 && AllFieldTypeIds.Count == 0 && LinkTypeIds.Count == 0;

    /// <summary>Field ids for an entity type (or all fields when the entity type is unknown/blank).</summary>
    public IReadOnlyList<string> FieldsFor(string? entityTypeId) =>
        !string.IsNullOrWhiteSpace(entityTypeId) && FieldTypeIdsByEntityType.TryGetValue(entityTypeId, out var fields)
            ? fields
            : AllFieldTypeIds;

    /// <summary>The inriver data type of a field, or <c>null</c> when unknown (no datatype info / unknown id).</summary>
    public string? DataTypeOf(string? fieldTypeId) =>
        !string.IsNullOrWhiteSpace(fieldTypeId) && FieldDataTypeById.TryGetValue(fieldTypeId, out var dt) ? dt : null;
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

        // Datatype + CVL-owner maps come from the same GetAllFieldTypes read (no new remoting). Last write wins
        // for ids shared across entity types — the datatype is the same regardless of owner.
        var dataTypeById = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var cvlIdByField = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var f in fieldTypes)
        {
            if (string.IsNullOrEmpty(f.Id)) continue;
            if (!string.IsNullOrEmpty(f.DataType)) dataTypeById[f.Id] = f.DataType;
            if (!string.IsNullOrEmpty(f.CVLId)) cvlIdByField[f.Id] = f.CVLId;
        }

        return new QueryMetadata(
            entityTypes.Select(e => e.Id).OrderBy(x => x, StringComparer.OrdinalIgnoreCase).ToList(),
            byEntity,
            fieldTypes.Select(f => f.Id).Distinct(StringComparer.OrdinalIgnoreCase).OrderBy(x => x, StringComparer.OrdinalIgnoreCase).ToList(),
            linkTypes.Select(l => l.Id).OrderBy(x => x, StringComparer.OrdinalIgnoreCase).ToList(),
            dataTypeById,
            cvlIdByField);
    }
}
