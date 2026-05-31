using ModelMeister.Inriver.Mapping;

namespace ModelMeister.Inriver.WorkAreas.Query;

/// <summary>One selectable value of a CVL: the <see cref="Key"/> stored in the query and a human
/// <see cref="Display"/> (the localized value, falling back to the key) shown in the picker.</summary>
public sealed record CvlValueOption(string Key, string Display)
{
    /// <summary>Combined "Display · Key" used so an autocomplete box matches on either the name or the key.</summary>
    public string Search => string.Equals(Display, Key, StringComparison.Ordinal) ? Key : $"{Display} · {Key}";
}

/// <summary>
/// The model ids a query references — used to populate the builder's field/entity/link pickers and to flag
/// cross-environment validity (ids that don't exist in the target env). Captured from cheap model reads
/// rather than a full snapshot.
/// <para><see cref="FieldDataTypeById"/> / <see cref="CvlIdByFieldId"/> let the builder pick a typed value
/// editor (bool toggle, date picker, number box, free text) and validate datatype mismatches; all are
/// derived from the same <c>GetAllFieldTypes</c> read. <see cref="CvlValuesByCvlId"/> adds a live, model-driven
/// value dropdown for CVL fields (read once per referenced CVL via <c>GetCVLValuesForCVL</c>).</para>
/// </summary>
public sealed record QueryMetadata(
    IReadOnlyList<string> EntityTypeIds,
    IReadOnlyDictionary<string, IReadOnlyList<string>> FieldTypeIdsByEntityType,
    IReadOnlyList<string> AllFieldTypeIds,
    IReadOnlyList<string> LinkTypeIds,
    IReadOnlyDictionary<string, string>? FieldDataTypeById = null,
    IReadOnlyDictionary<string, string>? CvlIdByFieldId = null,
    IReadOnlyDictionary<string, IReadOnlyList<CvlValueOption>>? CvlValuesByCvlId = null)
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

    /// <summary>CVL id → its selectable values. Drives the model-driven value dropdown for CVL fields.</summary>
    public IReadOnlyDictionary<string, IReadOnlyList<CvlValueOption>> CvlValuesByCvlId { get; init; } =
        CvlValuesByCvlId ?? new Dictionary<string, IReadOnlyList<CvlValueOption>>(StringComparer.OrdinalIgnoreCase);

    public bool IsEmpty => EntityTypeIds.Count == 0 && AllFieldTypeIds.Count == 0 && LinkTypeIds.Count == 0;

    /// <summary>Field ids for an entity type (or all fields when the entity type is unknown/blank).</summary>
    public IReadOnlyList<string> FieldsFor(string? entityTypeId) =>
        !string.IsNullOrWhiteSpace(entityTypeId) && FieldTypeIdsByEntityType.TryGetValue(entityTypeId, out var fields)
            ? fields
            : AllFieldTypeIds;

    /// <summary>The inriver data type of a field, or <c>null</c> when unknown (no datatype info / unknown id).</summary>
    public string? DataTypeOf(string? fieldTypeId) =>
        !string.IsNullOrWhiteSpace(fieldTypeId) && FieldDataTypeById.TryGetValue(fieldTypeId, out var dt) ? dt : null;

    /// <summary>True when the field's data type is a CVL (so its value should be picked from a model dropdown).</summary>
    public bool IsCvlField(string? fieldTypeId) =>
        !string.IsNullOrWhiteSpace(fieldTypeId) && CvlIdByFieldId.ContainsKey(fieldTypeId);

    /// <summary>The selectable CVL values for a CVL field, or empty when the field isn't a CVL / has no values.</summary>
    public IReadOnlyList<CvlValueOption> CvlValuesFor(string? fieldTypeId) =>
        !string.IsNullOrWhiteSpace(fieldTypeId)
        && CvlIdByFieldId.TryGetValue(fieldTypeId, out var cvlId)
        && CvlValuesByCvlId.TryGetValue(cvlId, out var values)
            ? values
            : [];
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

        // Read the values of each referenced CVL once so the builder can offer a model-driven value dropdown.
        // A CVL that can't be read just falls back to free-text entry (its key stays a valid value).
        var cvlValues = new Dictionary<string, IReadOnlyList<CvlValueOption>>(StringComparer.OrdinalIgnoreCase);
        foreach (var cvlId in cvlIdByField.Values.Distinct(StringComparer.OrdinalIgnoreCase))
        {
            if (cvlValues.ContainsKey(cvlId)) continue;
            try
            {
                var values = _client.Read(m => m.ModelService.GetCVLValuesForCVL(cvlId, false)) ?? [];
                cvlValues[cvlId] = values
                    .Where(v => v is not null && !v.Deactivated && !string.IsNullOrEmpty(v.Key))
                    .OrderBy(v => v.Index)
                    .Select(v =>
                    {
                        var display = CvlValueDisplay(v.Value);
                        return new CvlValueOption(v.Key!, string.IsNullOrWhiteSpace(display) ? v.Key! : display);
                    })
                    .ToList();
            }
            catch
            {
                // Leave this CVL without a dropdown; the value editor degrades to free text.
            }
        }

        return new QueryMetadata(
            entityTypes.Select(e => e.Id).OrderBy(x => x, StringComparer.OrdinalIgnoreCase).ToList(),
            byEntity,
            fieldTypes.Select(f => f.Id).Distinct(StringComparer.OrdinalIgnoreCase).OrderBy(x => x, StringComparer.OrdinalIgnoreCase).ToList(),
            linkTypes.Select(l => l.Id).OrderBy(x => x, StringComparer.OrdinalIgnoreCase).ToList(),
            dataTypeById,
            cvlIdByField,
            cvlValues);
    }

    /// <summary>Best-effort display text for a CVL value's <c>Value</c> object (a localized
    /// <c>LocaleString</c> or a primitive). Falls back to the empty string; the caller uses the value's key
    /// when this is blank.</summary>
    private static string CvlValueDisplay(object? value) => value switch
    {
        // ToTp sets DefaultValue to the first non-empty translation, which is the best single label we have.
        inRiver.Remoting.Objects.LocaleString ls => LocaleStringMapper.ToTp(ls).DefaultValue ?? "",
        null => "",
        var raw => raw.ToString() ?? "",
    };
}
