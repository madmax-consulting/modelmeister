using IriverPermission = inRiver.Remoting.Objects.Permission;
using IriverRole = inRiver.Remoting.Objects.Role;
using ModelMeister.Inriver.Snapshot;
using ModelMeister.Model.Loading;

namespace ModelMeister.Inriver.Mapping;

/// <summary>Bi-directional mapping for inriver roles and their permission bindings.</summary>
public static class RoleMapper
{
    /// <summary>Inriver DTO -> snapshot DTO, including the permission binding list.</summary>
    public static LiveRole ToLive(IriverRole r) => new()
    {
        Id = r.Id,
        Name = r.Name,
        Description = r.Description ?? string.Empty,
        Permissions = (r.Permissions ?? [])
            .Select(PermissionMapper.ToLive)
            .ToList(),
    };

    /// <summary>Map a role for Add. The applier issues separate permission-bind calls, so the empty list is safe.</summary>
    public static IriverRole ToInriver(LoadedRole r) => new()
    {
        Name = r.Name,
        Description = r.Description,
        Permissions = [],
    };

    /// <summary>
    /// Map a role for Update preserving the live permission list. Without this, <c>UpdateRole</c> would
    /// transiently zero out role membership before the per-permission diff calls re-add them.
    /// </summary>
    public static IriverRole ToInriverForUpdate(LoadedRole r, LiveRole live) => new()
    {
        Id = live.Id,
        Name = r.Name,
        Description = r.Description,
        Permissions = live.Permissions
            .Select(p => new IriverPermission
            {
                Id = p.Id,
                Name = p.Name,
                Description = p.Description,
            })
            .ToList(),
    };
}

/// <summary>Bi-directional mapping for permission concepts (managed externally; we only bind to roles).</summary>
public static class PermissionMapper
{
    /// <summary>Inriver DTO -> snapshot DTO.</summary>
    public static LivePermission ToLive(IriverPermission p) => new()
    {
        Id = p.Id,
        Name = p.Name,
        Description = p.Description ?? string.Empty,
    };

    /// <summary>Code-defined permission reference -> inriver DTO (rarely used; concept is platform-managed).</summary>
    public static IriverPermission ToInriver(LoadedPermission p) => new()
    {
        Name = p.Name,
        Description = p.Description,
    };
}
