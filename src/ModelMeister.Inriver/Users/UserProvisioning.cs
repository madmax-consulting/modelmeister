using inRiver.Remoting.Objects;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using ModelMeister.Rest;

namespace ModelMeister.Inriver.Users;

/// <summary>
/// Provisioning helper. Remoting can <i>read</i> users and <i>add to roles</i>, but cannot <i>create</i>
/// a new user. So this class layers two strategies: try the REST API (which supports user create
/// when the caller has the <c>APIManageUsers</c> permission), then fall back to Remoting for the
/// post-create role assignment.
/// </summary>
public sealed class UserProvisioning
{
    private readonly InriverClient _remoting;
    private readonly InriverRestClient? _rest;
    private readonly ILogger _log;

    /// <summary>
    /// Wraps the supplied <paramref name="remoting"/> connection. Supply a <paramref name="rest"/>
    /// client to enable user-create; without one, this class can only assign roles to existing users.
    /// </summary>
    public UserProvisioning(InriverClient remoting, InriverRestClient? rest = null, ILogger<UserProvisioning>? log = null)
    {
        _remoting = remoting;
        _rest = rest;
        _log = (ILogger?)log ?? NullLogger.Instance;
    }

    /// <summary>Specification of a user to provision: identity + role memberships + per-user flags.</summary>
    public sealed record UserSpec(
        string Username,
        string? Email,
        string? FirstName,
        string? LastName,
        IReadOnlyList<string> Roles,
        string Language = "en",
        bool GenerateApiKey = false);

    /// <summary>Outcome of a single <see cref="ProvisionAsync"/> call.</summary>
    public sealed record ProvisionResult(string Username, bool Created, bool RolesAssigned, string? ApiKey, IReadOnlyList<string> Errors);

    /// <summary>List all users in the connected env. Remoting-only.</summary>
    public IReadOnlyList<UserSummary> ListUsers()
    {
        var users = _remoting.Read(m => m.UserService.GetAllUsers() ?? []);
        var roles = _remoting.Read(m => m.UserService.GetAllRoles() ?? [])
            .ToDictionary(r => r.Id, r => r.Name);
        var summaries = new List<UserSummary>();
        foreach (var u in users)
        {
            var userRoles = SafeRolesForUser(u.Username);
            summaries.Add(new UserSummary
            {
                Id = u.Id,
                Username = u.Username,
                Email = u.Email,
                FirstName = u.FirstName,
                LastName = u.LastName,
                Active = true,
                Roles = userRoles.Select(r => r.Name).ToList(),
            });
        }
        return summaries;
    }

    /// <summary>Returns the user's roles, swallowing transport errors as an empty list (callers don't care why).</summary>
    private IReadOnlyList<Role> SafeRolesForUser(string username)
    {
        try { return _remoting.Read(m => m.UserService.GetRolesForUser(username) ?? []); }
        catch { return []; }
    }

    /// <summary>List role names in the connected env. Useful for validating Excel imports.</summary>
    public IReadOnlyList<string> ListRoleNames() =>
        _remoting.Read(m => m.UserService.GetAllRoles() ?? [])
            .Select(r => r.Name)
            .OrderBy(s => s, StringComparer.OrdinalIgnoreCase)
            .ToList();

    /// <summary>
    /// Provision a user. Tries REST create first (Remoting can't create users); on REST failure or
    /// when the user already exists, the existing user is reused. Role assignments use Remoting.
    /// </summary>
    public async Task<ProvisionResult> ProvisionAsync(UserSpec spec, CancellationToken ct = default)
    {
        var errors = new List<string>();
        bool created = false;
        string? apiKey = null;

        var existing = _remoting.Read(m =>
        {
            try { return m.UserService.GetUserByUsername(spec.Username); }
            catch { return null; }
        });

        if (existing is null)
        {
            if (_rest is null)
                errors.Add("User does not exist and no REST API client is configured (Remoting cannot create users).");
            else
            {
                try
                {
                    // Roles ride in as a single segment-0 (default/global) entry; an empty
                    // segmentRoles array when the user has none. The create endpoint requires the
                    // field to be present either way.
                    var create = new UserCreate
                    {
                        Username = spec.Username,
                        Email = spec.Email,
                        FirstName = spec.FirstName,
                        LastName = spec.LastName,
                    };
                    var roleNames = spec.Roles.Where(r => !string.IsNullOrWhiteSpace(r)).ToList();
                    if (roleNames.Count > 0)
                        create.SegmentRoles.Add(new ModelMeister.Rest.SegmentRole { SegmentId = 0, RoleNames = roleNames });
                    var created2 = await _rest.CreateUserAsync(create, ct).ConfigureAwait(false);
                    created = true;
                    if (created2 is not null && !string.IsNullOrEmpty(created2.ApiKey)) apiKey = created2.ApiKey;
                    _log.LogInformation("User {Username} created via REST (id {Id}).", spec.Username, created2?.Id);
                }
                catch (Exception ex)
                {
                    errors.Add("REST create failed: " + ex.Message);
                    _log.LogWarning(ex, "REST user create failed for {Username}", spec.Username);
                }
            }
        }

        // Roles via Remoting (works whether the user was just created via REST or already existed).
        var rolesAssigned = await AssignRolesAsync(spec.Username, spec.Roles, errors, ct).ConfigureAwait(false);

        if (spec.GenerateApiKey && apiKey is null)
        {
            try
            {
                apiKey = await _remoting.WriteAsync(m => m.UserService.GenerateUserRestApiKey(spec.Username), ct).ConfigureAwait(false);
            }
            catch (Exception ex) { errors.Add("Generate API key failed: " + ex.Message); }
        }

        return new ProvisionResult(spec.Username, created, rolesAssigned, apiKey, errors);
    }

