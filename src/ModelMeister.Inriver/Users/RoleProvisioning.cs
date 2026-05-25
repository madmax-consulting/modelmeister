using inRiver.Remoting.Objects;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace ModelMeister.Inriver.Users;

/// <summary>
/// Provisioning helper for roles + their permission bindings. Pure Remoting (no REST needed):
/// Remoting can create/update/delete roles and bind/unbind permissions. Upserts are keyed by role
/// <i>name</i> so the same workbook can target any environment (role ids differ per env).
/// </summary>
public sealed class RoleProvisioning
{
    private readonly InriverClient _remoting;
    private readonly ILogger _log;

    public RoleProvisioning(InriverClient remoting, ILogger<RoleProvisioning>? log = null)
    {
        _remoting = remoting;
        _log = (ILogger?)log ?? NullLogger.Instance;
    }

    /// <summary>Specification of a role to provision: identity + description + desired permission names.</summary>
    public sealed record RoleSpec(string Name, string? Description, IReadOnlyList<string> Permissions);

    /// <summary>Outcome of a single <see cref="ProvisionAsync"/> call.</summary>
    public sealed record ProvisionResult(string RoleName, bool Created, bool PermissionsSynced, IReadOnlyList<string> Errors);

    /// <summary>List all roles + their bound permission names in the connected env. Remoting-only.</summary>
    public IReadOnlyList<RoleSummary> ListRoles()
    {
        var roles = _remoting.Read(m => m.UserService.GetAllRoles() ?? []);
        return roles.Select(r => new RoleSummary
        {
            Id = r.Id,
            Name = r.Name,
            Description = r.Description ?? string.Empty,
            Permissions = (r.Permissions ?? [])
                .Select(p => p.Name)
                .OrderBy(s => s, StringComparer.OrdinalIgnoreCase)
                .ToList(),
        })
        .OrderBy(r => r.Name, StringComparer.OrdinalIgnoreCase)
        .ToList();
    }

    /// <summary>List permission names available in the connected env (for workbook reference + import validation).</summary>
    public IReadOnlyList<string> ListPermissionNames() =>
        _remoting.Read(m => m.UserService.GetAllPermissions() ?? [])
            .Select(p => p.Name)
            .OrderBy(s => s, StringComparer.OrdinalIgnoreCase)
            .ToList();

    /// <summary>
    /// Upsert a role by name. Missing roles are created; existing ones get their description synced.
    /// Permission membership is reconciled (add/remove) by name — never via the <c>UpdateRole</c>
    /// payload, which would transiently zero membership. Missing permissions are reported, not thrown.
    /// </summary>
    public async Task<ProvisionResult> ProvisionAsync(RoleSpec spec, CancellationToken ct = default)
    {
        var errors = new List<string>();
        bool created = false;

        var existing = _remoting.Read(m =>
        {
            try { return m.UserService.GetRoleByName(spec.Name); }
            catch { return null; }
        });

        int roleId;
        if (existing is null)
        {
            try
            {
                var added = await _remoting.WriteAsync(m => m.UserService.AddRole(new Role
                {
                    Name = spec.Name,
                    Description = spec.Description ?? string.Empty,
                    Permissions = [],
                }), ct).ConfigureAwait(false);
                created = true;
                // Some inriver builds return the created role without a populated id — re-resolve by name.
                roleId = added?.Id is { } id and not 0 ? id : _remoting.Read(m => m.UserService.GetRoleByName(spec.Name)?.Id ?? 0);
                _log.LogInformation("Role {Name} created (id {Id}).", spec.Name, roleId);
            }
            catch (Exception ex)
            {
                errors.Add("Create role failed: " + ex.Message);
                return new ProvisionResult(spec.Name, false, false, errors);
            }
        }
        else
        {
            roleId = existing.Id;
            if (!string.Equals(existing.Description ?? string.Empty, spec.Description ?? string.Empty, StringComparison.Ordinal))
            {
                try
                {
                    // Preserve the live permission list on the Update payload; membership is reconciled below.
                    await _remoting.WriteAsync(m => m.UserService.UpdateRole(new Role
                    {
                        Id = existing.Id,
                        Name = existing.Name,
                        Description = spec.Description ?? string.Empty,
                        Permissions = existing.Permissions ?? [],
                    }), ct).ConfigureAwait(false);
                }
                catch (Exception ex) { errors.Add("Update role description failed: " + ex.Message); }
            }
        }

        if (roleId == 0)
        {
            errors.Add($"Could not resolve a role id for '{spec.Name}'.");
            return new ProvisionResult(spec.Name, created, false, errors);
        }

        var permsSynced = await SyncPermissionsAsync(roleId, existing, spec.Permissions, errors, ct).ConfigureAwait(false);
        return new ProvisionResult(spec.Name, created, permsSynced, errors);
    }

    private async Task<bool> SyncPermissionsAsync(int roleId, Role? existing, IReadOnlyList<string> desiredNames, List<string> errors, CancellationToken ct)
    {
        var allPerms = _remoting.Read(m => m.UserService.GetAllPermissions() ?? [])
            .GroupBy(p => p.Name, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key, g => g.First().Id, StringComparer.OrdinalIgnoreCase);

        var current = (existing?.Permissions ?? []).Select(p => p.Name).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var desired = desiredNames.Where(n => !string.IsNullOrWhiteSpace(n)).ToHashSet(StringComparer.OrdinalIgnoreCase);

        var toAdd = desired.Except(current, StringComparer.OrdinalIgnoreCase).ToList();
        var toRemove = current.Except(desired, StringComparer.OrdinalIgnoreCase).ToList();

        bool ok = true;
        foreach (var name in toAdd)
        {
            if (!allPerms.TryGetValue(name, out var pid))
            {
                errors.Add($"Permission '{name}' not found in target environment — skipping.");
                ok = false;
                continue;
            }
            try { await _remoting.WriteAsync(m => m.UserService.AddPermissionToRole(pid, roleId), ct).ConfigureAwait(false); }
            catch (Exception ex) { errors.Add($"Bind permission '{name}' failed: {ex.Message}"); ok = false; }
        }
        foreach (var name in toRemove)
        {
            if (!allPerms.TryGetValue(name, out var pid)) continue;
            try { await _remoting.WriteAsync(m => m.UserService.RemovePermissionFromRole(pid, roleId), ct).ConfigureAwait(false); }
            catch (Exception ex) { errors.Add($"Unbind permission '{name}' failed: {ex.Message}"); ok = false; }
        }
        return ok;
    }
}

/// <summary>Lightweight role record returned by <see cref="RoleProvisioning.ListRoles"/>.</summary>
public sealed class RoleSummary
{
    public int Id { get; set; }
    public string Name { get; set; } = "";
    public string Description { get; set; } = "";
    public List<string> Permissions { get; set; } = [];
}
