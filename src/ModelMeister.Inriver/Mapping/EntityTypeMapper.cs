using IriverEntityType = inRiver.Remoting.Objects.EntityType;
using IriverFieldType = inRiver.Remoting.Objects.FieldType;
using IriverLocaleString = inRiver.Remoting.Objects.LocaleString;
using ModelMeister.Inriver.Snapshot;
using ModelMeister.Model.Loading;

namespace ModelMeister.Inriver.Mapping;

/// <summary>Bi-directional mapping for entity types and their display-field metadata.</summary>
public static class EntityTypeMapper
{
    /// <summary>Inriver DTO -> snapshot DTO, including its field list.</summary>
    public static LiveEntityType ToLive(IriverEntityType e) => new()
    {
        Id = e.Id,
        Name = LocaleStringMapper.ToTp(e.Name),
        IsLinkEntityType = e.IsLinkEntityType,
        DisplayNameFieldId = e.GetDisplayNameFieldTypeId,
        DisplayDescriptionFieldId = e.GetDisplayDescriptionFieldTypeId,
        Fields = (e.FieldTypes ?? [])
            .Select(FieldTypeMapper.ToLive)
            .ToList(),
    };

    /// <summary>Convenience overload: maps an entity type along with its own declared fields.</summary>
    public static IriverEntityType ToInriver(LoadedEntityType e) => ToInriver(e, e.Fields);

    /// <summary>
    /// Map an entity type for Add/Update. Populates <c>FieldTypes</c> with stub field DTOs so that
    /// the server-side derived <c>GetDisplayNameFieldTypeId</c> / <c>GetDisplayDescriptionFieldTypeId</c>
    /// (computed from a field whose <c>IsDisplayName</c>/<c>IsDisplayDescription</c> flag is true)
    /// remain stable across <c>UpdateEntityType</c>. Without this, an Update would clear those display ids.
    /// </summary>
    public static IriverEntityType ToInriver(LoadedEntityType e, IEnumerable<LoadedField> fields)
    {
        // Avoid multiple enumeration — fields may be a lazy enumerable.
        var fieldList = fields as IList<LoadedField> ?? fields.ToList();
        var displayName = fieldList.FirstOrDefault(f => f.Field.IsDisplayName);
        var displayDesc = fieldList.FirstOrDefault(f => f.Field.IsDisplayDescription);

        // Emit minimal stubs (Id + the IsDisplay* flag) — enough for the server-side derivation,
        // without re-asserting every other field property.
        var stubs = new List<IriverFieldType>();
        if (displayName is not null)
        {
            stubs.Add(new IriverFieldType
            {
                Id = displayName.Id,
                EntityTypeId = e.EntityTypeId,
                Name = new IriverLocaleString(),
                IsDisplayName = true,
            });
        }
        if (displayDesc is not null && (displayName is null || displayDesc.Id != displayName.Id))
        {
            stubs.Add(new IriverFieldType
            {
                Id = displayDesc.Id,
                EntityTypeId = e.EntityTypeId,
                Name = new IriverLocaleString(),
                IsDisplayDescription = true,
            });
        }

        return new IriverEntityType
        {
            Id = e.EntityTypeId,
            Name = LocaleStringMapper.ToInriver(e.Name),
            IsLinkEntityType = e.IsLinkEntityType,
            FieldTypes = stubs,
        };
    }
}
