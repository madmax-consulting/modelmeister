using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Platform.Storage;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using ModelMeister.Inriver.Diff;
using ModelMeister.Inriver.Reporting;
using ModelMeister.Inriver.Snapshot;
using ModelMeister.Ui.Services;

namespace ModelMeister.Ui.ViewModels;

/// <summary>
/// View-model for the Compare page. Runs the diff against either a live snapshot or an offline
/// JSON, exposes the diff as a Concept→Operation→Row tree, and tracks which rows the user has
/// chosen to exclude from the upcoming Apply. Merge-policy toggles live on <see cref="PolicyViewModel"/>.
/// </summary>
public partial class DiffViewModel : ViewModelBase
{
    private static readonly string[] OperationOrder = { "Add", "Update", "Delete", "Other" };

    private readonly MainWindowViewModel _main;
    private readonly ISettingsStore _settings;
    private readonly Shell _shell;
    private readonly IAppLog _log;

    /// <summary>True while a snapshot is being captured / diff being computed.</summary>
    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(CompareCommand))]
    private bool _busy;
    /// <summary>Short status message at the bottom of the page.</summary>
    [ObservableProperty] private string _statusMessage = "Load a model and connect to an environment, then click Compare.";

    /// <summary>One-line summary of the merge policy currently configured on the Policy page.</summary>
    public string PolicySummary => _main.PolicyVm.Summary;

    /// <summary>Raised by <see cref="MainWindowViewModel"/> when a policy toggle changes elsewhere.</summary>
    internal void OnPolicyChanged() => OnPropertyChanged(nameof(PolicySummary));

    /// <summary>Number of Add operations in the current diff.</summary>
    [ObservableProperty] private int _adds;
    /// <summary>Number of Update/Change operations in the current diff.</summary>
    [ObservableProperty] private int _updates;
    /// <summary>Number of Delete/Remove/Deactivate operations in the current diff.</summary>
    [ObservableProperty] private int _deletes;
    /// <summary>Number of advisory warnings emitted alongside the change set.</summary>
    [ObservableProperty] private int _warnings;
    /// <summary>True when the diff produced at least one change.</summary>
    [ObservableProperty] private bool _hasChanges;
    /// <summary>Human-readable text rendering of the diff (used by Copy/Export).</summary>
    [ObservableProperty] private string _diffText = "";
    /// <summary>Substring filter applied to <see cref="Tree"/>; matches against row Description.</summary>
    [ObservableProperty] private string _treeFilter = "";

    /// <summary>Currently selected item in the tree (a <see cref="ChangeRow"/> drives the details pane).</summary>
    [ObservableProperty] private object? _selectedTreeItem;
    /// <summary>Title of the details pane (current row's Description).</summary>
    [ObservableProperty] private string _selectedTitle = "";
    /// <summary>Subtitle of the details pane (current row's Kind).</summary>
    [ObservableProperty] private string _selectedSubtitle = "";
    /// <summary>True when the details pane should be visible at all.</summary>
    [ObservableProperty] private bool _hasDetails;
    /// <summary>True when <see cref="SelectedDeltas"/> has at least one entry.</summary>
    [ObservableProperty] private bool _hasDeltas;

    private readonly List<ConceptNode> _allConcepts = new();
    private readonly HashSet<ChangeRow> _excludedRows = new();

    /// <summary>Top-level concept buckets ("CVLs", "Entity types", ...). Bound to a TreeView.</summary>
    public ObservableCollection<ConceptNode> Tree { get; } = [];
    /// <summary>Advisory warnings (non-failing) emitted by the differ.</summary>
    public ObservableCollection<WarningRow> WarningRows { get; } = [];
    /// <summary>Per-property deltas for the selected Update change.</summary>
    public ObservableCollection<PropertyDelta> SelectedDeltas { get; } = [];

    /// <summary>Number of explicitly skipped changes (subtracted when applying).</summary>
    [ObservableProperty] private int _excludedCount;

    /// <summary>Changes that the user has not skipped on the diff tree. Apply uses this.</summary>
    public IReadOnlyList<ModelChange> EffectiveChanges()
    {
        var set = _main.ChangeSet;
        if (set is null) return [];
        if (_excludedRows.Count == 0) return set.Changes;

        var excluded = _excludedRows.Select(r => r.Change).ToHashSet();
        return set.Changes.Where(c => !excluded.Contains(c)).ToList();
    }

    partial void OnSelectedTreeItemChanged(object? value)
    {
        SelectedDeltas.Clear();

        if (value is not ChangeRow row || _main.LiveSnapshot is null)
        {
            SelectedTitle = "";
            SelectedSubtitle = "";
            HasDetails = false;
            HasDeltas = false;
            return;
        }

        SelectedTitle = row.Description;
        SelectedSubtitle = row.Kind;
        foreach (var d in ChangeDetails.For(row.Change, _main.LiveSnapshot, CurrentPolicy))
            SelectedDeltas.Add(d);
        HasDetails = true;
        HasDeltas = SelectedDeltas.Count > 0;
    }

    partial void OnTreeFilterChanged(string value) => ApplyTreeFilter();

    public DiffViewModel(MainWindowViewModel main, ISettingsStore settings, Shell shell, IAppLog log)
    {
        _main = main;
        _settings = settings;
        _shell = shell;
        _log = log;

        // Re-evaluate CanCompare whenever its upstream gates change on the hub.
        _main.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName is nameof(MainWindowViewModel.IsConnected)
                                or nameof(MainWindowViewModel.LoadedModel))
                CompareCommand.NotifyCanExecuteChanged();
        };
    }

    /// <summary>The merge policy from <see cref="PolicyViewModel"/>, used by the differ and apply confirmation.</summary>
    public MergePolicy CurrentPolicy => _main.PolicyVm.CurrentPolicy;

    private bool CanCompare() => !Busy && _main.LoadedModel is not null && _main.IsConnected;

    [RelayCommand(CanExecute = nameof(CanCompare))]
    private async Task CompareAsync()
    {
        Busy = true;
        ResetState();

        try
        {
            StatusMessage = "Capturing live snapshot…";
            _log.Info("Compare", "Capturing live snapshot…");
            var live = await _shell.CaptureSnapshotAsync().ConfigureAwait(true);
            _main.LiveSnapshot = live;

            StatusMessage = "Computing diff…";
            var changes = _shell.ComputeDiff(_main.LoadedModel!, live, CurrentPolicy);
            _main.ChangeSet = changes;
            _main.StampChangeSetAnchors(live, _main.PolicyVm.Signature());
            // Reaching a successful Compare implies the user is done configuring Policy. Mark it
            // done so the workflow strip reflects that even if they skipped clicking Next on Policy.
            _main.IsPolicyDone = true;
            Populate(changes);

            StatusMessage = HasChanges
                ? $"{Adds} adds, {Updates} updates, {Deletes} deletes, {Warnings} warnings."
                : "In sync — no changes required.";

            if (HasChanges)
                _log.Info("Compare", $"{Adds} adds · {Updates} updates · {Deletes} deletes · {Warnings} warnings.");
            else
                _log.Success("Compare", "In sync — no changes required.");
        }
        catch (Exception ex)
        {
            StatusMessage = $"Compare failed: {ex.Message}";
            _log.Error("Compare", ex.Message, ex);
            _log.Toast(LogLevel.Error, "Compare failed", ex.Message);
        }
        finally
        {
            Busy = false;
        }
    }

    /// <summary>Diff against a snapshot loaded from JSON instead of a live env (used by History/Restore flows).</summary>
    public async Task CompareWithSnapshotJsonAsync(string snapshotJsonPath)
    {
        if (_main.LoadedModel is null) { StatusMessage = "Load a model first."; return; }
        Busy = true;
        ResetState();
        try
        {
            var live = await _shell.LoadSnapshotJsonAsync(snapshotJsonPath).ConfigureAwait(true);
            _main.LiveSnapshot = live;

            var changes = _shell.ComputeDiff(_main.LoadedModel, live, CurrentPolicy);
            _main.ChangeSet = changes;
            _main.StampChangeSetAnchors(live, _main.PolicyVm.Signature());
            _main.IsPolicyDone = true;
            Populate(changes);

            StatusMessage = HasChanges
                ? $"(offline) {Adds} adds, {Updates} updates, {Deletes} deletes."
                : "(offline) In sync — no changes.";
        }
        catch (Exception ex)
        {
            StatusMessage = $"Offline compare failed: {ex.Message}";
        }
        finally
        {
            Busy = false;
        }
    }

    [RelayCommand] private void GoApply() => _main.GoTo(NavTarget.Apply);

    [RelayCommand]
    private void ExpandAll() => SetAllExpanded(true);

    [RelayCommand]
    private void CollapseAll() => SetAllExpanded(false);

    private void SetAllExpanded(bool expanded)
    {
        foreach (var concept in Tree)
        {
            concept.IsExpanded = expanded;
            foreach (var op in concept.Operations) op.IsExpanded = expanded;
        }
    }

    [RelayCommand]
    private void ClearTreeFilter() => TreeFilter = "";

    [RelayCommand]
    private void IncludeAll()
    {
        foreach (var row in _excludedRows.ToList()) row.IsExcluded = false;
        _excludedRows.Clear();
        ExcludedCount = 0;
        RecomputeDiffText();
    }

    /// <summary>Right-click "Enable all" on a tree node. Sets every leaf under <paramref name="node"/> to included.</summary>
    [RelayCommand]
    private void EnableNode(object? node) => SetNodeExcluded(node, excluded: false);

    /// <summary>Right-click "Disable all" on a tree node. Sets every leaf under <paramref name="node"/> to excluded.</summary>
    [RelayCommand]
    private void DisableNode(object? node) => SetNodeExcluded(node, excluded: true);

    private void SetNodeExcluded(object? node, bool excluded)
    {
        switch (node)
        {
            case ChangeRow row:
                SetExcluded(row, excluded);
                break;
            case OperationNode op:
                foreach (var r in op.Items) SetExcluded(r, excluded);
                break;
            case ConceptNode concept:
                foreach (var op in concept.Operations)
                    foreach (var r in op.Items) SetExcluded(r, excluded);
                break;
            default: return;
        }
        ExcludedCount = _excludedRows.Count;
        RecomputeDiffText();
    }

    /// <summary>Toggle a single row's excluded state from the tree row's checkbox/click.
    /// Excluding (or re-including) an <c>AddEntityType</c> cascades to every <c>AddFieldType</c>
    /// row belonging to the same entity — fields can't be added to an entity that isn't being created.</summary>
    public void ToggleExcluded(ChangeRow row)
    {
        SetExcluded(row, !row.IsExcluded);

        if (row.Change is AddEntityType addEt)
        {
            var ownerId = addEt.EntityType.EntityTypeId;
            foreach (var dep in AllRows())
            {
                if (dep.Change is AddFieldType aft && aft.Owner.EntityTypeId == ownerId)
                    SetExcluded(dep, row.IsExcluded);
            }
        }

        ExcludedCount = _excludedRows.Count;
        RecomputeDiffText();
    }

    private void SetExcluded(ChangeRow row, bool excluded)
    {
        if (row.IsExcluded == excluded) return;
        row.IsExcluded = excluded;
        if (excluded) _excludedRows.Add(row);
        else _excludedRows.Remove(row);
    }

    private IEnumerable<ChangeRow> AllRows()
    {
        foreach (var concept in _allConcepts)
            foreach (var op in concept.Operations)
                foreach (var r in op.Items)
                    yield return r;
    }

    /// <summary>Re-render <see cref="DiffText"/> against the currently-included changes so
    /// toggling rows in the tree updates the "Diff as text" pane.</summary>
    private void RecomputeDiffText()
    {
        var set = _main.ChangeSet;
        if (set is null) { DiffText = ""; return; }
        var effective = new ModelChangeSet
        {
            Changes = EffectiveChanges().ToList(),
            Warnings = set.Warnings,
        };
        DiffText = ChangeReport.ToText(effective);
    }

    [RelayCommand]
    private async Task CopyDiffAsync()
    {
        var clipboard = MainWindowOrNull()?.Clipboard;
        if (clipboard is null || string.IsNullOrEmpty(DiffText)) return;
        await clipboard.SetTextAsync(DiffText);
        _log.Info("Compare", "Diff text copied to clipboard.");
    }

    [RelayCommand]
    private async Task ExportDiffAsync()
    {
        var window = MainWindowOrNull();
        if (window is null) return;

        var pick = await window.StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
        {
            Title = "Save diff as text",
            SuggestedFileName = "diff.txt",
            DefaultExtension = "txt",
        });

        var path = pick?.TryGetLocalPath();
        if (string.IsNullOrEmpty(path)) return;

        await File.WriteAllTextAsync(path, DiffText);
        _log.Success("Compare", $"Diff exported to {path}");
    }

    private void ResetState()
    {
        Tree.Clear();
        _allConcepts.Clear();
        _excludedRows.Clear();
        ExcludedCount = 0;
        WarningRows.Clear();
        SelectedDeltas.Clear();
        SelectedTreeItem = null;
        HasDetails = false;
    }

    private void Populate(ModelChangeSet set)
    {
        Adds = set.Changes.Count(c => OperationOf(c) == "Add");
        Updates = set.Changes.Count(c => OperationOf(c) == "Update");
        Deletes = set.Changes.Count(c => OperationOf(c) == "Delete");
        Warnings = set.Warnings.Count;
        HasChanges = !set.IsEmpty;

        var byConcept = new Dictionary<string, ConceptNode>();
        foreach (var change in set.Changes)
        {
            var concept = ConceptOf(change);
            if (!byConcept.TryGetValue(concept, out var node))
            {
                node = new ConceptNode(concept);
                byConcept[concept] = node;
                _allConcepts.Add(node);
            }
            node.Add(change);
        }
        foreach (var node in _allConcepts) node.Finalise();
        ApplyTreeFilter();

        foreach (var w in set.Warnings) WarningRows.Add(new WarningRow(w.Code, w.Message));

        DiffText = ChangeReport.ToText(set);
    }

    private void ApplyTreeFilter()
    {
        Tree.Clear();
        var query = (TreeFilter ?? "").Trim();
        var rendered = string.IsNullOrEmpty(query)
            ? _allConcepts.Cast<ConceptNode?>()
            : _allConcepts.Select(c => c.WithFilter(query));

        foreach (var node in rendered)
        {
            if (node is not null) Tree.Add(node);
        }
    }

    private static string ConceptOf(ModelChange c) => c switch
    {
        AddLanguage or DeactivateCvlValue or AddCvlValue or UpdateCvlValue           => "CVL values & languages",
        AddCategory or UpdateCategory or DeleteCategory                              => "Categories",
        AddCvl or UpdateCvl or DeleteCvl                                             => "CVLs",
        AddEntityType or UpdateEntityType or DeleteEntityType                        => "Entity types",
        AddFieldType or UpdateFieldType or DeleteFieldType or ChangeFieldDatatype    => "Fields",
        AddFieldset or UpdateFieldset or DeleteFieldset
            or AddFieldToFieldset or RemoveFieldFromFieldset                         => "Fieldsets",
        AddLinkType or UpdateLinkType or DeleteLinkType                              => "Link types",
        AddRole or UpdateRole or DeleteRole
            or AddPermissionToRole or RemovePermissionFromRole                       => "Roles & permissions",
        AddRestrictedFieldPermission or RemoveRestrictedFieldPermission              => "Restricted permissions",
        AddCompletenessDefinition or UpdateCompletenessDefinition
            or DeleteCompletenessDefinition                                         => "Completeness",
        _                                                                            => "Other",
    };

    /// <summary>
    /// Classify <paramref name="c"/> into its semantic bucket ("Add"/"Update"/"Delete"/"Other") by
    /// inspecting the generated change-type's name. Exposed publicly so <see cref="ConceptNode"/>
    /// can call it from outside the VM.
    /// </summary>
    public static string OperationOf(ModelChange c)
    {
        var n = c.GetType().Name;
        if (n.StartsWith("Add", StringComparison.Ordinal)) return "Add";
        if (n.StartsWith("Update", StringComparison.Ordinal)
            || n.StartsWith("Change", StringComparison.Ordinal)) return "Update";
        if (n.StartsWith("Delete", StringComparison.Ordinal)
            || n.StartsWith("Remove", StringComparison.Ordinal)
            || n.StartsWith("Deactivate", StringComparison.Ordinal)) return "Delete";
        return "Other";
    }

    private static Avalonia.Controls.Window? MainWindowOrNull()
        => Application.Current?.ApplicationLifetime is IClassicDesktopStyleApplicationLifetime d
            ? d.MainWindow
            : null;

    internal static IReadOnlyList<string> OperationDisplayOrder => OperationOrder;
}

