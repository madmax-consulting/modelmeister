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
using ModelMeister.Inriver.Users;
using ModelMeister.Inriver.WorkAreas;
using ModelMeister.Inriver.WorkAreas.Query;
using ModelMeister.Ui.Services;

namespace ModelMeister.Ui.ViewModels;

/// <summary>
/// Work-area page view-model. Presents the connected env's shared work-area folders as a tree, with
/// single-env CRUD (new folder / sub-folder / query folder, rename, delete), reorder + re-parent
/// (move up/down, indent/outdent), a syndication toggle, a saved-search builder, Excel export/import, and a
/// detail pane showing the selected query. Operational config — does NOT route through the model diff/apply
/// pipeline; it talks to the env directly like Users/Extensions.
/// <para>The <see cref="PersonalWorkAreaViewModel"/> subclass reuses all of this against a selected user's
/// personal folders by overriding <see cref="PersonalUsername"/> (and hiding the shared-only syndication).</para>
/// </summary>
public partial class WorkAreaViewModel : FeaturePageViewModel
{
    private readonly MainWindowViewModel _main;
    private readonly Shell _shell;
    private readonly IAppLog _log;
    private QueryMetadata? _meta;

    /// <inheritdoc/>
    public override bool SupportsCompare => true;
    /// <inheritdoc/>
    public override BackupScope BackupScope => BackupScope.WorkAreas;
    /// <inheritdoc/>
    public override ExcelCapability Excel => ExcelCapability.ExportImport;

    /// <summary>Personal scope username (null = shared). Overridden by the personal page.</summary>
    protected virtual string? PersonalUsername => null;

    /// <summary>Whether the syndication flag applies in this scope (shared only). Drives toggle visibility.</summary>
    public virtual bool ShowSyndicationToggle => true;

    /// <summary>Page chrome text — overridden by the personal page.</summary>
    public virtual string WorkAreaEyebrow => "WORK AREAS";
    public virtual string WorkAreaSubtitle => "Shared folders and saved searches in the connected environment — export/import or compare across environments";
    public virtual string EmptyTitle => "No shared folders";

    /// <summary>Whether a "pick a user" dropdown is shown (personal page only). Drives picker visibility.</summary>
    public virtual bool ShowUserPicker => false;

    /// <summary>Candidate users for the personal-scope picker (empty for shared).</summary>
    public ObservableCollection<UserSummary> Users { get; } = [];

    [ObservableProperty] private UserSummary? _selectedUser;

    partial void OnSelectedUserChanged(UserSummary? value)
    {
        _meta = null;
        MarkDataDirty();
        _ = RefreshAsync();
    }

    protected MainWindowViewModel Main => _main;
    protected Shell ShellSvc => _shell;
    protected IAppLog Log => _log;

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
    [NotifyCanExecuteChangedFor(nameof(NewQueryFolderCommand))]
    [NotifyCanExecuteChangedFor(nameof(RenameCommand))]
    [NotifyCanExecuteChangedFor(nameof(DeleteCommand))]
    [NotifyCanExecuteChangedFor(nameof(EditQueryCommand))]
    [NotifyCanExecuteChangedFor(nameof(ToggleSyndicationCommand))]
    [NotifyCanExecuteChangedFor(nameof(MoveUpCommand))]
    [NotifyCanExecuteChangedFor(nameof(MoveDownCommand))]
    [NotifyCanExecuteChangedFor(nameof(IndentCommand))]
    [NotifyCanExecuteChangedFor(nameof(OutdentCommand))]
    private bool _busy;

