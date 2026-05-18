using IriverRestricted = inRiver.Remoting.Objects.RestrictedFieldPermission;
using ModelMeister.Inriver.Snapshot;

namespace ModelMeister.Inriver.Mapping;

/// <summary>
/// Bi-directional mapping for restricted-field permission rows. The inriver DTO uses empty strings
/// where the snapshot uses nulls, so these helpers normalise in both directions.
/// </summary>
public static class RestrictedFieldPermissionMapper
{
    /// <summary>Inriver DTO -> snapshot DTO (empty -> null).</summary>
    public static LiveRestrictedFieldPermission ToLive(IriverRestricted r) => new()
    {
        Id = r.Id,
        RoleId = r.RoleId,
        RestrictionType = r.RestrictionType ?? string.Empty,
        EntityTypeId = NullIfEmpty(r.EntityTypeId),
        FieldTypeId = NullIfEmpty(r.FieldTypeId),
        CategoryId = NullIfEmpty(r.CategoryId),
    };

    /// <summary>Snapshot DTO -> inriver DTO (null -> empty).</summary>
    public static IriverRestricted ToInriver(LiveRestrictedFieldPermission r) => new()
    {
        RoleId = r.RoleId,
        RestrictionType = r.RestrictionType,
        EntityTypeId = r.EntityTypeId ?? string.Empty,
        FieldTypeId = r.FieldTypeId ?? string.Empty,
        CategoryId = r.CategoryId ?? string.Empty,
    };

    private static string? NullIfEmpty(string? s) => string.IsNullOrEmpty(s) ? null : s;
}
