using IriverRestricted = inRiver.Remoting.Objects.RestrictedFieldPermission;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace ModelMeister.Inriver.Users;

/// <summary>
/// Provisioning helper for restricted-field permissions. Remoting offers add + delete only (no update),
/// so "manage" is add/delete. A row is fully described by its natural key
/// (role name + restriction type + entity/field/category ids); the role <i>name</i> is resolved to a
/// target-env role id at add time, keeping the spec environment-agnostic.
/// </summary>
public sealed class RestrictedFieldProvisioning
{
    private readonly InriverClient _remoting;
    private readonly ILogger _log;

    public RestrictedFieldProvisioning(InriverClient remoting, ILogger<RestrictedFieldProvisioning>? log = null)
    {
        _remoting = remoting;
        _log = (ILogger?)log ?? NullLogger.Instance;
    }

    /// <summary>A restricted-field permission to add. Uses role <i>name</i>, not id, for cross-env portability.</summary>
    public sealed record RestrictedFieldSpec(
        string RoleName,
        string RestrictionType,
        string? EntityTypeId,
        string? FieldTypeId,
        string? CategoryId);

    /// <summary>Outcome of a single add/delete.</summary>
    public sealed record ProvisionResult(string NaturalKey, bool Created, bool Deleted, IReadOnlyList<string> Errors);

    /// <summary>List all restricted-field permissions, resolving each row's RoleId to a role name.</summary>
    public IReadOnlyList<RestrictedFieldSummary> ListRestrictedFields()
    {
        var roleNameById = _remoting.Read(m => m.UserService.GetAllRoles() ?? [])
            .GroupBy(r => r.Id)
            .ToDictionary(g => g.Key, g => g.First().Name);
        var rows = _remoting.Read(m => m.UserService.GetAllRestrictedFieldPermissions() ?? []);
        return rows.Select(r => new RestrictedFieldSummary
        {
            Id = r.Id,
            RoleId = r.RoleId,
            RoleName = roleNameById.GetValueOrDefault(r.RoleId, $"(role {r.RoleId})"),
            RestrictionType = r.RestrictionType ?? string.Empty,
            EntityTypeId = NullIfEmpty(r.EntityTypeId),
            FieldTypeId = NullIfEmpty(r.FieldTypeId),
            CategoryId = NullIfEmpty(r.CategoryId),
        })
        .OrderBy(r => r.RoleName, StringComparer.OrdinalIgnoreCase)
        .ThenBy(r => r.NaturalKey, StringComparer.OrdinalIgnoreCase)
        .ToList();
    }

    /// <summary>Add a restricted-field permission. Resolves the role name on the target env (errors if absent).</summary>
    public async Task<ProvisionResult> AddAsync(RestrictedFieldSpec spec, CancellationToken ct = default)
    {
        var errors = new List<string>();
        var restrictionType = NormalizeRestrictionType(spec.RestrictionType);
        var key = NaturalKey(spec.RoleName, restrictionType ?? spec.RestrictionType, spec.EntityTypeId, spec.FieldTypeId, spec.CategoryId);

        if (restrictionType is null)
        {
            errors.Add($"Restriction type '{spec.RestrictionType}' is invalid — must be 'Readonly' or 'Hidden'.");
            return new ProvisionResult(key, false, false, errors);
        }

        var role = _remoting.Read(m =>
        {
            try { return m.UserService.GetRoleByName(spec.RoleName); }
            catch { return null; }
        });
        if (role is null)
        {
            errors.Add($"Role '{spec.RoleName}' not found in target environment.");
            return new ProvisionResult(key, false, false, errors);
        }

        try
        {
            await _remoting.WriteAsync(m => m.UserService.AddRestrictedFieldPermission(new IriverRestricted
            {
                RoleId = role.Id,
                RestrictionType = restrictionType,
                EntityTypeId = spec.EntityTypeId ?? string.Empty,
                FieldTypeId = spec.FieldTypeId ?? string.Empty,
                CategoryId = spec.CategoryId ?? string.Empty,
            }), ct).ConfigureAwait(false);
            return new ProvisionResult(key, true, false, errors);
        }
        catch (Exception ex)
        {
            errors.Add("Add restricted-field permission failed: " + ex.Message);
            return new ProvisionResult(key, false, false, errors);
        }
    }

    /// <summary>Delete a restricted-field permission by its live id.</summary>
    public async Task<ProvisionResult> DeleteAsync(int liveId, CancellationToken ct = default)
    {
        var errors = new List<string>();
        try
        {
            await _remoting.WriteAsync(m => m.UserService.DeleteRestrictedFieldPermission(liveId), ct).ConfigureAwait(false);
            return new ProvisionResult($"#{liveId}", false, true, errors);
        }
        catch (Exception ex)
        {
            errors.Add("Delete restricted-field permission failed: " + ex.Message);
            return new ProvisionResult($"#{liveId}", false, false, errors);
        }
    }

    /// <summary>The cross-env natural key of a restricted-field permission.</summary>
    public static string NaturalKey(string roleName, string restrictionType, string? entityTypeId, string? fieldTypeId, string? categoryId)
        => string.Join("|", roleName, restrictionType, entityTypeId ?? "", fieldTypeId ?? "", categoryId ?? "");

    /// <summary>
    /// The two restriction types inriver accepts, in canonical casing — note the lowercase 'o' in
    /// <c>Readonly</c> (inriver rejects "ReadOnly"). Maps case-insensitively; returns <c>null</c> for
    /// anything else so callers can reject it with a clear message.
    /// </summary>
    public static string? NormalizeRestrictionType(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return null;
        var v = value.Trim();
        if (string.Equals(v, "Readonly", StringComparison.OrdinalIgnoreCase)) return "Readonly";
        if (string.Equals(v, "Hidden", StringComparison.OrdinalIgnoreCase)) return "Hidden";
        return null;
    }

    /// <summary>Normalises inriver's empty-string scope ids (and blank workbook cells) to null.</summary>
    public static string? NullIfEmpty(string? s) => string.IsNullOrWhiteSpace(s) ? null : s;
}

/// <summary>Lightweight restricted-field record returned by <see cref="RestrictedFieldProvisioning.ListRestrictedFields"/>.</summary>
public sealed class RestrictedFieldSummary
{
    public int Id { get; set; }
    public int RoleId { get; set; }
    public string RoleName { get; set; } = "";
    public string RestrictionType { get; set; } = "";
    public string? EntityTypeId { get; set; }
    public string? FieldTypeId { get; set; }
    public string? CategoryId { get; set; }

    /// <summary>Cross-env identity: ignores the env-specific live id and role id.</summary>
    public string NaturalKey => RestrictedFieldProvisioning.NaturalKey(RoleName, RestrictionType, EntityTypeId, FieldTypeId, CategoryId);
}
