using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using ModelMeister.Inriver.Diff;
using ModelMeister.Ui.Services;

namespace ModelMeister.Ui.ViewModels;

/// <summary>
/// Owns the merge-policy toggles that gate destructive Compare/Apply behaviour. Sits as its own
/// workflow step between Model and Compare so the user explicitly reviews what they will allow
/// before any diff is computed. Persists every toggle change to <see cref="AppSettings"/>.
/// </summary>
public partial class PolicyViewModel : ViewModelBase
{
    /// <summary>Field-type property tokens the differ checks — the order shown in settings.</summary>
    public static readonly IReadOnlyList<string> KnownFieldProperties =
    [
        "Name", "Description", "Mandatory", "Unique", "ReadOnly", "Hidden", "MultiValue",
        "IsDisplayName", "IsDisplayDescription", "SupportsExpression", "Category", "Index",
        "TrackChanges", "ExcludeFromDefaultView", "DefaultValue", "Cvl", "DefaultExpression",
    ];

    private readonly MainWindowViewModel _main;
    private readonly ISettingsStore _settings;
    private bool _initialized;

    /// <summary>Allow updates to Name / Description fields when they differ.</summary>
    [ObservableProperty] private bool _overwriteNamesAndDescriptions;
    /// <summary>Allow updates to CVL value labels.</summary>
    [ObservableProperty] private bool _overwriteCvlValues;
    /// <summary>Allow Delete / Deactivate operations.</summary>
    [ObservableProperty] private bool _allowDeletes;
    /// <summary>Allow field datatype changes (typically destructive).</summary>
    [ObservableProperty] private bool _allowDatatypeChange;
    /// <summary>Allow renaming a CVL value's key (migrate references).</summary>
    [ObservableProperty] private bool _allowCvlValueRename;

    /// <summary>Per-property "ignore differences" toggles (one per <see cref="KnownFieldProperties"/>).</summary>
    public IReadOnlyList<FieldPropertyIgnoreToggle> IgnorableProperties { get; }

    /// <summary>Editable field-id ignore rules (contains / starts-with / ends-with).</summary>
    public ObservableCollection<FieldIdRuleRow> FieldIdRules { get; } = new();

    /// <summary>Match kinds offered in the field-id rule editor.</summary>
    public IReadOnlyList<FieldIdMatchKind> MatchKinds { get; } =
        Enum.GetValues<FieldIdMatchKind>();

    public PolicyViewModel(MainWindowViewModel main, ISettingsStore settings)
    {
        _main = main;
        _settings = settings;

        var s = settings.Current;
        _overwriteNamesAndDescriptions = s.OverwriteNamesAndDescriptions;
        _overwriteCvlValues = s.OverwriteCvlValues;
        _allowDeletes = s.AllowDeletes;
        _allowDatatypeChange = s.AllowDatatypeChange;
        _allowCvlValueRename = s.AllowCvlValueRename;

        IgnorableProperties = KnownFieldProperties
            .Select(p => new FieldPropertyIgnoreToggle(
                p,
                s.IgnoredFieldProperties.Contains(p, StringComparer.OrdinalIgnoreCase),
                SyncIgnoreRules))
            .ToArray();

        foreach (var r in s.IgnoredFieldIdPatterns)
            FieldIdRules.Add(new FieldIdRuleRow(r.Kind, r.Value, SyncIgnoreRules));

        _initialized = true;
    }

    /// <summary>Stable signature of the current toggle + ignore-rule state, used by <see cref="DiffViewModel"/>
    /// to anchor a captured changeset so changes after the fact invalidate it.</summary>
    internal string Signature()
    {
        var props = string.Join(",", IgnorableProperties.Where(p => p.Ignored).Select(p => p.Property));
        var ids = string.Join(",", FieldIdRules.Select(r => $"{r.Kind}:{r.Value}"));
        return $"{OverwriteNamesAndDescriptions}|{OverwriteCvlValues}|{AllowDeletes}|{AllowDatatypeChange}|{AllowCvlValueRename}|{props}|{ids}";
    }

    /// <summary>The current toggle + ignore-rule state captured as a <see cref="MergePolicy"/>.</summary>
    public MergePolicy CurrentPolicy => new()
    {
        OverwriteNamesAndDescriptions = OverwriteNamesAndDescriptions,
        OverwriteCvlValues = OverwriteCvlValues,
        AllowDeletes = AllowDeletes,
        AllowDatatypeChange = AllowDatatypeChange,
        AllowCvlValueRename = AllowCvlValueRename,
        IgnoredFieldProperties = IgnorableProperties.Where(p => p.Ignored).Select(p => p.Property).ToArray(),
        IgnoredFieldIdPatterns = FieldIdRules
            .Where(r => !string.IsNullOrWhiteSpace(r.Value))
            .Select(r => new FieldIdIgnoreRule(r.Kind, r.Value.Trim()))
            .ToArray(),
    };