    [ObservableProperty] private string _status = "";

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasSelection))]
    [NotifyPropertyChangedFor(nameof(SelectedQueryPretty))]
    [NotifyPropertyChangedFor(nameof(HasQuery))]
    [NotifyCanExecuteChangedFor(nameof(NewSubfolderCommand))]
    [NotifyCanExecuteChangedFor(nameof(RenameCommand))]
    [NotifyCanExecuteChangedFor(nameof(DeleteCommand))]
    [NotifyCanExecuteChangedFor(nameof(EditQueryCommand))]
    [NotifyCanExecuteChangedFor(nameof(ToggleSyndicationCommand))]
    [NotifyCanExecuteChangedFor(nameof(MoveUpCommand))]
    [NotifyCanExecuteChangedFor(nameof(MoveDownCommand))]
    [NotifyCanExecuteChangedFor(nameof(IndentCommand))]
    [NotifyCanExecuteChangedFor(nameof(OutdentCommand))]
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
                _meta = null; // re-capture builder metadata against the newly connected env
                MarkDataDirty();
                if (_main.IsConnected) _ = EnsureLoadedAsync();
                NewFolderCommand.NotifyCanExecuteChanged();
                NewQueryFolderCommand.NotifyCanExecuteChanged();
            }
        };
    }

    private bool CanMutate() => !Busy && _main.IsConnected;
    private bool CanActOnSelection() => !Busy && _main.IsConnected && Selected is not null;
    private bool CanEditQuery() => !Busy && _main.IsConnected && Selected is { IsQuery: true };
    private bool CanToggleSyndication() => !Busy && _main.IsConnected && ShowSyndicationToggle && Selected is not null;

    private bool CanMoveUp() => !Busy && _main.IsConnected && SelectedSiblingIndex() > 0;
    private bool CanMoveDown()
    {
        if (Busy || !_main.IsConnected || Selected is null) return false;
        var sibs = SiblingsOf(Selected);
        var i = sibs.IndexOf(Selected);
        return i >= 0 && i < sibs.Count - 1;
    }
    private bool CanIndent() => CanMoveUp(); // needs a preceding sibling to nest under
    private bool CanOutdent() => !Busy && _main.IsConnected && Selected?.Parent?.Parent is not null;

    /// <inheritdoc/>
    public override async Task RefreshAsync()
    {
        if (!_main.IsConnected) { Status = "Connect to an environment first."; return; }
        Busy = true;
        try
        {
            var folders = await _shell.ListWorkAreasAsync(PersonalUsername).ConfigureAwait(true);
            _flat = folders;
            var selectedPath = Selected?.Path;
            BuildTree(folders);
            // Re-select the previously-selected folder by path so a refresh after a rename/edit keeps focus.
            if (selectedPath is not null) Selected = FindByPath(Tree, selectedPath);
            var queries = folders.Count(f => f.IsQuery);
            Status = folders.Count == 0
                ? EmptyStatus
                : $"{folders.Count} folder(s) · {queries} saved {(queries == 1 ? "query" : "queries")}";
        }
        catch (Exception ex)
        {
            Status = "Failed: " + ex.Message;
            _log.Error("WorkAreas", ex.Message, ex);
        }
        finally { Busy = false; }
    }

    /// <summary>Status shown when no folders exist (overridden by the personal page).</summary>
    protected virtual string EmptyStatus => "No shared work-area folders.";

    /// <summary>Build the folder hierarchy from the flat DTO list (by <c>ParentId</c>, ordered by Index then Name).</summary>
    private void BuildTree(IReadOnlyList<WorkAreaFolderDto> folders)
    {
        var nodes = folders.ToDictionary(f => f.Id, f => new WorkAreaNode(f));
        Tree.Clear();
        foreach (var f in folders.OrderBy(f => f.Index).ThenBy(f => f.Name, StringComparer.OrdinalIgnoreCase))
        {
            var node = nodes[f.Id];
            if (f.ParentId is { } pid && nodes.TryGetValue(pid, out var parent))
            {
                node.Parent = parent;
                parent.Children.Add(node);
            }
            else
            {
                Tree.Add(node);
            }
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

    private IList<WorkAreaNode> SiblingsOf(WorkAreaNode? node) =>
        node?.Parent?.Children ?? (IList<WorkAreaNode>)Tree;

    private int SelectedSiblingIndex() => Selected is null ? -1 : SiblingsOf(Selected).IndexOf(Selected);

    // ----- CRUD -----

    /// <summary>Create a new top-level folder.</summary>
    [RelayCommand(CanExecute = nameof(CanMutate))]
    private async Task NewFolderAsync()
    {
        var name = await DialogHost.PromptTextAsync(
            "New work-area folder", "Folder name", watermark: "e.g. Marketing", confirmLabel: "Create").ConfigureAwait(true);
        if (string.IsNullOrWhiteSpace(name)) return;
        await CreateFolderAsync(name, parent: null, isQuery: false).ConfigureAwait(true);
    }

    /// <summary>Create a folder under the selected folder.</summary>
    [RelayCommand(CanExecute = nameof(CanActOnSelection))]
    private async Task NewSubfolderAsync()
    {
        if (Selected is null) return;
        var name = await DialogHost.PromptTextAsync(
            $"New folder under '{Selected.Name}'", "Folder name", watermark: "e.g. Launch 2026", confirmLabel: "Create").ConfigureAwait(true);
        if (string.IsNullOrWhiteSpace(name)) return;
        await CreateFolderAsync(name, Selected, isQuery: false).ConfigureAwait(true);
    }

    /// <summary>Create a query (saved-search) folder, then open the builder to define its query.</summary>
    [RelayCommand(CanExecute = nameof(CanMutate))]
    private async Task NewQueryFolderAsync()
    {
        var name = await DialogHost.PromptTextAsync(
            "New query folder", "Folder name", watermark: "e.g. Pending review", confirmLabel: "Create").ConfigureAwait(true);
        if (string.IsNullOrWhiteSpace(name)) return;
        var id = await CreateFolderAsync(name.Trim(), Selected, isQuery: true).ConfigureAwait(true);
        if (id is { } folderId) await OpenQueryBuilderAsync(folderId, name.Trim(), existingJson: null).ConfigureAwait(true);
    }

    private async Task<Guid?> CreateFolderAsync(string name, WorkAreaNode? parent, bool isQuery)
    {
        Busy = true;
        Status = $"Creating '{name}'…";
        try
        {
            var siblings = parent?.Children.Count ?? Tree.Count;
            var id = await _shell.CreateWorkAreaFolderAsync(name.Trim(), parent?.Id, siblings, isQuery, PersonalUsername).ConfigureAwait(true);
            _log.Success("WorkAreas", $"Created {(isQuery ? "query " : "")}folder '{name}'{(parent is null ? "" : $" under '{parent.Path}'")}.");
            MarkDataDirty();
            await RefreshAsync().ConfigureAwait(true);
            return id;
        }
        catch (Exception ex) { Status = "Failed: " + ex.Message; _log.Error("WorkAreas", ex.Message, ex); return null; }
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
            await _shell.RenameWorkAreaFolderAsync(Selected.Id, name.Trim(), PersonalUsername).ConfigureAwait(true);
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
            _main.ConnectedEnv?.Name, _main.ConnectedEnv?.TypeKey).ConfigureAwait(true);
        if (!ok) return;
        var deletedPath = Selected.Path;
        Busy = true;
        Status = $"Deleting '{Selected.Name}'… {detail}";
        try
        {
            await _shell.DeleteWorkAreaFolderAsync(Selected.Id, PersonalUsername).ConfigureAwait(true);
            _log.Success("WorkAreas", $"Deleted folder '{deletedPath}'.");
            _log.Toast(LogLevel.Success, "Folder deleted", deletedPath);
            Selected = null;
            MarkDataDirty();
            await RefreshAsync().ConfigureAwait(true);
        }
        catch (Exception ex) { Status = "Failed: " + ex.Message; _log.Error("WorkAreas", ex.Message, ex); }
        finally { Busy = false; }
    }

    private static int CountDescendants(WorkAreaNode node) =>
        node.Children.Count + node.Children.Sum(CountDescendants);

    // ----- query builder -----

    /// <summary>Open the saved-search builder for the selected query folder.</summary>
    [RelayCommand(CanExecute = nameof(CanEditQuery))]
    private async Task EditQueryAsync()
    {
        if (Selected is null) return;
        await OpenQueryBuilderAsync(Selected.Id, Selected.Name, Selected.QueryJson).ConfigureAwait(true);
    }

    private async Task OpenQueryBuilderAsync(Guid folderId, string folderName, string? existingJson)
    {
        QueryMetadata meta;
        try { meta = await EnsureMetaAsync().ConfigureAwait(true); }
        catch (Exception ex) { _log.Error("WorkAreas", $"Couldn't read model metadata: {ex.Message}", ex); meta = QueryMetadata.Empty; }

        var editor = await DialogHost.QueryEditorAsync(folderName, existingJson, meta).ConfigureAwait(true);
        if (editor is null) return;

        Busy = true;
        Status = $"Saving query on '{folderName}'…";
        try
        {
            await _shell.SetWorkAreaQueryAsync(folderId, editor.ResultJson, PersonalUsername).ConfigureAwait(true);
            _log.Success("WorkAreas", $"Saved query on '{folderName}'.");
            MarkDataDirty();
            await RefreshAsync().ConfigureAwait(true);
        }
        catch (Exception ex) { Status = "Failed: " + ex.Message; _log.Error("WorkAreas", ex.Message, ex); }
        finally { Busy = false; }
    }

    private async Task<QueryMetadata> EnsureMetaAsync()
    {
        _meta ??= await _shell.CaptureWorkAreaQueryMetadataAsync().ConfigureAwait(true);
        return _meta;
    }

    // ----- syndication -----

    /// <summary>Toggle the shared folder's syndication flag.</summary>
    [RelayCommand(CanExecute = nameof(CanToggleSyndication))]
    private async Task ToggleSyndicationAsync()
    {
        if (Selected is null || !ShowSyndicationToggle) return;
        var on = !Selected.IsSyndication;
        Busy = true;
        Status = on ? $"Marking '{Selected.Name}' as syndication…" : $"Clearing syndication on '{Selected.Name}'…";
        try
        {
            await _shell.SetWorkAreaSyndicationAsync(Selected.Id, on, PersonalUsername).ConfigureAwait(true);
            _log.Success("WorkAreas", $"{(on ? "Marked" : "Cleared")} syndication on '{Selected.Path}'.");
            MarkDataDirty();
            await RefreshAsync().ConfigureAwait(true);
        }
        catch (Exception ex) { Status = "Failed: " + ex.Message; _log.Error("WorkAreas", ex.Message, ex); }
        finally { Busy = false; }
    }

    // ----- move / reorder -----

    [RelayCommand(CanExecute = nameof(CanMoveUp))]
    private Task MoveUpAsync() => ReorderAsync(-1);

    [RelayCommand(CanExecute = nameof(CanMoveDown))]
    private Task MoveDownAsync() => ReorderAsync(+1);

    /// <summary>Swap the selected folder with its neighbour and re-index the whole sibling set so the order is
    /// deterministic regardless of inriver's index semantics.</summary>
    private async Task ReorderAsync(int delta)
    {
        if (Selected is null) return;
        var sibs = SiblingsOf(Selected).ToList();
        var i = sibs.IndexOf(Selected);
        var j = i + delta;
        if (i < 0 || j < 0 || j >= sibs.Count) return;
        (sibs[i], sibs[j]) = (sibs[j], sibs[i]);

        Busy = true;
        Status = "Reordering…";
        try
        {
            // Normalise the whole sibling set to contiguous 0..n-1 in the new order. Writing every sibling
            // (not just the moved ones) makes the result deterministic regardless of inriver's pre-existing,
            // possibly non-contiguous or duplicated, index values.
            for (var k = 0; k < sibs.Count; k++)
                await _shell.SetWorkAreaIndexAsync(sibs[k].Id, k, PersonalUsername).ConfigureAwait(true);
            MarkDataDirty();
            await RefreshAsync().ConfigureAwait(true);
        }
        catch (Exception ex) { Status = "Failed: " + ex.Message; _log.Error("WorkAreas", ex.Message, ex); }
        finally { Busy = false; }
    }

    /// <summary>Nest the selected folder under its preceding sibling.</summary>
    [RelayCommand(CanExecute = nameof(CanIndent))]
    private async Task IndentAsync()
    {
        if (Selected is null) return;
        var sibs = SiblingsOf(Selected);
        var i = sibs.IndexOf(Selected);
        if (i <= 0) return;
        var newParent = sibs[i - 1];
        await MoveUnderAsync(newParent.Id, newParent.Children.Count, $"into '{newParent.Name}'").ConfigureAwait(true);
    }

    /// <summary>Move the selected folder out to its grandparent (can't move to the very top — inriver's move
    /// requires a parent folder).</summary>
    [RelayCommand(CanExecute = nameof(CanOutdent))]
    private async Task OutdentAsync()
    {
        var grand = Selected?.Parent?.Parent;
        if (grand is null) return;
        await MoveUnderAsync(grand.Id, grand.Children.Count, $"out to '{grand.Name}'").ConfigureAwait(true);
    }

    private async Task MoveUnderAsync(Guid newParentId, int newIndex, string where)
    {
        if (Selected is null) return;
        Busy = true;
        Status = $"Moving '{Selected.Name}' {where}…";
        try
        {
            await _shell.MoveWorkAreaFolderAsync(Selected.Id, newParentId, newIndex, PersonalUsername).ConfigureAwait(true);
            _log.Success("WorkAreas", $"Moved '{Selected.Name}' {where}.");
            MarkDataDirty();
            await RefreshAsync().ConfigureAwait(true);
        }
        catch (Exception ex) { Status = "Failed: " + ex.Message; _log.Error("WorkAreas", ex.Message, ex); }
        finally { Busy = false; }
    }

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
            var folders = await _shell.ListWorkAreasAsync(PersonalUsername).ConfigureAwait(true);
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
        var plan = new ModelMeister.Ui.Services.Import.Plans.WorkAreasImportPlan(_main, _shell, _log, PersonalUsername);
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
    public int Index => Dto.Index;
    public bool IsQuery => Dto.IsQuery;
    public bool IsSyndication => Dto.IsSyndication;
    public string? QueryJson => Dto.QueryJson;
    public string Owner => Dto.Username ?? "shared";

    /// <summary>Parent node (null for a root folder). Set during tree build; drives indent/outdent.</summary>
    public WorkAreaNode? Parent { get; set; }

    /// <summary>Short badge describing what kind of folder this is (drives the tree row chip).</summary>
    public string Kind => IsSyndication ? "syndication" : IsQuery ? "query" : "folder";

    public ObservableCollection<WorkAreaNode> Children { get; }
}
