using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using ModelMeister.Inriver.Snapshot;
using ModelMeister.Model.Primitives;
using ModelMeister.Ui.Models;

namespace ModelMeister.Ui.ViewModels;

/// <summary>
/// View-model behind the Create / Edit CVL dialog. Edits the CVL definition (id, datatype, parent,
/// custom-value-list flag) and stages its value rows in an inline-editable sub-grid (add / edit /
/// delete / select-all). Changes are staged in memory; on confirm the caller (CvlWorkbenchViewModel)
/// creates/updates the CVL, upserts every value row, and deletes any value whose key was removed
/// (diffed against <see cref="OriginalKeys"/>).
/// </summary>
public partial class CvlEditorViewModel : ViewModelBase
{
    public CvlEditorViewModel(
        bool isEdit, string id, CvlDataType dataType, string? parentId, bool customValueList,
        IReadOnlyList<LiveCvlValue> values, IReadOnlyList<string> availableCvlIds)
    {
        IsEdit = isEdit;
        _id = id;
        _dataType = dataType;
        _customValueList = customValueList;

        AvailableParents.Add(NoneParent);
        foreach (var c in availableCvlIds
                     .Where(c => !string.Equals(c, id, StringComparison.OrdinalIgnoreCase))
                     .OrderBy(c => c, StringComparer.OrdinalIgnoreCase))
            AvailableParents.Add(c);
        _parentId = string.IsNullOrEmpty(parentId) ? NoneParent : parentId;

        foreach (var v in values.OrderBy(v => v.Index))
            Values.Add(new CvlValueEditRow(v.Key, v.Value?.DefaultValue ?? "", v.ParentKey ?? "", v.Index, v.Deactivated));
        OriginalKeys = values.Select(v => v.Key).ToHashSet(StringComparer.OrdinalIgnoreCase);

        Selection = new RowSelectionModel(Values);
    }

    private const string NoneParent = "(none)";

    public bool IsEdit { get; }
    public string Title => IsEdit ? $"Edit CVL · {Id}" : "New CVL";

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(ConfirmCommand))]
    private string _id;

    [ObservableProperty] private CvlDataType _dataType;
    [ObservableProperty] private string _parentId;
    [ObservableProperty] private bool _customValueList;
    [ObservableProperty] private string _validation = "";

    /// <summary>All CVL datatypes for the datatype dropdown.</summary>
    public IReadOnlyList<CvlDataType> DataTypes { get; } = Enum.GetValues<CvlDataType>();

    /// <summary>Candidate parent CVL ids ("(none)" + every other CVL id).</summary>
    public ObservableCollection<string> AvailableParents { get; } = [];

    public ObservableCollection<CvlValueEditRow> Values { get; } = [];
    public RowSelectionModel Selection { get; }

    /// <summary>Keys present when the dialog opened — diffed on save to find deletes.</summary>
    public IReadOnlyCollection<string> OriginalKeys { get; }

    /// <summary>The chosen parent id, or null when "(none)".</summary>
    public string? ResolvedParentId =>
        string.IsNullOrWhiteSpace(ParentId) || string.Equals(ParentId, NoneParent, StringComparison.Ordinal) ? null : ParentId;

    [RelayCommand]
    private void AddValue()
    {
        var nextIndex = Values.Count == 0 ? 0 : Values.Max(v => v.Index) + 1;
        Values.Add(new CvlValueEditRow("", "", "", nextIndex, false));
    }

    [RelayCommand]
    private void DeleteValue(CvlValueEditRow? row)
    {
        if (row is not null) Values.Remove(row);
    }

    [RelayCommand]
    private void DeleteSelectedValues()
    {
        foreach (var r in Selection.SelectedOf<CvlValueEditRow>()) Values.Remove(r);
    }

    /// <summary>Flag every checked value active. Staged in memory; persisted by the Save upsert loop.</summary>
    [RelayCommand]
    private void ActivateSelectedValues()
    {
        foreach (var r in Selection.SelectedOf<CvlValueEditRow>()) r.Deactivated = false;
    }

    /// <summary>Flag every checked value deactivated. Staged in memory; persisted by the Save upsert loop.</summary>
    [RelayCommand]
    private void DeactivateSelectedValues()
    {
        foreach (var r in Selection.SelectedOf<CvlValueEditRow>()) r.Deactivated = true;
    }

    public bool? Result { get; private set; }
    public event Action? Closed;

    private bool CanConfirm() => !string.IsNullOrWhiteSpace(Id);

    [RelayCommand(CanExecute = nameof(CanConfirm))]
    private void Confirm()
    {
        if (string.IsNullOrWhiteSpace(Id)) { Validation = "CVL id is required."; return; }
        if (Values.Any(v => string.IsNullOrWhiteSpace(v.Key))) { Validation = "Every value needs a key."; return; }
        var dupes = Values.GroupBy(v => v.Key, StringComparer.OrdinalIgnoreCase)
                          .Where(g => g.Count() > 1).Select(g => g.Key).ToList();
        if (dupes.Count > 0) { Validation = "Duplicate value keys: " + string.Join(", ", dupes); return; }
        Validation = "";
        Result = true;
        Closed?.Invoke();
    }

    [RelayCommand]
    private void Abort()
    {
        Result = false;
        Closed?.Invoke();
    }
}

/// <summary>One inline-editable CVL value row in the editor's value sub-grid.</summary>
public partial class CvlValueEditRow : SelectableRow
{
    public CvlValueEditRow(string key, string value, string parentKey, int index, bool deactivated)
    {
        _key = key;
        _value = value;
        _parentKey = parentKey;
        _index = index;
        _deactivated = deactivated;
    }

    [ObservableProperty] private string _key;
    [ObservableProperty] private string _value;
    [ObservableProperty] private string _parentKey;
    [ObservableProperty] private int _index;
    [ObservableProperty] private bool _deactivated;
}