    private async Task<bool> AssignRolesAsync(string username, IReadOnlyList<string> desiredRoleNames, List<string> errors, CancellationToken ct)
    {
        var user = _remoting.Read(m =>
        {
            try { return m.UserService.GetUserByUsername(username); }
            catch { return null; }
        });
        if (user is null) return false;

        var allRoles = _remoting.Read(m => m.UserService.GetAllRoles() ?? [])
            .ToDictionary(r => r.Name, StringComparer.OrdinalIgnoreCase);
        var currentRoles = SafeRolesForUser(username).Select(r => r.Name).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var desiredRoles = desiredRoleNames.Where(r => !string.IsNullOrWhiteSpace(r)).ToHashSet(StringComparer.OrdinalIgnoreCase);

        var toAdd = desiredRoles.Except(currentRoles, StringComparer.OrdinalIgnoreCase).ToList();
        var toRemove = currentRoles.Except(desiredRoles, StringComparer.OrdinalIgnoreCase).ToList();

        bool ok = true;
        foreach (var name in toAdd)
        {
            if (!allRoles.TryGetValue(name, out var role))
            {
                errors.Add($"Role '{name}' not found in target environment — skipping.");
                ok = false;
                continue;
            }
            try { await _remoting.WriteAsync(m => m.UserService.AddUserToRole(role.Id, user.Id), ct).ConfigureAwait(false); }
            catch (Exception ex) when (IsBenignRoleFault(ex))
            {
                // inriver rejects adding the connected/current user to a role, and re-adding a role the
                // user already has. Neither is a real failure — the desired end state is satisfied — so
                // treat it as a skip rather than surfacing a scary error (this is what made the bulk
                // grant "crash" for the user).
                _log.LogInformation("Skipped adding '{Username}' to role '{Role}': {Reason}", username, name, ex.Message);
            }
            catch (Exception ex) { errors.Add($"Add to role '{name}' failed: {ex.Message}"); ok = false; }
        }
        foreach (var name in toRemove)
        {
            if (!allRoles.TryGetValue(name, out var role)) continue;
            try { await _remoting.WriteAsync(m => m.UserService.RemoveUserFromRole(role.Id, user.Id), ct).ConfigureAwait(false); }
            catch (Exception ex) when (IsBenignRoleFault(ex))
            {
                _log.LogInformation("Skipped removing '{Username}' from role '{Role}': {Reason}", username, name, ex.Message);
            }
            catch (Exception ex) { errors.Add($"Remove from role '{name}' failed: {ex.Message}"); ok = false; }
        }
        return ok;
    }

    /// <summary>
    /// True for role-membership faults that mean "already in the desired state" rather than a real
    /// error: inriver throws <see cref="ArgumentException"/> "Trying to add current User to a Role"
    /// for the connected user, and complains when the user is already (not) a member. Matched on
    /// message text because Remoting surfaces these as plain exceptions with no typed code.
    /// </summary>
    private static bool IsBenignRoleFault(Exception ex)
    {
        var m = ex.Message;
        return m.Contains("current User", StringComparison.OrdinalIgnoreCase)
            || m.Contains("already", StringComparison.OrdinalIgnoreCase);
    }
}

/// <summary>Lightweight user record returned by <see cref="UserProvisioning.ListUsers"/>.</summary>
public sealed class UserSummary
{
    public int Id { get; set; }
    public string Username { get; set; } = "";
    public string? Email { get; set; }
    public string? FirstName { get; set; }
    public string? LastName { get; set; }
    public bool Active { get; set; }
    public List<string> Roles { get; set; } = [];
}
