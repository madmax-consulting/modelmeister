using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using ModelMeister.Inriver.Backup;
using ModelMeister.Ui.Models;

namespace ModelMeister.Ui.Services;

/// <summary>One restored item's outcome, shaped for the shared <c>ProvisionResultViewModel</c> table.</summary>
public sealed record RestoreOutcome(string Name, string Outcome, string Detail);

/// <summary>
/// Restores a saved backup snapshot back into the <b>currently-connected</b> environment — the
/// counterpart to <see cref="BackupService"/>. Restore always targets the live connection (the
/// Remoting singleton allows one at a time), so the UI confirms against the connected env's name +
/// stage. Scoped backups (Users / Roles / Restricted fields / Server settings / Extensions) restore
/// through the matching <c>Shell.Restore*Async</c> wrapper; a Full snapshot restores its non-model
/// slices here and exposes the model slice path for the caller to route through the model
/// Compare/Apply workflow (model "restore" is a diff/apply, not an upsert).
/// </summary>
public sealed class RestoreService
{
    private readonly Shell _shell;
    private readonly IEnvironmentVault _vault;

    public RestoreService(Shell shell, IEnvironmentVault vault)
    {
        _shell = shell;
        _vault = vault;
    }

    /// <summary>Load a backup off-thread and list the item labels it would restore — drives the
    /// itemized destructive confirmation (same prompt deletes use).</summary>
    public Task<IReadOnlyList<string>> DescribeItemsAsync(BackupFileInfo info, CancellationToken ct = default)
        => Task.Run<IReadOnlyList<string>>(() => info.Scope switch
        {
            "Users"            => UsersBackup.Load(info.Path).Users.Select(u => u.Username).ToList(),
            "Roles"            => RolesBackup.Load(info.Path).Roles.Select(r => r.Name).ToList(),
            "RestrictedFields" => RestrictedFieldsBackup.Load(info.Path).RestrictedFields.Select(RestrictedFieldLabel).ToList(),
            "ServerSettings"   => ServerSettingsBackup.Load(info.Path).Settings.Keys.ToList(),
            "Extensions"       => ExtensionsBackup.Load(info.Path).Extensions.Select(e => e.Id).ToList(),
            "WorkAreas"        => WorkAreasBackup.Load(info.Path).Folders.Select(f => f.Path).ToList(),
            "HtmlTemplates"    => HtmlTemplatesBackup.Load(info.Path).Templates.Select(t => t.Name).ToList(),
            "Full"             => DescribeFull(info.Path),
            _                  => new List<string>(),
        }, ct);

    /// <summary>Restore <paramref name="info"/> into <paramref name="env"/> (the connected env).
    /// Returns one outcome per item. For a Full snapshot the model slice is skipped here — see
    /// <see cref="ModelSlicePath"/>.</summary>
    public async Task<IReadOnlyList<RestoreOutcome>> RestoreAsync(BackupFileInfo info, EnvironmentEntry env, CancellationToken ct = default)
    {
        var secret = _vault.GetSecret(env.Id);
        switch (info.Scope)
        {
            case "Users":
            {
                var backup = await Task.Run(() => UsersBackup.Load(info.Path), ct).ConfigureAwait(false);
                var results = await _shell.RestoreUsersAsync(backup, secret, env, ct).ConfigureAwait(false);
                return results.Select(r => new RestoreOutcome(
                    r.Username,
                    r.Errors.Count > 0 ? "error" : (r.Created ? "created" : "updated"),
                    r.Errors.Count > 0 ? string.Join(" · ", r.Errors) : (r.RolesAssigned ? "roles assigned" : "ok"))).ToList();
            }
            case "Roles":
            {
                var backup = await Task.Run(() => RolesBackup.Load(info.Path), ct).ConfigureAwait(false);
                var results = await _shell.RestoreRolesAsync(backup, ct).ConfigureAwait(false);
                return results.Select(r => new RestoreOutcome(
                    r.RoleName,
                    r.Errors.Count > 0 ? "error" : (r.Created ? "created" : "updated"),
                    r.Errors.Count > 0 ? string.Join(" · ", r.Errors) : (r.PermissionsSynced ? "permissions synced" : "ok"))).ToList();
            }
            case "RestrictedFields":
            {
                var backup = await Task.Run(() => RestrictedFieldsBackup.Load(info.Path), ct).ConfigureAwait(false);
                var results = await _shell.RestoreRestrictedFieldsAsync(backup, ct).ConfigureAwait(false);
                return results.Select(r => new RestoreOutcome(
                    r.NaturalKey,
                    r.Errors.Count > 0 ? "error" : (r.Created ? "created" : "skipped"),
                    r.Errors.Count > 0 ? string.Join(" · ", r.Errors) : (r.Created ? "added" : "already present"))).ToList();
            }
            case "ServerSettings":
            {
                var backup = await Task.Run(() => ServerSettingsBackup.Load(info.Path), ct).ConfigureAwait(false);
                var results = await _shell.RestoreServerSettingsAsync(backup, dryRun: false, ct).ConfigureAwait(false);
                return results.Select(r => new RestoreOutcome(
                    r.Key,
                    r.Error is not null ? "error" : (r.Written ? "set" : "unchanged"),
                    r.Error ?? (r.Written ? "written" : "no change"))).ToList();
            }
            case "Extensions":
            {
                var backup = await Task.Run(() => ExtensionsBackup.Load(info.Path), ct).ConfigureAwait(false);
                var results = await _shell.RestoreExtensionsAsync(backup, env, secret, dryRun: false, ct).ConfigureAwait(false);
                return results.Select(r => new RestoreOutcome(
                    r.Id,
                    r.Ok ? r.Op : "error",
                    r.Error ?? r.Op)).ToList();
            }
            case "WorkAreas":
            {
                var backup = await Task.Run(() => WorkAreasBackup.Load(info.Path), ct).ConfigureAwait(false);
                var results = await _shell.RestoreWorkAreasAsync(backup, ct).ConfigureAwait(false);
                return results.Select(r => new RestoreOutcome(
                    r.Path, r.Ok ? r.Op : "error", r.Error ?? r.Op)).ToList();
            }
            case "HtmlTemplates":
            {
                var backup = await Task.Run(() => HtmlTemplatesBackup.Load(info.Path), ct).ConfigureAwait(false);
                var results = await _shell.RestoreHtmlTemplatesAsync(backup, ct).ConfigureAwait(false);
                return results.Select(r => new RestoreOutcome(
                    r.Name, r.Ok ? r.Op : "error", r.Error ?? r.Op)).ToList();
            }
            case "Full":
                return await RestoreFullAsync(info.Path, env, secret, ct).ConfigureAwait(false);
            default:
                return new List<RestoreOutcome>();
        }
    }

