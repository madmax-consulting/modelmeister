using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using ModelMeister.Excel;
using ModelMeister.Inriver.WorkAreas;
using ModelMeister.Ui.Services;

namespace ModelMeister.Ui.ViewModels;

/// <summary>
/// Work-area page view-model. Presents the connected env's shared work-area folders as a tree, with
/// single-env CRUD (new folder / sub-folder, rename, delete), Excel export/import, and a detail pane
/// showing a selected query folder's saved search. Operational config — does NOT route through the
/// model diff/apply pipeline; it talks to the env directly like Users/Extensions.
/// </summary>
public partial class WorkAreaViewModel : FeaturePageViewModel
{
    private readonly MainWindowViewModel _main;
    private readonly Shell _shell;
    private readonly IAppLog _log;

    /// <inheritdoc/>
    public override bool SupportsCompare => true;
    /// <inheritdoc/>
    public override BackupScope BackupScope => BackupScope.WorkAreas;
    /// <inheritdoc/>
    public override ExcelCapability Excel => ExcelCapability.ExportImport;

    /// <inheritdoc/>
    public override async Task BackupAsync()
    {
        if (!_main.IsConnected) { _log.Toast(LogLevel.Warn, "Backup", "Connect first."); return; }
        try
        {
            var path = await _main.Backups.CaptureWorkAreasAsync().ConfigureAwait(true);
            _log.Success("Backup", $"Work-areas backup saved → {path}");
            _log.Toast(LogLevel.Success, "Work-areas backup saved", Path.GetFileName(path));
        }
        catch (Exception ex)
        {
            _log.Error("Backup", $"Work-areas backup failed: {ex.Message}", ex);
            _log.Toast(LogLevel.Error, "Backup failed", ex.Message);
        }
    }

    /// <summary>Root nodes of the folder tree.</summary>
    public ObservableCollection<WorkAreaNode> Tree { get; } = [];