/// <summary>One concept bucket in the diff tree (e.g. "CVLs", "Entity types"). Holds Add/Update/Delete sub-buckets.</summary>
public partial class ConceptNode : ObservableObject
{
    public ConceptNode(string title) { Title = title; }
    public string Title { get; }
    public ObservableCollection<OperationNode> Operations { get; } = [];
    public int Count => Operations.Sum(o => o.Items.Count);
    public string Header => $"{Title}  ({Count})";
    public string Display => Header;
    [ObservableProperty] private bool _isExpanded = true;

    /// <summary>Append a change to the matching operation bucket, creating it on first sight.</summary>
    public void Add(ModelChange change)
    {
        var op = DiffViewModel.OperationOf(change);
        var bucket = Operations.FirstOrDefault(o => o.Operation == op);
        if (bucket is null)
        {
            bucket = new OperationNode(op);
            Operations.Add(bucket);
        }
        bucket.Items.Add(new ChangeRow(change));
    }

    /// <summary>Reorder the operation buckets to a canonical Add &#8594; Update &#8594; Delete &#8594; Other order.</summary>
    public void Finalise()
    {
        var sorted = Operations.OrderBy(o => Array.IndexOf(DiffViewModel.OperationDisplayOrder.ToArray(), o.Operation)).ToList();
        Operations.Clear();
        foreach (var op in sorted) Operations.Add(op);
    }