    /// <summary>For a Full snapshot, the path to its <c>model.json</c> slice (or <c>null</c> if absent).
    /// The caller routes this through the model Compare/Apply workflow rather than an in-place write.</summary>
    public string? ModelSlicePath(BackupFileInfo info)
    {
        if (info.Scope != "Full") return null;
        try { return FullBackup.Open(info.Path).PathFor(BackupSlice.Model); }
        catch { return null; }
    }

    private async Task<IReadOnlyList<RestoreOutcome>> RestoreFullAsync(
        string folder, EnvironmentEntry env, EnvironmentSecret? secret, CancellationToken ct)
    {
        var full = FullBackup.Open(folder);
        var outcomes = new List<RestoreOutcome>();

        if (full.PathFor(BackupSlice.Users) is { } usersPath)
        {
            var backup = await Task.Run(() => UsersBackup.Load(usersPath), ct).ConfigureAwait(false);
            var results = await _shell.RestoreUsersAsync(backup, secret, env, ct).ConfigureAwait(false);
            outcomes.AddRange(results.Select(r => new RestoreOutcome(
                $"user: {r.Username}", r.Errors.Count > 0 ? "error" : (r.Created ? "created" : "updated"),
                r.Errors.Count > 0 ? string.Join(" · ", r.Errors) : "ok")));
        }
        if (full.PathFor(BackupSlice.ServerSettings) is { } settingsPath)
        {
            var backup = await Task.Run(() => ServerSettingsBackup.Load(settingsPath), ct).ConfigureAwait(false);
            var results = await _shell.RestoreServerSettingsAsync(backup, dryRun: false, ct).ConfigureAwait(false);
            outcomes.AddRange(results.Select(r => new RestoreOutcome(
                $"setting: {r.Key}", r.Error is not null ? "error" : (r.Written ? "set" : "unchanged"), r.Error ?? "ok")));
        }
        if (full.PathFor(BackupSlice.Extensions) is { } extensionsPath)
        {
            var backup = await Task.Run(() => ExtensionsBackup.Load(extensionsPath), ct).ConfigureAwait(false);
            var results = await _shell.RestoreExtensionsAsync(backup, env, secret, dryRun: false, ct).ConfigureAwait(false);
            outcomes.AddRange(results.Select(r => new RestoreOutcome(
                $"extension: {r.Id}", r.Ok ? r.Op : "error", r.Error ?? r.Op)));
        }
        if (full.PathFor(BackupSlice.Model) is not null)
            outcomes.Add(new RestoreOutcome("model", "deferred",
                "Load into Compare to review/apply the reverse change set."));

        return outcomes;
    }

    private static List<string> DescribeFull(string folder)
    {
        var full = FullBackup.Open(folder);
        var items = new List<string>();
        foreach (var slice in full.Slices.Keys.OrderBy(s => s.ToString()))
            items.Add(slice == BackupSlice.Model ? "Model (review via Compare)" : slice.ToString());
        return items;
    }

    private static string RestrictedFieldLabel(RestrictedFieldsBackup.Entry e) =>
        $"{e.RoleName} · {e.RestrictionType} · {e.EntityTypeId}{(string.IsNullOrEmpty(e.FieldTypeId) ? "" : "/" + e.FieldTypeId)}{(string.IsNullOrEmpty(e.CategoryId) ? "" : " [" + e.CategoryId + "]")}";
}