    /// <summary>Flat DTO list backing the current tree — used for Excel export.</summary>
    private IReadOnlyList<WorkAreaFolderDto> _flat = [];

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(NewFolderCommand))]
    [NotifyCanExecuteChangedFor(nameof(NewSubfolderCommand))]
    [NotifyCanExecuteChangedFor(nameof(RenameCommand))]
    [NotifyCanExecuteChangedFor(nameof(DeleteCommand))]
    private bool _busy;

    [ObservableProperty] private string _status = "";

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasSelection))]
    [NotifyPropertyChangedFor(nameof(SelectedQueryPretty))]
    [NotifyPropertyChangedFor(nameof(HasQuery))]
    [NotifyCanExecuteChangedFor(nameof(NewSubfolderCommand))]
    [NotifyCanExecuteChangedFor(nameof(RenameCommand))]
    [NotifyCanExecuteChangedFor(nameof(DeleteCommand))]
    private WorkAreaNode? _selected;

    public bool HasSelection => Selected is not null;
    public bool HasQuery => Selected?.IsQuery == true && !string.IsNullOrWhiteSpace(Selected.QueryJson);

    /// <summary>Pretty-printed (re-indented) query JSON for the detail pane.</summary>
    public string SelectedQueryPretty => HasQuery ? PrettyJson(Selected!.QueryJson!) : "";

    public WorkAreaViewModel(MainWindowViewModel main, Shell shell, IAppLog log)
    {
        _main = main;
        _shell = shell;
        _log = log;
        _main.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(MainWindowViewModel.IsConnected))
            {
                MarkDataDirty();
                if (_main.IsConnected) _ = EnsureLoadedAsync();
                NewFolderCommand.NotifyCanExecuteChanged();
            }
        };
    }

    private bool CanMutate() => !Busy && _main.IsConnected;
    private bool CanActOnSelection() => !Busy && _main.IsConnected && Selected is not null;

    /// <inheritdoc/>
    public override async Task RefreshAsync()
    {
        if (!_main.IsConnected) { Status = "Connect to an environment first."; return; }
        Busy = true;
        try
        {
            var folders = await _shell.ListWorkAreasAsync().ConfigureAwait(true);
            _flat = folders;
            var selectedPath = Selected?.Path;
            BuildTree(folders);
            // Re-select the previously-selected folder by path so a refresh after a rename/edit keeps focus.
            if (selectedPath is not null) Selected = FindByPath(Tree, selectedPath);
            var queries = folders.Count(f => f.IsQuery);
            Status = folders.Count == 0
                ? "No shared work-area folders."
                : $"{folders.Count} folder(s) · {queries} saved {(queries == 1 ? "query" : "queries")}";
        }
        catch (Exception ex)
        {
            Status = "Failed: " + ex.Message;
            _log.Error("WorkAreas", ex.Message, ex);
        }
        finally { Busy = false; }
    }

    /// <summary>Build the folder hierarchy from the flat DTO list (by <c>ParentId</c>, ordered by Index then Name).</summary>
    private void BuildTree(IReadOnlyList<WorkAreaFolderDto> folders)
    {
        var nodes = folders.ToDictionary(f => f.Id, f => new WorkAreaNode(f));
        Tree.Clear();
        foreach (var f in folders.OrderBy(f => f.Index).ThenBy(f => f.Name, StringComparer.OrdinalIgnoreCase))
        {
            var node = nodes[f.Id];
            if (f.ParentId is { } pid && nodes.TryGetValue(pid, out var parent))
                parent.Children.Add(node);
            else
                Tree.Add(node);
        }
    }

    private static WorkAreaNode? FindByPath(IEnumerable<WorkAreaNode> nodes, string path)
    {
        foreach (var n in nodes)
        {
            if (string.Equals(n.Path, path, StringComparison.OrdinalIgnoreCase)) return n;
            if (FindByPath(n.Children, path) is { } hit) return hit;
        }
        return null;
    }

    // ----- CRUD -----

    /// <summary>Create a new top-level folder.</summary>
    [RelayCommand(CanExecute = nameof(CanMutate))]
    private async Task NewFolderAsync()
    {
        var name = await DialogHost.PromptTextAsync(
            "New work-area folder", "Folder name", watermark: "e.g. Marketing", confirmLabel: "Create").ConfigureAwait(true);
        if (string.IsNullOrWhiteSpace(name)) return;
        await CreateFolderAsync(name, parent: null).ConfigureAwait(true);
    }

    /// <summary>Create a folder under the selected folder.</summary>
    [RelayCommand(CanExecute = nameof(CanActOnSelection))]
    private async Task NewSubfolderAsync()
    {
        if (Selected is null) return;
        var name = await DialogHost.PromptTextAsync(
            $"New folder under '{Selected.Name}'", "Folder name", watermark: "e.g. Launch 2026", confirmLabel: "Create").ConfigureAwait(true);
        if (string.IsNullOrWhiteSpace(name)) return;
        await CreateFolderAsync(name, Selected).ConfigureAwait(true);
    }

    private async Task CreateFolderAsync(string name, WorkAreaNode? parent)
    {
        Busy = true;
        Status = $"Creating '{name}'…";
        try
        {
            var siblings = parent?.Children.Count ?? Tree.Count;
            await _shell.CreateWorkAreaFolderAsync(name.Trim(), parent?.Id, siblings, isQuery: false).ConfigureAwait(true);
            _log.Success("WorkAreas", $"Created folder '{name}'{(parent is null ? "" : $" under '{parent.Path}'")}.");
            MarkDataDirty();
            await RefreshAsync().ConfigureAwait(true);
        }
        catch (Exception ex) { Status = "Failed: " + ex.Message; _log.Error("WorkAreas", ex.Message, ex); }
        finally { Busy = false; }
    }

    /// <summary>Rename the selected folder.</summary>
    [RelayCommand(CanExecute = nameof(CanActOnSelection))]
    private async Task RenameAsync()
    {
        if (Selected is null) return;
        var name = await DialogHost.PromptTextAsync(
            "Rename folder", "Folder name", initial: Selected.Name, confirmLabel: "Rename").ConfigureAwait(true);
        if (string.IsNullOrWhiteSpace(name) || name == Selected.Name) return;
        Busy = true;
        Status = $"Renaming to '{name}'…";
        try
        {
            await _shell.RenameWorkAreaFolderAsync(Selected.Id, name.Trim()).ConfigureAwait(true);
            _log.Success("WorkAreas", $"Renamed '{Selected.Name}' → '{name}'.");
            MarkDataDirty();
            await RefreshAsync().ConfigureAwait(true);
        }
        catch (Exception ex) { Status = "Failed: " + ex.Message; _log.Error("WorkAreas", ex.Message, ex); }
        finally { Busy = false; }
    }

    /// <summary>Delete the selected folder (and everything under it) after confirmation.</summary>
    [RelayCommand(CanExecute = nameof(CanActOnSelection))]
    private async Task DeleteAsync()
    {
        if (Selected is null) return;
        var descendants = CountDescendants(Selected);
        var detail = descendants > 0
            ? $"This also deletes {descendants} folder(s) nested under it."
            : "";
        var ok = await DialogHost.ConfirmBulkAsync(
            "Delete work-area folder", "Delete", "folder",
            new[] { Selected.Path + (descendants > 0 ? $"  (+{descendants} nested)" : "") },
            _main.ConnectedEnv?.Name, _main.ConnectedEnv?.Stage ?? Models.EnvironmentStage.Unspecified).ConfigureAwait(true);
        if (!ok) return;
        Busy = true;
        Status = $"Deleting '{Selected.Name}'… {detail}";
        try
        {
            await _shell.DeleteWorkAreaFolderAsync(Selected.Id).ConfigureAwait(true);
            _log.Success("WorkAreas", $"Deleted folder '{Selected.Path}'.");
            Selected = null;
            MarkDataDirty();
            await RefreshAsync().ConfigureAwait(true);
        }
        catch (Exception ex) { Status = "Failed: " + ex.Message; _log.Error("WorkAreas", ex.Message, ex); }
        finally { Busy = false; }
    }

    private static int CountDescendants(WorkAreaNode node) =>
        node.Children.Count + node.Children.Sum(CountDescendants);

    [RelayCommand] private Task CopyPath(WorkAreaNode? n) => ClipboardHelpers.CopyAsync(n?.Path);
    [RelayCommand] private Task CopyQuery(WorkAreaNode? n) => ClipboardHelpers.CopyAsync(n?.QueryJson);

    // ----- Excel -----

    /// <inheritdoc/>
    public override async Task ExportExcelAsync()
    {
        if (!_main.IsConnected) { Status = "Connect to an environment first."; return; }
        var path = await FilePickerHelpers.PickSaveAsync("Save work-areas workbook", "workareas.xlsx", "xlsx").ConfigureAwait(true);
        if (path is null) return;
        Busy = true;
        try
        {
            // Re-read so the export matches what inriver holds right now, not a stale tree.
            var folders = await _shell.ListWorkAreasAsync().ConfigureAwait(true);
            await Task.Run(() => WorkAreaWorkbook.Save(folders, path)).ConfigureAwait(true);
            Status = $"Wrote {Path.GetFileName(path)} · {folders.Count} folder(s)";
            _log.Success("WorkAreas", $"Exported {folders.Count} folder(s) → {path}");
        }
        catch (Exception ex) { Status = "Failed: " + ex.Message; _log.Error("WorkAreas", ex.Message, ex); }
        finally { Busy = false; }
    }

    /// <inheritdoc/>
    public override async Task ImportExcelAsync()
    {
        if (!_main.IsConnected) { Status = "Connect to an environment first."; return; }
        var plan = new ModelMeister.Ui.Services.Import.Plans.WorkAreasImportPlan(_main, _shell, _log);
        var ran = await DialogHost.ShowImportWorkflowAsync(
            plan, _log, _main.Settings.Current.RecentWorkbookPaths).ConfigureAwait(true);
        if (!ran) return;
        RememberWorkbook(_main.Settings, plan.LastWorkbookPath);
        MarkDataDirty();
        await RefreshAsync().ConfigureAwait(true);
    }

    private static string PrettyJson(string compact)
    {
        try
        {
            using var doc = JsonDocument.Parse(compact);
            return JsonSerializer.Serialize(doc.RootElement, new JsonSerializerOptions { WriteIndented = true });
        }
        catch { return compact; }
    }
}

/// <summary>One node in the work-area folder tree. Wraps a <see cref="WorkAreaFolderDto"/> and owns its
/// child nodes so the Avalonia <c>TreeView</c> can bind hierarchically.</summary>
public sealed class WorkAreaNode
{
    public WorkAreaNode(WorkAreaFolderDto dto)
    {
        Dto = dto;
        Children = [];
    }

    public WorkAreaFolderDto Dto { get; }
    public Guid Id => Dto.Id;
    public string Name => Dto.Name;
    public string Path => Dto.Path;
    public bool IsQuery => Dto.IsQuery;
    public bool IsSyndication => Dto.IsSyndication;
    public string? QueryJson => Dto.QueryJson;

    /// <summary>Short badge describing what kind of folder this is (drives the tree row chip).</summary>
    public string Kind => IsSyndication ? "syndication" : IsQuery ? "query" : "folder";

    public ObservableCollection<WorkAreaNode> Children { get; }
}