    /// <summary>
    /// Returns a filtered clone of this node where only rows whose Description contains <paramref name="filter"/>
    /// survive, and operation buckets without surviving rows are dropped. Returns null if nothing matches.
    /// </summary>
    public ConceptNode? WithFilter(string filter)
    {
        var clone = new ConceptNode(Title) { IsExpanded = true };
        foreach (var op in Operations)
        {
            var keep = op.Items.Where(r => r.Description.Contains(filter, StringComparison.OrdinalIgnoreCase)).ToList();
            if (keep.Count == 0) continue;

            var opClone = new OperationNode(op.Operation) { IsExpanded = true };
            foreach (var r in keep) opClone.Items.Add(r);
            clone.Operations.Add(opClone);
        }
        return clone.Operations.Count == 0 ? null : clone;
    }
}

/// <summary>One operation bucket (Add/Update/Delete/Other) under a <see cref="ConceptNode"/>.</summary>
public partial class OperationNode : ObservableObject
{
    public OperationNode(string op) { Operation = op; }
    public string Operation { get; }
    public ObservableCollection<ChangeRow> Items { get; } = [];
    public string Header => $"{Operation}  ({Items.Count})";
    [ObservableProperty] private bool _isExpanded = true;
}

/// <summary>Leaf row in the diff tree. <see cref="IsExcluded"/> is toggled by the per-row checkbox.</summary>
public partial class ChangeRow : ObservableObject
{
    public ChangeRow(ModelChange change)
    {
        Change = change;
        Operation = DiffViewModel.OperationOf(change);
        Description = change.Describe();
        Kind = change.GetType().Name;
    }
    public ModelChange Change { get; }
    public string Operation { get; }
    public string Description { get; }
    public string Kind { get; }
    [ObservableProperty] private bool _isExcluded;
}

/// <summary>One advisory warning row from the differ.</summary>
public sealed record WarningRow(string Code, string Message);
