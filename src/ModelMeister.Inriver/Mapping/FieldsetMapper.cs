using IriverFieldSet = inRiver.Remoting.Objects.FieldSet;
using ModelMeister.Inriver.Snapshot;
using ModelMeister.Model.Loading;

namespace ModelMeister.Inriver.Mapping;

/// <summary>Bi-directional mapping for field sets.</summary>
public static class FieldsetMapper
{
    /// <summary>Inriver DTO -> snapshot DTO. Member field-type ids are preserved verbatim.</summary>
    public static LiveFieldset ToLive(IriverFieldSet f) => new()
    {
        Id = f.Id,
        EntityTypeId = f.EntityTypeId,
        Name = LocaleStringMapper.ToTp(f.Name),
        Description = LocaleStringMapper.ToTp(f.Description),
        FieldTypeIds = f.FieldTypes is null ? [] : [.. f.FieldTypes],
    };

    /// <summary>
    /// Map a code-defined fieldset to an inriver DTO. For Update, pass <paramref name="live"/> so existing
    /// member field-type IDs survive the round-trip — <c>UpdateFieldSet</c> will otherwise blank the
    /// membership (the per-field add/remove diff records re-establish it, but only after a transient window).
    /// </summary>
    public static IriverFieldSet ToInriver(LoadedFieldset f, LiveFieldset? live = null) => new()
    {
        Id = f.FieldsetId,
        EntityTypeId = f.EntityTypeId,
        Name = LocaleStringMapper.ToInriver(f.Name),
        Description = LocaleStringMapper.ToInriver(f.Description),
        // Preserve live membership when updating; an empty list on the Add path lets inriver default.
        FieldTypes = live is null ? [] : [.. live.FieldTypeIds],
    };
}
