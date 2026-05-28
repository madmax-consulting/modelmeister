using System;
using System.Collections.Generic;
using System.Linq;
using ModelMeister.Ui.Models;

namespace ModelMeister.Ui.Services;

/// <summary>
/// Single source of truth for <see cref="Organization"/> definitions. Seeds the one built-in "Default"
/// organization and overlays the user's persisted edits + custom organizations from
/// <see cref="AppSettings.Organizations"/>. Consumed by the organizations-management page, the env
/// editor, and the global organization picker. An environment whose <see cref="EnvironmentEntry.OrgKey"/>
/// is unset resolves to "Default", so existing environments migrate with no vault rewrite.
/// </summary>
public interface IOrganizationRegistry
{
    /// <summary>Built-in + custom organizations, ordered by sort order then name.</summary>
    IReadOnlyList<Organization> All { get; }

    /// <summary>Resolve an organization by key; never null — falls back to the built-in "Default".</summary>
    Organization Resolve(string? key);

    /// <summary>True when any persisted environment is currently assigned the given organization key.
    /// An environment with an unset key counts as belonging to "Default", so the Default organization is
    /// reported in-use whenever any unassigned environment exists.</summary>
    bool IsInUse(string key);

    /// <summary>Create a custom organization or update an existing one (built-in or custom). Persists +
    /// raises <see cref="Changed"/>.</summary>
    void Upsert(Organization org);

    /// <summary>Delete an organization. The built-in "Default" is a starter and may be deleted too — the
    /// deletion is tombstoned so it survives a restart. Persists + raises <see cref="Changed"/>.</summary>
    void Delete(string key);

    /// <summary>Raised after the set of organizations changes (Upsert / Delete).</summary>
    event Action? Changed;
}

/// <summary>Settings-backed <see cref="IOrganizationRegistry"/>.</summary>
public sealed class OrganizationRegistry : IOrganizationRegistry
{
    /// <summary>Key of the built-in organization assigned (by read-through) to environments with no explicit org.</summary>
    public const string DefaultKey = "Default";

    private readonly ISettingsStore _settings;
    private readonly IEnvironmentVault? _vault;
    private readonly List<Organization> _all = new();

    /// <inheritdoc/>
    public event Action? Changed;

    public OrganizationRegistry(ISettingsStore settings, IEnvironmentVault? vault = null)
    {
        _settings = settings;
        _vault = vault;
        Rebuild();
    }

    /// <inheritdoc/>
    public IReadOnlyList<Organization> All => _all;

    /// <inheritdoc/>
    public Organization Resolve(string? key)
    {
        if (!string.IsNullOrEmpty(key))
        {
            var hit = _all.FirstOrDefault(o => string.Equals(o.Key, key, StringComparison.Ordinal));
            if (hit is not null) return hit;
        }
        return _all.FirstOrDefault(o => o.Key == DefaultKey) ?? Defaults()[0];
    }

    /// <inheritdoc/>
    public bool IsInUse(string key)
    {
        if (_vault is null) return false;
        return _vault.List().Any(e =>
            string.Equals(Resolve(e.OrgKey).Key, key, StringComparison.Ordinal));
    }

    /// <inheritdoc/>
    public void Upsert(Organization org)
    {
        if (string.IsNullOrWhiteSpace(org.Key)) org.Key = NewKey(org.Name);

        var existing = _all.FirstOrDefault(o => string.Equals(o.Key, org.Key, StringComparison.Ordinal));
        if (existing is not null)
        {
            existing.Name = org.Name;
            existing.Description = org.Description;
            // IsBuiltIn and SortOrder are intentionally preserved.
        }
        else
        {
            org.IsBuiltIn = false;
            if (org.SortOrder == 0) org.SortOrder = _all.Count == 0 ? 0 : _all.Max(o => o.SortOrder) + 1;
            _all.Add(org);
        }

        Sort();
        Persist();
        Changed?.Invoke();
    }

    /// <inheritdoc/>
    public void Delete(string key)
    {
        var existing = _all.FirstOrDefault(o => string.Equals(o.Key, key, StringComparison.Ordinal));
        if (existing is null) return;
        _all.Remove(existing);
        if (existing.IsBuiltIn)
        {
            // The built-in is re-seeded from Defaults() on every Rebuild — tombstone the key so the
            // deletion sticks across restarts.
            var tombstones = _settings.Current.DeletedBuiltInOrgKeys ??= new List<string>();
            if (!tombstones.Contains(existing.Key, StringComparer.Ordinal))
                tombstones.Add(existing.Key);
        }
        Persist();
        Changed?.Invoke();
    }

    private void Rebuild()
    {
        _all.Clear();
        var persisted = _settings.Current.Organizations ?? new List<Organization>();
        var deleted = _settings.Current.DeletedBuiltInOrgKeys ?? new List<string>();

        foreach (var d in Defaults())
        {
            // The user deleted this built-in — don't re-seed it.
            if (deleted.Contains(d.Key, StringComparer.Ordinal)) continue;

            var overlay = persisted.FirstOrDefault(p => string.Equals(p.Key, d.Key, StringComparison.Ordinal));
            if (overlay is not null)
            {
                d.Name = overlay.Name;
                d.Description = overlay.Description;
                if (overlay.SortOrder != 0) d.SortOrder = overlay.SortOrder;
            }
            _all.Add(d);
        }

        var builtInKeys = Defaults().Select(d => d.Key).ToHashSet(StringComparer.Ordinal);
        foreach (var p in persisted)
        {
            if (builtInKeys.Contains(p.Key)) continue;
            p.IsBuiltIn = false;
            _all.Add(p);
        }

        Sort();
    }

    private void Persist()
    {
        var defaults = Defaults().ToDictionary(d => d.Key, StringComparer.Ordinal);
        // Only persist custom organizations + the built-in if the user actually changed it, so future
        // default tweaks still flow through to anyone who never touched it.
        _settings.Current.Organizations = _all
            .Where(o => !o.IsBuiltIn || IsModifiedBuiltIn(o, defaults))
            .ToList();
        _settings.Save();
    }

    private static bool IsModifiedBuiltIn(Organization o, IDictionary<string, Organization> defaults)
    {
        if (!defaults.TryGetValue(o.Key, out var d)) return true;
        return o.Name != d.Name
            || (o.Description ?? "") != (d.Description ?? "")
            || o.SortOrder != d.SortOrder;
    }

    private void Sort()
        => _all.Sort((a, b) => a.SortOrder != b.SortOrder
            ? a.SortOrder.CompareTo(b.SortOrder)
            : string.Compare(a.Name, b.Name, StringComparison.OrdinalIgnoreCase));

    private string NewKey(string? name)
    {
        var baseSlug = new string((name ?? "").Where(char.IsLetterOrDigit).ToArray());
        if (string.IsNullOrEmpty(baseSlug)) baseSlug = "org";
        var key = baseSlug;
        var n = 1;
        while (_all.Any(o => string.Equals(o.Key, key, StringComparison.OrdinalIgnoreCase)))
            key = baseSlug + ++n;
        return key;
    }

    /// <summary>Fresh instance of the single shipped organization. Existing environments (no
    /// <see cref="EnvironmentEntry.OrgKey"/>) resolve here, so this is the migration landing spot.</summary>
    private static List<Organization> Defaults() => new()
    {
        new() { Key = DefaultKey, Name = "Default", Description = "Default organization.", IsBuiltIn = true, SortOrder = 0 },
    };
}
