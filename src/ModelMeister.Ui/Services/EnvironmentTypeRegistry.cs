using System;
using System.Collections.Generic;
using System.Linq;
using Avalonia.Media;
using ModelMeister.Ui.Models;

namespace ModelMeister.Ui.Services;

/// <summary>
/// Single source of truth for <see cref="EnvironmentType"/> definitions. Seeds the seven built-in
/// types (keyed by the legacy <see cref="EnvironmentStage"/> names, so migration is an identity map)
/// and overlays the user's persisted edits + custom types from <see cref="AppSettings.EnvironmentTypes"/>.
/// Consumed by the pill converters, the env editor, the types-management page, and the protected-env
/// guard. A process-wide <see cref="Current"/> reference lets the value-converters (which can't take
/// constructor injection) resolve type keys.
/// </summary>
public interface IEnvironmentTypeRegistry
{
    /// <summary>Built-ins + custom types, ordered by sort order then name.</summary>
    IReadOnlyList<EnvironmentType> All { get; }

    /// <summary>Resolve a type by key; never null — falls back to the built-in "Unspecified" type.</summary>
    EnvironmentType Resolve(string? key);

    /// <summary>True when the type for <paramref name="key"/> is marked protected (drives the safety banner).</summary>
    bool IsProtected(string? key);

    /// <summary>True when any persisted environment is currently assigned the given type key.</summary>
    bool IsInUse(string key);

    /// <summary>Create a custom type or update an existing one (built-in or custom). Persists + raises <see cref="Changed"/>.</summary>
    void Upsert(EnvironmentType type);

    /// <summary>Delete a custom type. Built-ins are never deleted (no-op). Persists + raises <see cref="Changed"/>.</summary>
    void Delete(string key);

    /// <summary>Raised after the set of types changes (Upsert / Delete).</summary>
    event Action? Changed;
}

/// <summary>Settings-backed <see cref="IEnvironmentTypeRegistry"/>.</summary>
public sealed class EnvironmentTypeRegistry : IEnvironmentTypeRegistry
{
    /// <summary>Key of the neutral fallback type assigned when an environment has no explicit type.</summary>
    public const string UnspecifiedKey = "Unspecified";

    private readonly ISettingsStore _settings;
    private readonly IEnvironmentVault? _vault;
    private readonly List<EnvironmentType> _all = new();

    /// <summary>Process-wide instance the value-converters resolve against. Set at startup (and by tests).</summary>
    public static IEnvironmentTypeRegistry? Current { get; set; }

    /// <inheritdoc/>
    public event Action? Changed;

    public EnvironmentTypeRegistry(ISettingsStore settings, IEnvironmentVault? vault = null)
    {
        _settings = settings;
        _vault = vault;
        Rebuild();
    }

    /// <inheritdoc/>
    public IReadOnlyList<EnvironmentType> All => _all;

    /// <inheritdoc/>
    public EnvironmentType Resolve(string? key)
    {
        if (!string.IsNullOrEmpty(key))
        {
            var hit = _all.FirstOrDefault(t => string.Equals(t.Key, key, StringComparison.Ordinal));
            if (hit is not null) return hit;
        }
        return _all.FirstOrDefault(t => t.Key == UnspecifiedKey) ?? Defaults()[0];
    }

    /// <inheritdoc/>
    public bool IsProtected(string? key) => Resolve(key).IsProtected;

    /// <inheritdoc/>
    public bool IsInUse(string key)
        => _vault?.List().Any(e => string.Equals(e.TypeKey, key, StringComparison.Ordinal)) ?? false;

    /// <inheritdoc/>
    public void Upsert(EnvironmentType type)
    {
        if (string.IsNullOrWhiteSpace(type.Key)) type.Key = NewKey(type.Name);

        var existing = _all.FirstOrDefault(t => string.Equals(t.Key, type.Key, StringComparison.Ordinal));
        if (existing is not null)
        {
            existing.Name = type.Name;
            existing.Shorthand = type.Shorthand;
            existing.Description = type.Description;
            existing.ColorHex = type.ColorHex;
            existing.IsProtected = type.IsProtected;
            // IsBuiltIn and SortOrder are intentionally preserved.
        }
        else
        {
            type.IsBuiltIn = false;
            if (type.SortOrder == 0) type.SortOrder = _all.Count == 0 ? 0 : _all.Max(t => t.SortOrder) + 1;
            _all.Add(type);
        }

        Sort();
        Persist();
        Changed?.Invoke();
    }

    /// <inheritdoc/>
    public void Delete(string key)
    {
        var existing = _all.FirstOrDefault(t => string.Equals(t.Key, key, StringComparison.Ordinal));
        if (existing is null || existing.IsBuiltIn) return;
        _all.Remove(existing);
        Persist();
        Changed?.Invoke();
    }

