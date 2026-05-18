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

        _initialized = true;
    }

    /// <summary>Stable signature of the current toggle state, used by <see cref="DiffViewModel"/>
    /// to anchor a captured changeset so toggle changes after the fact invalidate it.</summary>
    internal string Signature() =>
        $"{OverwriteNamesAndDescriptions}|{OverwriteCvlValues}|{AllowDeletes}|{AllowDatatypeChange}|{AllowCvlValueRename}";

    /// <summary>The current toggle state captured as a <see cref="MergePolicy"/> for the differ/applier.</summary>
    public MergePolicy CurrentPolicy => new()
    {
        OverwriteNamesAndDescriptions = OverwriteNamesAndDescriptions,
        OverwriteCvlValues = OverwriteCvlValues,
        AllowDeletes = AllowDeletes,
        AllowDatatypeChange = AllowDatatypeChange,
        AllowCvlValueRename = AllowCvlValueRename,
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
            var on = flags.Where(f => f.Item1).Select(f => f.Item2).ToArray();
            return on.Length == 0 ? "Conservative — no destructive switches enabled." : "Allow: " + string.Join(", ", on);
        }
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