    /// <summary>Short summary of which switches are on, for display on the Compare page.</summary>
    public string Summary
    {
        get
        {
            var flags = new[]
            {
                (OverwriteNamesAndDescriptions, "names+desc"),
                (OverwriteCvlValues, "CVL values"),
                (AllowDeletes, "deletes"),
                (AllowDatatypeChange, "datatype"),
                (AllowCvlValueRename, "CVL rename"),
            };
            var on = flags.Where(f => f.Item1).Select(f => f.Item2).ToList();

            var ignoredProps = IgnorableProperties.Count(p => p.Ignored);
            var ignoredIds = FieldIdRules.Count(r => !string.IsNullOrWhiteSpace(r.Value));
            var ignore = new List<string>();
            if (ignoredProps > 0) ignore.Add($"{ignoredProps} field prop(s)");
            if (ignoredIds > 0) ignore.Add($"{ignoredIds} id rule(s)");

            var parts = new List<string>();
            parts.Add(on.Count == 0 ? "Conservative — no destructive switches enabled." : "Allow: " + string.Join(", ", on));
            if (ignore.Count > 0) parts.Add("Ignore: " + string.Join(", ", ignore));
            return string.Join("   ", parts);
        }
    }

    /// <summary>Add a blank field-id ignore rule for the user to fill in.</summary>
    [RelayCommand]
    private void AddFieldIdRule()
    {
        FieldIdRules.Add(new FieldIdRuleRow(FieldIdMatchKind.Contains, "", SyncIgnoreRules));
        SyncIgnoreRules();
    }

    /// <summary>Remove a field-id ignore rule.</summary>
    [RelayCommand]
    private void RemoveFieldIdRule(FieldIdRuleRow? row)
    {
        if (row is null) return;
        FieldIdRules.Remove(row);
        SyncIgnoreRules();
    }

    /// <summary>Mirror the current ignore-rule UI state into settings, then persist + invalidate.</summary>
    private void SyncIgnoreRules()
    {
        if (!_initialized) return;
        _settings.Current.IgnoredFieldProperties =
            IgnorableProperties.Where(p => p.Ignored).Select(p => p.Property).ToList();
        _settings.Current.IgnoredFieldIdPatterns = FieldIdRules
            .Where(r => !string.IsNullOrWhiteSpace(r.Value))
            .Select(r => new FieldIdIgnoreRule(r.Kind, r.Value.Trim()))
            .ToList();
        PersistAndNotify();
    }

    partial void OnOverwriteNamesAndDescriptionsChanged(bool value) { _settings.Current.OverwriteNamesAndDescriptions = value; PersistAndNotify(); }
    partial void OnOverwriteCvlValuesChanged(bool value)            { _settings.Current.OverwriteCvlValues = value;            PersistAndNotify(); }
    partial void OnAllowDeletesChanged(bool value)                  { _settings.Current.AllowDeletes = value;                  PersistAndNotify(); }
    partial void OnAllowDatatypeChangeChanged(bool value)           { _settings.Current.AllowDatatypeChange = value;           PersistAndNotify(); }
    partial void OnAllowCvlValueRenameChanged(bool value)           { _settings.Current.AllowCvlValueRename = value;           PersistAndNotify(); }

    private void PersistAndNotify()
    {
        // Guard against firing during construction — toggles are set from saved settings before
        // _initialized flips true, and at that point there's no diff to invalidate yet.
        if (!_initialized) return;
        _settings.Save();
        OnPropertyChanged(nameof(Summary));
        _main.NotifyPolicyChanged();
        _main.InvalidateChangeSet("Policy changed");
    }
}

/// <summary>A single "ignore differences for this field property" checkbox row.</summary>
public partial class FieldPropertyIgnoreToggle : ObservableObject
{
    private readonly Action _onChanged;

    public FieldPropertyIgnoreToggle(string property, bool ignored, Action onChanged)
    {
        Property = property;
        _ignored = ignored;
        _onChanged = onChanged;
    }

    /// <summary>The field-type property name (matches the tokens checked by the differ).</summary>
    public string Property { get; }

    [ObservableProperty] private bool _ignored;

    partial void OnIgnoredChanged(bool value) => _onChanged();
}

/// <summary>An editable field-id ignore rule (match kind + value).</summary>
public partial class FieldIdRuleRow : ObservableObject
{
    private readonly Action _onChanged;

    public FieldIdRuleRow(FieldIdMatchKind kind, string value, Action onChanged)
    {
        _kind = kind;
        _value = value ?? "";
        _onChanged = onChanged;
    }

    [ObservableProperty] private FieldIdMatchKind _kind;
    [ObservableProperty] private string _value;

    partial void OnKindChanged(FieldIdMatchKind value) => _onChanged();
    partial void OnValueChanged(string value) => _onChanged();
}