    private void Rebuild()
    {
        _all.Clear();
        var persisted = _settings.Current.EnvironmentTypes ?? new List<EnvironmentType>();

        foreach (var d in Defaults())
        {
            var overlay = persisted.FirstOrDefault(p => string.Equals(p.Key, d.Key, StringComparison.Ordinal));
            if (overlay is not null)
            {
                d.Name = overlay.Name;
                d.Shorthand = overlay.Shorthand;
                d.Description = overlay.Description;
                d.ColorHex = overlay.ColorHex;
                d.IsProtected = overlay.IsProtected;
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
        // Only persist custom types + built-ins the user actually changed, so future default tweaks
        // still flow through to anyone who never touched them.
        _settings.Current.EnvironmentTypes = _all
            .Where(t => !t.IsBuiltIn || IsModifiedBuiltIn(t, defaults))
            .ToList();
        _settings.Save();
    }

    private static bool IsModifiedBuiltIn(EnvironmentType t, IDictionary<string, EnvironmentType> defaults)
    {
        if (!defaults.TryGetValue(t.Key, out var d)) return true;
        return t.Name != d.Name
            || t.Shorthand != d.Shorthand
            || (t.Description ?? "") != (d.Description ?? "")
            || t.ColorHex != d.ColorHex
            || t.IsProtected != d.IsProtected
            || t.SortOrder != d.SortOrder;
    }

    private void Sort()
        => _all.Sort((a, b) => a.SortOrder != b.SortOrder
            ? a.SortOrder.CompareTo(b.SortOrder)
            : string.Compare(a.Name, b.Name, StringComparison.OrdinalIgnoreCase));

    private string NewKey(string? name)
    {
        var baseSlug = new string((name ?? "").Where(char.IsLetterOrDigit).ToArray());
        if (string.IsNullOrEmpty(baseSlug)) baseSlug = "type";
        var key = baseSlug;
        var n = 1;
        while (_all.Any(t => string.Equals(t.Key, key, StringComparison.OrdinalIgnoreCase)))
            key = baseSlug + ++n;
        return key;
    }

    /// <summary>Fresh instances of the seven shipped types. Colors match the legacy light-theme stage
    /// brushes; only <c>Prod</c> is protected (preserving today's banner-on-Prod behavior).</summary>
    private static List<EnvironmentType> Defaults() => new()
    {
        new() { Key = "Unspecified", Name = "Unspecified", Shorthand = "ENV",   Description = "No specific classification.",     ColorHex = "#6B7280", IsProtected = false, IsBuiltIn = true, SortOrder = 0 },
        new() { Key = "Dev",         Name = "Development",  Shorthand = "DEV",   Description = "Developer sandbox.",              ColorHex = "#1F6FE8", IsProtected = false, IsBuiltIn = true, SortOrder = 1 },
        new() { Key = "Test",        Name = "Test",         Shorthand = "TEST",  Description = "Integration / test environment.", ColorHex = "#C97B0A", IsProtected = false, IsBuiltIn = true, SortOrder = 2 },
        new() { Key = "QA",          Name = "QA",           Shorthand = "QA",    Description = "Quality-assurance environment.",  ColorHex = "#C97B0A", IsProtected = false, IsBuiltIn = true, SortOrder = 3 },
        new() { Key = "UAT",         Name = "UAT",          Shorthand = "UAT",   Description = "User-acceptance testing.",        ColorHex = "#C97B0A", IsProtected = false, IsBuiltIn = true, SortOrder = 4 },
        new() { Key = "Stage",       Name = "Staging",      Shorthand = "STAGE", Description = "Pre-production staging.",          ColorHex = "#C0392B", IsProtected = false, IsBuiltIn = true, SortOrder = 5 },
        new() { Key = "Prod",        Name = "Production",   Shorthand = "PROD",  Description = "Live production environment.",     ColorHex = "#C0392B", IsProtected = true,  IsBuiltIn = true, SortOrder = 6 },
    };
}

/// <summary>
/// Derives the two pill brushes from a single hex color: the strong color for text/border and a
/// translucent variant for the background (which reads correctly over both light and dark surfaces).
/// Invalid/empty hex falls back to a neutral gray rather than throwing.
/// </summary>
public static class EnvironmentTypeColors
{
    private static readonly Color Fallback = Color.Parse("#6B7280");

    public static Color ToColor(string? hex)
        => !string.IsNullOrWhiteSpace(hex) && Color.TryParse(hex, out var c) ? c : Fallback;

    public static IBrush Strong(string? hex) => new SolidColorBrush(ToColor(hex));

    public static IBrush Soft(string? hex)
    {
        var c = ToColor(hex);
        return new SolidColorBrush(Color.FromArgb(0x22, c.R, c.G, c.B)); // ~13% alpha
    }
}
