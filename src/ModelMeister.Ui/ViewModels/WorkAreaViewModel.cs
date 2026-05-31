using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Collections.Specialized;
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

/// <summary>How a folder is held on the in-app clipboard for the next <c>Paste</c>.</summary>
public enum ClipboardMode
{
    /// <summary>Nothing on the clipboard.</summary>
    None,
    /// <summary>The clipboard node will be <em>moved</em> (re-parented) on paste.</summary>
    Cut,
    /// <summary>The clipboard node will be <em>deep-copied</em> on paste.</summary>
    Copy,
}

/// <summary>
/// Work-area page view-model. Presents the connected env's shared work-area folders as a tree, with
/// single-env CRUD (new folder / sub-folder / query folder, rename, delete), reorder + re-parent
/// (move up/down, indent/outdent, move-to picker, drag-drop), copy/duplicate (shallow + deep, in-place,
/// to a picked destination, and across scopes), a cut/copy/paste clipboard, multi-select bulk actions,
/// a syndication toggle, a saved-search builder, Excel export/import, a search filter, and a detail pane
/// showing the selected query. Operational config — does NOT route through the model diff/apply
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

    /// <summary>Multi-selected nodes (mirrored control→VM by <c>TreeSelectionBehavior</c>). Drives the bulk
    /// commands; when this is empty or holds a single node the bulk commands fall back to <see cref="Selected"/>.</summary>
    public ObservableCollection<WorkAreaNode> SelectedNodes { get; } = [];

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
    [NotifyCanExecuteChangedFor(nameof(DuplicateFolderCommand))]
    [NotifyCanExecuteChangedFor(nameof(DuplicateSubtreeCommand))]
    [NotifyCanExecuteChangedFor(nameof(CopyToCommand))]
    [NotifyCanExecuteChangedFor(nameof(MoveToCommand))]
    [NotifyCanExecuteChangedFor(nameof(CutCommand))]
    [NotifyCanExecuteChangedFor(nameof(CopyNodeCommand))]
    [NotifyCanExecuteChangedFor(nameof(PasteCommand))]
    [NotifyCanExecuteChangedFor(nameof(DeleteSelectedCommand))]
    [NotifyCanExecuteChangedFor(nameof(MoveSelectedToCommand))]
    [NotifyCanExecuteChangedFor(nameof(CopySelectedToCommand))]
    [NotifyCanExecuteChangedFor(nameof(ExportSelectedCommand))]
    private bool _busy;

    [ObservableProperty] private string _status = "";

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasSelection))]
    [NotifyPropertyChangedFor(nameof(SelectedQueryPretty))]
    [NotifyPropertyChangedFor(nameof(HasQuery))]
    [NotifyPropertyChangedFor(nameof(SelectedBreadcrumb))]
    [NotifyCanExecuteChangedFor(nameof(NewSubfolderCommand))]
    [NotifyCanExecuteChangedFor(nameof(RenameCommand))]
    [NotifyCanExecuteChangedFor(nameof(DeleteCommand))]
    [NotifyCanExecuteChangedFor(nameof(EditQueryCommand))]
    [NotifyCanExecuteChangedFor(nameof(ToggleSyndicationCommand))]
    [NotifyCanExecuteChangedFor(nameof(MoveUpCommand))]
    [NotifyCanExecuteChangedFor(nameof(MoveDownCommand))]
    [NotifyCanExecuteChangedFor(nameof(IndentCommand))]
    [NotifyCanExecuteChangedFor(nameof(OutdentCommand))]
    [NotifyCanExecuteChangedFor(nameof(DuplicateFolderCommand))]
    [NotifyCanExecuteChangedFor(nameof(DuplicateSubtreeCommand))]
    [NotifyCanExecuteChangedFor(nameof(CopyToCommand))]
    [NotifyCanExecuteChangedFor(nameof(MoveToCommand))]
    [NotifyCanExecuteChangedFor(nameof(CutCommand))]
    [NotifyCanExecuteChangedFor(nameof(CopyNodeCommand))]
    [NotifyCanExecuteChangedFor(nameof(DeleteSelectedCommand))]
    [NotifyCanExecuteChangedFor(nameof(MoveSelectedToCommand))]
    [NotifyCanExecuteChangedFor(nameof(CopySelectedToCommand))]
    [NotifyCanExecuteChangedFor(nameof(ExportSelectedCommand))]
    private WorkAreaNode? _selected;

    /// <summary>Search box text. Empty resets all rows visible and restores the prior expansion snapshot.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasFilter))]
    private string _filterText = "";

    /// <summary>The single folder currently on the in-app clipboard (Cut/Copy), or null.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasClipboard))]
    [NotifyPropertyChangedFor(nameof(ClipboardSummary))]
    [NotifyPropertyChangedFor(nameof(PasteMenuHeader))]
    [NotifyCanExecuteChangedFor(nameof(PasteCommand))]
    [NotifyCanExecuteChangedFor(nameof(ClearClipboardCommand))]
    private WorkAreaNode? _clipboardNode;

    /// <summary>Whether the clipboard node will be moved (Cut) or deep-copied (Copy) on paste.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasClipboard))]
    [NotifyPropertyChangedFor(nameof(ClipboardSummary))]
    [NotifyPropertyChangedFor(nameof(PasteMenuHeader))]
    private ClipboardMode _clipboardMode;

    public bool HasSelection => Selected is not null;
    public bool HasQuery => Selected?.IsQuery == true && !string.IsNullOrWhiteSpace(Selected.QueryJson);

    /// <summary>True when a search filter is active (drives the clear-filter affordance).</summary>
    public bool HasFilter => !string.IsNullOrEmpty(FilterText);

    /// <summary>True when a folder sits on the in-app clipboard (gates <c>Paste</c>).</summary>
    public bool HasClipboard => ClipboardNode is not null && ClipboardMode != ClipboardMode.None;

    /// <summary>Persistent clipboard chip text, e.g. "Clipboard: Marketing (move)" — empty when nothing armed.</summary>
    public string ClipboardSummary => HasClipboard
        ? $"Clipboard: {ClipboardNode!.Name} ({(ClipboardMode == ClipboardMode.Cut ? "move" : "copy")})"
        : "";

    /// <summary>Dynamic Paste context-menu header so the user sees WHAT is on the clipboard and whether it is a
    /// move or a copy.</summary>
    public string PasteMenuHeader => HasClipboard
        ? $"Paste '{ClipboardNode!.Name}' here ({(ClipboardMode == ClipboardMode.Cut ? "move" : "copy")})"
        : "Paste";

    /// <summary>Pretty-printed (re-indented) query JSON for the detail pane.</summary>
    public string SelectedQueryPretty => HasQuery ? PrettyJson(Selected!.QueryJson!) : "";

    /// <summary>Breadcrumb from the root down to <see cref="Selected"/> (inclusive). Empty when nothing is selected.</summary>
    public IReadOnlyList<WorkAreaNode> SelectedBreadcrumb
    {
        get
        {
            if (Selected is null) return [];
            var chain = new List<WorkAreaNode>();
            for (var n = Selected; n is not null; n = n.Parent) chain.Add(n);
            chain.Reverse();
            return chain;
        }
    }

    /// <summary>True when a filter is active but no folder in the whole tree matches it (drives the
    /// filtered-empty state). Recomputed after every <see cref="ApplyFilter"/> pass.</summary>
    public bool HasNoFilterMatches => HasFilter && Tree.Count > 0 && !AnyVisible(Tree);

    /// <summary>True when more than one node is multi-selected (drives the "Delete selected" context item).</summary>
    public bool HasMultiSelection => SelectedNodes.Count > 1;

    private static bool AnyVisible(IEnumerable<WorkAreaNode> nodes)
    {
        foreach (var n in nodes)
        {
            if (n.IsVisible) return true;
            if (AnyVisible(n.Children)) return true;
        }
        return false;
    }

    /// <summary>Make a breadcrumb segment the new primary selection.</summary>
    [RelayCommand]
    private void SelectBreadcrumb(WorkAreaNode? node)
    {
        if (node is not null) Selected = node;
    }

    /// <summary>Reveal the selected row whenever the selection changes: walk its parent chain and expand each
    /// ancestor so a node selected from a breadcrumb, a post-create re-select, or a refresh is never left
    /// hidden inside a collapsed parent.</summary>
    partial void OnSelectedChanged(WorkAreaNode? value) => RevealNode(value);

    private static void RevealNode(WorkAreaNode? node)
    {
        for (var p = node?.Parent; p is not null; p = p.Parent)
            p.IsExpanded = true;
    }

    public WorkAreaViewModel(MainWindowViewModel main, Shell shell, IAppLog log)
    {
        _main = main;
        _shell = shell;
        _log = log;
        SelectedNodes.CollectionChanged += OnSelectedNodesChanged;
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

    private void OnSelectedNodesChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        DeleteSelectedCommand.NotifyCanExecuteChanged();
        MoveSelectedToCommand.NotifyCanExecuteChanged();
        CopySelectedToCommand.NotifyCanExecuteChanged();
        ExportSelectedCommand.NotifyCanExecuteChanged();
        OnPropertyChanged(nameof(HasMultiSelection));
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

    // ---- new-command CanExecute predicates (DESIGN §D) ----

    /// <summary>Single-target commands. Gated only on connectivity so a right-click on an UNselected row keeps
    /// the menu items enabled — the resolved target comes from the command parameter (the right-clicked node)
    /// or falls back to <see cref="Selected"/>, and each body re-checks via <c>Target(node) is not {} src</c>.
    /// (The View also selects the row on right-click so <see cref="Selected"/> stays in sync — DESIGN §J.)</summary>
    private bool CanActOnTarget() => !Busy && _main.IsConnected;

    /// <summary>Bulk commands: anything selected — the multi-set, or the single primary node.</summary>
    private bool CanActOnSelectionMulti() => !Busy && _main.IsConnected && (SelectedNodes.Count > 0 || Selected is not null);

    /// <summary>Paste is enabled when something is on the clipboard.</summary>
    private bool CanPaste() => !Busy && _main.IsConnected && HasClipboard;

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
            var clipboardId = ClipboardNode?.Id;
            BuildTree(folders);
            // Re-select the previously-selected folder by path so a refresh after a rename/edit keeps focus.
            if (selectedPath is not null) Selected = FindByPath(Tree, selectedPath);
            // A rebuild re-creates every node, so the multi-selection set is stale — clear it (the control
            // will re-mirror what survives once it re-renders).
            SelectedNodes.Clear();
            // The clipboard node is likewise an orphaned instance now — re-resolve it to the surviving node by
            // id (re-applying the Cut visual), or clear the clipboard if the folder no longer exists. Without
            // this the paste self/descendant guard and the IsCut dimming would silently break after a refresh.
            ReResolveClipboard(clipboardId);
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

    /// <summary>Build the folder hierarchy from the flat DTO list (by <c>ParentId</c>, ordered by Index then Name).
    /// Captures the current expansion state by path before clearing and re-applies it after the rebuild so
    /// expand/collapse, the search filter, and drag-drop all survive a refresh round-trip.</summary>
    private void BuildTree(IReadOnlyList<WorkAreaFolderDto> folders)
    {
        // Snapshot expansion (by path) so it survives the node re-creation.
        var expandedPaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        CollectExpandedPaths(Tree, expandedPaths);

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

        // Set Depth and re-apply expansion in one walk.
        ApplyDepthAndExpansion(Tree, 0, expandedPaths);

        // Re-apply any active filter against the freshly built tree.
        if (HasFilter) ApplyFilter();
        OnPropertyChanged(nameof(HasNoFilterMatches));
    }

    private static void CollectExpandedPaths(IEnumerable<WorkAreaNode> nodes, HashSet<string> into)
    {
        foreach (var n in nodes)
        {
            if (n.IsExpanded) into.Add(n.Path);
            CollectExpandedPaths(n.Children, into);
        }
    }

    private static void ApplyDepthAndExpansion(IEnumerable<WorkAreaNode> nodes, int depth, HashSet<string> expandedPaths)
    {
        foreach (var n in nodes)
        {
            n.Depth = depth;
            n.IsExpanded = expandedPaths.Contains(n.Path);
            ApplyDepthAndExpansion(n.Children, depth + 1, expandedPaths);
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

    /// <summary>Resolve the command target: the right-clicked <paramref name="node"/> if given, else <see cref="Selected"/>.</summary>
    private WorkAreaNode? Target(WorkAreaNode? node) => node ?? Selected;

    /// <summary>The set of nodes a bulk command should act on: the multi-selection, else the single primary node.
    /// Descendants of an already-selected node are dropped so a subtree isn't acted on twice.</summary>
    private IReadOnlyList<WorkAreaNode> BulkTargets()
    {
        var raw = SelectedNodes.Count > 0
            ? SelectedNodes.ToList()
            : (Selected is null ? new List<WorkAreaNode>() : [Selected]);
        // Drop any node that has an ancestor also in the set.
        return raw.Where(n => !raw.Any(other => !ReferenceEquals(other, n) && IsDescendantOf(n, other))).ToList();
    }

    /// <summary>True when <paramref name="node"/> is <paramref name="ancestor"/> or sits anywhere beneath it.</summary>
    private static bool IsDescendantOf(WorkAreaNode node, WorkAreaNode ancestor)
    {
        for (var p = node.Parent; p is not null; p = p.Parent)
            if (ReferenceEquals(p, ancestor)) return true;
        return false;
    }

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

    // ----- duplicate / copy / move (DESIGN §D) -----

    /// <summary>Shallow-duplicate the target folder beside itself (just the folder, no children) with a
    /// "(copy)" name.</summary>
    [RelayCommand(CanExecute = nameof(CanActOnTarget))]
    private async Task DuplicateFolderAsync(WorkAreaNode? node)
    {
        if (Target(node) is not { } src) return;
        // newName: null → the service derives the "(copy)" name from the live destination siblings.
        await RunAsync($"Duplicating '{src.Name}'…", $"Duplicated '{src.Path}'.", () =>
            _shell.CopyWorkAreaFolderAsync(src.Id, src.Parent?.Id, SiblingsOf(src).Count, newName: null, PersonalUsername), toastTitle: "Folder duplicated").ConfigureAwait(true);
    }

    /// <summary>Deep-duplicate the target subtree beside itself (folder + all descendants) with a "(copy)" name.</summary>
    [RelayCommand(CanExecute = nameof(CanActOnTarget))]
    private async Task DuplicateSubtreeAsync(WorkAreaNode? node)
    {
        if (Target(node) is not { } src) return;
        // newName: null → the service derives the "(copy)" name from the live destination siblings.
        await RunAsync($"Copying '{src.Name}' (with sub-folders)…", $"Copied subtree '{src.Path}'.", () =>
            _shell.CopyWorkAreaSubtreeAsync(src.Id, src.Parent?.Id, SiblingsOf(src).Count, newName: null, PersonalUsername), toastTitle: "Subtree copied").ConfigureAwait(true);
    }

    /// <summary>Pick a destination folder (and optionally a scope/user) and deep-copy the target subtree there.</summary>
    [RelayCommand(CanExecute = nameof(CanActOnTarget))]
    private async Task CopyToAsync(WorkAreaNode? node)
    {
        if (Target(node) is not { } src) return;
        var pick = await DialogHost.PickFolderAsync(
            $"Copy '{src.Name}' to…", Tree, src, ShowScopeSwitchInPicker, Users, PersonalUsername).ConfigureAwait(true);
        if (pick is null) return;
        await CopyToDestinationAsync(src, pick).ConfigureAwait(true);
    }

    private async Task CopyToDestinationAsync(WorkAreaNode src, FolderPickResult pick)
    {
        var crossScope = !string.Equals(pick.TargetPersonalUsername, PersonalUsername, StringComparison.OrdinalIgnoreCase);
        var index = DestinationIndex(pick.TargetParentId, crossScope ? null : src);
        await RunAsync($"Copying '{src.Name}'…", $"Copied '{src.Path}'.", () => crossScope
            ? _shell.CopyWorkAreaAcrossScopeAsync(src.Id, PersonalUsername, pick.TargetParentId, pick.TargetPersonalUsername, index)
            : _shell.CopyWorkAreaSubtreeAsync(src.Id, pick.TargetParentId, index, newName: null, PersonalUsername), toastTitle: "Folder copied").ConfigureAwait(true);
    }

    /// <summary>Pick a destination folder and re-parent the target there. Cross-scope move = copy + delete
    /// (after an explicit confirm, since a move can't span scopes atomically).</summary>
    [RelayCommand(CanExecute = nameof(CanActOnTarget))]
    private async Task MoveToAsync(WorkAreaNode? node)
    {
        if (Target(node) is not { } src) return;
        var pick = await DialogHost.PickFolderAsync(
            $"Move '{src.Name}' to…", Tree, src, ShowScopeSwitchInPicker, Users, PersonalUsername).ConfigureAwait(true);
        if (pick is null) return;

        var crossScope = !string.Equals(pick.TargetPersonalUsername, PersonalUsername, StringComparison.OrdinalIgnoreCase);
        if (crossScope)
        {
            var ok = await DialogHost.ConfirmAsync(
                "Move across scope",
                $"Moving '{src.Name}' to another scope copies it there and deletes the original. Continue?",
                confirmLabel: "Move").ConfigureAwait(true);
            if (!ok) return;
            await RunAsync($"Moving '{src.Name}'…", $"Moved '{src.Path}' across scope.", async () =>
            {
                var newId = await _shell.CopyWorkAreaAcrossScopeAsync(
                    src.Id, PersonalUsername, pick.TargetParentId, pick.TargetPersonalUsername, DestinationIndex(pick.TargetParentId, null)).ConfigureAwait(true);
                await _shell.DeleteWorkAreaFolderAsync(src.Id, PersonalUsername).ConfigureAwait(true);
                return newId;
            }, toastTitle: "Folder moved").ConfigureAwait(true);
            return;
        }

        if (pick.TargetParentId is not { } parentId)
        {
            _log.Toast(LogLevel.Warn, "Move", "inriver requires a parent folder — can't move to the root.");
            return;
        }
        await RunAsync($"Moving '{src.Name}'…", $"Moved '{src.Path}'.", () =>
            _shell.MoveWorkAreaFolderAsync(src.Id, parentId, DestinationIndex(parentId, src), PersonalUsername), toastTitle: "Folder moved").ConfigureAwait(true);
    }

    /// <summary>Whether the destination picker offers a scope/user switch (only meaningful when there are users to pick).</summary>
    private bool ShowScopeSwitchInPicker => ShowUserPicker || Users.Count > 0;

    /// <summary>Sibling count under <paramref name="targetParentId"/> in the current tree (append position),
    /// excluding <paramref name="moving"/> if it is already a child there (so a same-parent move keeps its slot).</summary>
    private int DestinationIndex(Guid? targetParentId, WorkAreaNode? moving)
    {
        var siblings = targetParentId is { } pid
            ? (FindById(Tree, pid)?.Children.Count ?? 0)
            : Tree.Count;
        if (moving is not null && moving.Parent?.Id == targetParentId) return Math.Max(0, siblings - 1);
        return siblings;
    }

    private static WorkAreaNode? FindById(IEnumerable<WorkAreaNode> nodes, Guid id)
    {
        foreach (var n in nodes)
        {
            if (n.Id == id) return n;
            if (FindById(n.Children, id) is { } hit) return hit;
        }
        return null;
    }

    // ----- clipboard (cut / copy / paste) -----

    /// <summary>Mark the target folder for a move on the next paste.</summary>
    [RelayCommand(CanExecute = nameof(CanActOnTarget))]
    private void Cut(WorkAreaNode? node)
    {
        if (Target(node) is not { } src) return;
        ClearClipboardFlag();
        ClipboardNode = src;
        ClipboardMode = ClipboardMode.Cut;
        src.IsCut = true;
        Status = $"Cut '{src.Name}' — choose a folder and Paste.";
    }

    /// <summary>Mark the target folder for a deep copy on the next paste.</summary>
    [RelayCommand(CanExecute = nameof(CanActOnTarget))]
    private void CopyNode(WorkAreaNode? node)
    {
        if (Target(node) is not { } src) return;
        ClearClipboardFlag();
        ClipboardNode = src;
        ClipboardMode = ClipboardMode.Copy;
        Status = $"Copied '{src.Name}' — choose a folder and Paste.";
    }

    /// <summary>Paste the clipboard folder under the target (or root): Cut ⇒ move, Copy ⇒ deep copy.</summary>
    [RelayCommand(CanExecute = nameof(CanPaste))]
    private async Task PasteAsync(WorkAreaNode? targetNode)
    {
        if (ClipboardNode is not { } src || ClipboardMode == ClipboardMode.None) return;
        // Resolve the clipboard source against the LIVE tree by id (it may be an orphan from before a refresh),
        // so the self/descendant guard compares same-tree instances rather than a stale reference.
        var liveSrc = FindById(Tree, src.Id) ?? src;
        var dest = Target(targetNode);
        if (dest is not null && (dest.Id == liveSrc.Id || IsDescendantOf(dest, liveSrc)))
        {
            _log.Toast(LogLevel.Warn, "Paste", "Can't paste a folder into itself or one of its sub-folders.");
            return;
        }

        var mode = ClipboardMode;
        var index = DestinationIndex(dest?.Id, mode == ClipboardMode.Cut ? liveSrc : null);
        if (mode == ClipboardMode.Cut)
        {
            if (dest is null)
            {
                // inriver's move requires a parent folder, so a Cut+paste at the root can't go straight through.
                // Instead of a dead-end, fall back to the Move-to picker (clearing the clipboard once it runs)
                // so the user can still place a cut folder elsewhere.
                ClearClipboard();
                await MoveToAsync(liveSrc).ConfigureAwait(true);
                return;
            }
            await RunAsync($"Moving '{src.Name}'…", $"Moved '{src.Name}' into '{dest.Path}'.", () =>
                _shell.MoveWorkAreaFolderAsync(src.Id, dest.Id, index, PersonalUsername), toastTitle: "Folder moved").ConfigureAwait(true);
        }
        else
        {
            await RunAsync($"Copying '{src.Name}'…", $"Copied '{src.Name}' into '{(dest?.Path ?? "(root)")}'.", () =>
                _shell.CopyWorkAreaSubtreeAsync(src.Id, dest?.Id, index, newName: null, PersonalUsername), toastTitle: "Folder copied").ConfigureAwait(true);
        }

        ClearClipboard();
    }

    private void ClearClipboardFlag()
    {
        if (ClipboardNode is { } prev) prev.IsCut = false;
    }

    /// <summary>Clear the in-app clipboard (Esc / the clipboard pill's Clear button / after a paste).</summary>
    [RelayCommand]
    private void ClearClipboard()
    {
        ClearClipboardFlag();
        ClipboardNode = null;
        ClipboardMode = ClipboardMode.None;
    }

    /// <summary>After a tree rebuild, point <see cref="ClipboardNode"/> at the surviving node with the same id
    /// (re-applying the Cut dimming) or clear the clipboard when that folder is gone. Keeps the paste guard and
    /// the Cut visual valid across refreshes.</summary>
    private void ReResolveClipboard(Guid? clipboardId)
    {
        if (clipboardId is not { } id || ClipboardMode == ClipboardMode.None)
        {
            if (ClipboardNode is not null) ClearClipboard();
            return;
        }
        if (FindById(Tree, id) is { } survivor)
        {
            ClipboardNode = survivor;
            survivor.IsCut = ClipboardMode == ClipboardMode.Cut;
        }
        else
        {
            ClearClipboard();
        }
    }

    // ----- bulk (multi-select) -----

    /// <summary>Delete every selected subtree after one itemized confirmation.</summary>
    [RelayCommand(CanExecute = nameof(CanActOnSelectionMulti))]
    private async Task DeleteSelectedAsync()
    {
        var targets = BulkTargets();
        if (targets.Count == 0) return;
        var items = targets
            .Select(n => { var d = CountDescendants(n); return n.Path + (d > 0 ? $"  (+{d} nested)" : ""); })
            .ToList();
        var ok = await DialogHost.ConfirmBulkAsync(
            "Delete work-area folders", "Delete", "folder", items,
            _main.ConnectedEnv?.Name, _main.ConnectedEnv?.TypeKey).ConfigureAwait(true);
        if (!ok) return;

        Busy = true;
        Status = $"Deleting {targets.Count} folder(s)…";
        var done = 0;
        var failed = 0;
        try
        {
            foreach (var n in targets)
            {
                // Resilient per-item: one bad folder shouldn't abandon the rest of the batch.
                try
                {
                    await _shell.DeleteWorkAreaFolderAsync(n.Id, PersonalUsername).ConfigureAwait(true);
                    done++;
                }
                catch (Exception ex) { failed++; _log.Error("WorkAreas", $"Delete of '{n.Path}' failed: {ex.Message}", ex); }
            }
            var summary = BulkSummary("Deleted", done, targets.Count, skipped: 0, failed);
            _log.Success("WorkAreas", summary);
            _log.Toast(failed > 0 ? LogLevel.Warn : LogLevel.Success, "Folders deleted", summary);
            Selected = null;
            MarkDataDirty();
            await RefreshAsync().ConfigureAwait(true);
        }
        catch (Exception ex) { Status = "Failed: " + ex.Message; _log.Error("WorkAreas", ex.Message, ex); }
        finally { Busy = false; }
    }

    /// <summary>Re-parent every selected subtree under one picked destination.</summary>
    [RelayCommand(CanExecute = nameof(CanActOnSelectionMulti))]
    private async Task MoveSelectedToAsync()
    {
        var targets = BulkTargets();
        if (targets.Count == 0) return;
        var pick = await DialogHost.PickFolderAsync(
            $"Move {targets.Count} folder(s) to…", Tree, exclude: null, ShowScopeSwitchInPicker, Users, PersonalUsername).ConfigureAwait(true);
        if (pick is null) return;

        var crossScope = !string.Equals(pick.TargetPersonalUsername, PersonalUsername, StringComparison.OrdinalIgnoreCase);
        if (crossScope)
        {
            var ok = await DialogHost.ConfirmAsync(
                "Move across scope",
                $"Moving {targets.Count} folder(s) to another scope copies them there and deletes the originals. Continue?",
                confirmLabel: "Move").ConfigureAwait(true);
            if (!ok) return;
        }
        else if (pick.TargetParentId is null)
        {
            _log.Toast(LogLevel.Warn, "Move", "inriver requires a parent folder — can't move to the root.");
            return;
        }

        Busy = true;
        Status = $"Moving {targets.Count} folder(s)…";
        // No refresh happens until the loop ends, so the in-memory Tree (and thus DestinationIndex) is stale
        // across iterations — carry a running insert position so each item lands after the previous one rather
        // than all at the same append index (which would leave the final order up to inriver's tie-breaking).
        var baseIndex = DestinationIndex(pick.TargetParentId, null);
        var done = 0;
        var skipped = 0;
        var failed = 0;
        try
        {
            foreach (var n in targets)
            {
                // Guard each item against landing inside its own subtree.
                if (pick.TargetParentId is { } pid && (n.Id == pid || FindById(n.Children, pid) is not null)) { skipped++; continue; }
                var index = baseIndex + done;
                try
                {
                    if (crossScope)
                    {
                        await _shell.CopyWorkAreaAcrossScopeAsync(n.Id, PersonalUsername, pick.TargetParentId, pick.TargetPersonalUsername, index).ConfigureAwait(true);
                        await _shell.DeleteWorkAreaFolderAsync(n.Id, PersonalUsername).ConfigureAwait(true);
                    }
                    else
                    {
                        await _shell.MoveWorkAreaFolderAsync(n.Id, pick.TargetParentId!.Value, index, PersonalUsername).ConfigureAwait(true);
                    }
                    done++;
                }
                catch (Exception ex) { failed++; _log.Error("WorkAreas", $"Move of '{n.Path}' failed: {ex.Message}", ex); }
            }
            var summary = BulkSummary("Moved", done, targets.Count, skipped, failed);
            _log.Success("WorkAreas", summary);
            _log.Toast(failed > 0 ? LogLevel.Warn : LogLevel.Success, "Folders moved", summary);
            MarkDataDirty();
            await RefreshAsync().ConfigureAwait(true);
        }
        catch (Exception ex) { Status = "Failed: " + ex.Message; _log.Error("WorkAreas", ex.Message, ex); }
        finally { Busy = false; }
    }

    /// <summary>Human-readable bulk outcome, e.g. "Moved 3 of 5 folder(s) (1 skipped, 1 failed)".</summary>
    private static string BulkSummary(string verb, int done, int total, int skipped, int failed)
    {
        var extras = new List<string>();
        if (skipped > 0) extras.Add($"{skipped} skipped");
        if (failed > 0) extras.Add($"{failed} failed");
        var tail = extras.Count > 0 ? $" ({string.Join(", ", extras)})" : "";
        return $"{verb} {done} of {total} folder(s){tail}.";
    }

    /// <summary>Deep-copy every selected subtree under one picked destination.</summary>
    [RelayCommand(CanExecute = nameof(CanActOnSelectionMulti))]
    private async Task CopySelectedToAsync()
    {
        var targets = BulkTargets();
        if (targets.Count == 0) return;
        var pick = await DialogHost.PickFolderAsync(
            $"Copy {targets.Count} folder(s) to…", Tree, exclude: null, ShowScopeSwitchInPicker, Users, PersonalUsername).ConfigureAwait(true);
        if (pick is null) return;

        var crossScope = !string.Equals(pick.TargetPersonalUsername, PersonalUsername, StringComparison.OrdinalIgnoreCase);
        Busy = true;
        Status = $"Copying {targets.Count} folder(s)…";
        // As with the bulk move: no refresh mid-loop, so carry a running insert index instead of recomputing
        // the same append position every iteration (which would make the final sibling order non-deterministic).
        var baseIndex = DestinationIndex(pick.TargetParentId, null);
        var done = 0;
        var failed = 0;
        try
        {
            foreach (var n in targets)
            {
                var index = baseIndex + done;
                try
                {
                    if (crossScope)
                        await _shell.CopyWorkAreaAcrossScopeAsync(n.Id, PersonalUsername, pick.TargetParentId, pick.TargetPersonalUsername, index).ConfigureAwait(true);
                    else
                        await _shell.CopyWorkAreaSubtreeAsync(n.Id, pick.TargetParentId, index, newName: null, PersonalUsername).ConfigureAwait(true);
                    done++;
                }
                catch (Exception ex) { failed++; _log.Error("WorkAreas", $"Copy of '{n.Path}' failed: {ex.Message}", ex); }
            }
            var summary = BulkSummary("Copied", done, targets.Count, skipped: 0, failed);
            _log.Success("WorkAreas", summary);
            _log.Toast(failed > 0 ? LogLevel.Warn : LogLevel.Success, "Folders copied", summary);
            MarkDataDirty();
            await RefreshAsync().ConfigureAwait(true);
        }
        catch (Exception ex) { Status = "Failed: " + ex.Message; _log.Error("WorkAreas", ex.Message, ex); }
        finally { Busy = false; }
    }

    /// <summary>Export the selected subtrees (folders + descendants) to an Excel workbook.</summary>
    [RelayCommand(CanExecute = nameof(CanActOnSelectionMulti))]
    private async Task ExportSelectedAsync()
    {
        var targets = BulkTargets();
        if (targets.Count == 0) return;
        var path = await FilePickerHelpers.PickSaveAsync("Save selected work-areas", "workareas-selected.xlsx", "xlsx").ConfigureAwait(true);
        if (path is null) return;

        // Gather each selected subtree's DTOs (root + descendants) from the flat backing list, de-duplicated.
        var keep = new HashSet<Guid>();
        foreach (var n in targets) CollectIds(n, keep);
        var rows = _flat.Where(f => keep.Contains(f.Id)).ToList();

        Busy = true;
        try
        {
            await Task.Run(() => WorkAreaWorkbook.Save(rows, path)).ConfigureAwait(true);
            Status = $"Wrote {Path.GetFileName(path)} · {rows.Count} folder(s)";
            _log.Success("WorkAreas", $"Exported {rows.Count} selected folder(s) → {path}");
        }
        catch (Exception ex) { Status = "Failed: " + ex.Message; _log.Error("WorkAreas", ex.Message, ex); }
        finally { Busy = false; }
    }

    private static void CollectIds(WorkAreaNode node, HashSet<Guid> into)
    {
        into.Add(node.Id);
        foreach (var c in node.Children) CollectIds(c, into);
    }

    // ----- expand / collapse / filter -----

    /// <summary>Expand every node in the tree.</summary>
    [RelayCommand]
    private void ExpandAll() => SetExpandedAll(Tree, true);

    /// <summary>Collapse every node in the tree.</summary>
    [RelayCommand]
    private void CollapseAll() => SetExpandedAll(Tree, false);

    private static void SetExpandedAll(IEnumerable<WorkAreaNode> nodes, bool expanded)
    {
        foreach (var n in nodes)
        {
            n.IsExpanded = expanded;
            SetExpandedAll(n.Children, expanded);
        }
    }

    /// <summary>Clear the search filter.</summary>
    [RelayCommand]
    private void ClearFilter() => FilterText = "";

    partial void OnFilterTextChanged(string value)
    {
        ApplyFilter();
        OnPropertyChanged(nameof(HasNoFilterMatches));
    }

    /// <summary>Recompute each node's visibility against <see cref="FilterText"/>. A node is visible when its
    /// name/path matches OR any descendant matches; matches force their ancestors visible and auto-expand them.
    /// An empty filter resets all rows visible.</summary>
    private void ApplyFilter()
    {
        if (!HasFilter)
        {
            ShowAll(Tree);
            return;
        }
        foreach (var n in Tree) MarkVisible(n, FilterText);
    }

    private static void ShowAll(IEnumerable<WorkAreaNode> nodes)
    {
        foreach (var n in nodes)
        {
            n.IsVisible = true;
            ShowAll(n.Children);
        }
    }

    /// <summary>Returns true when <paramref name="node"/> (self or any descendant) matches the filter; sets
    /// <c>IsVisible</c> on the whole subtree accordingly and auto-expands ancestors of matches.</summary>
    private static bool MarkVisible(WorkAreaNode node, string filter)
    {
        var selfMatch =
            node.Name.Contains(filter, StringComparison.OrdinalIgnoreCase) ||
            node.Path.Contains(filter, StringComparison.OrdinalIgnoreCase);

        var anyChild = false;
        foreach (var c in node.Children)
            anyChild |= MarkVisible(c, filter);

        var visible = selfMatch || anyChild;
        node.IsVisible = visible;
        if (anyChild) node.IsExpanded = true; // reveal the matching descendants
        return visible;
    }

    // ----- drag-drop entry points (called by the View's drag handlers) -----

    /// <summary>Re-parent <paramref name="src"/> under <paramref name="target"/> (or to the root when target is null).
    /// Guards against dropping a node onto itself or one of its own descendants.</summary>
    public async Task MoveOntoAsync(WorkAreaNode src, WorkAreaNode? target)
    {
        if (ReferenceEquals(src, target)) return;
        if (target is not null && IsDescendantOf(target, src))
        {
            _log.Toast(LogLevel.Warn, "Move", "Can't move a folder into one of its own sub-folders.");
            return;
        }
        if (target is null)
        {
            _log.Toast(LogLevel.Warn, "Move", "inriver requires a parent folder — drop onto a folder.");
            return;
        }
        var index = DestinationIndex(target.Id, src);
        await RunAsync($"Moving '{src.Name}'…", $"Moved '{src.Name}' into '{target.Path}'.", () =>
            _shell.MoveWorkAreaFolderAsync(src.Id, target.Id, index, PersonalUsername), toastTitle: "Folder moved").ConfigureAwait(true);
    }

    /// <summary>Deep-copy <paramref name="src"/> under <paramref name="target"/> (or to the root when target is null).
    /// Guards against dropping a node onto itself or one of its own descendants.</summary>
    public async Task CopyOntoAsync(WorkAreaNode src, WorkAreaNode? target)
    {
        if (ReferenceEquals(src, target)) return;
        if (target is not null && IsDescendantOf(target, src))
        {
            _log.Toast(LogLevel.Warn, "Copy", "Can't copy a folder into one of its own sub-folders.");
            return;
        }
        var index = DestinationIndex(target?.Id, null);
        await RunAsync($"Copying '{src.Name}'…", $"Copied '{src.Name}' into '{(target?.Path ?? "(root)")}'.", () =>
            _shell.CopyWorkAreaSubtreeAsync(src.Id, target?.Id, index, newName: null, PersonalUsername), toastTitle: "Folder copied").ConfigureAwait(true);
    }

    /// <summary>Run a single-result work-area mutation with the standard Busy/Status/refresh/error envelope.
    /// <paramref name="toastTitle"/> (when given) fires a success toast so move/copy/duplicate end with the
    /// same visible confirmation as Delete.</summary>
    private Task RunAsync(string runningStatus, string successLog, Func<Task<Guid>> op, string? toastTitle = null)
        => RunAsync(runningStatus, successLog, async () => { await op().ConfigureAwait(true); }, toastTitle);

    /// <summary>Run a single void-result work-area mutation with the standard envelope.</summary>
    private async Task RunAsync(string runningStatus, string successLog, Func<Task> op, string? toastTitle = null)
    {
        Busy = true;
        Status = runningStatus;
        try
        {
            await op().ConfigureAwait(true);
            _log.Success("WorkAreas", successLog);
            _log.Toast(LogLevel.Success, toastTitle ?? "Done", successLog);
            MarkDataDirty();
            await RefreshAsync().ConfigureAwait(true);
        }
        catch (Exception ex) { Status = "Failed: " + ex.Message; _log.Error("WorkAreas", ex.Message, ex); }
        finally { Busy = false; }
    }

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
/// child nodes so the Avalonia <c>TreeView</c> can bind hierarchically. Observable so the View can two-way
/// bind expansion/selection and react to filter/clipboard visual state.</summary>
public sealed partial class WorkAreaNode : ObservableObject
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

    /// <summary>Depth from the root (0 = top level). Set during tree build; drives breadcrumb + indentation.</summary>
    public int Depth { get; set; }

    /// <summary>Whether this row is expanded in the tree. Two-way bound to the <c>TreeViewItem</c>; preserved
    /// across refreshes by path and toggled by Expand-/Collapse-all and the filter.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IconKey))]
    private bool _isExpanded;

    /// <summary>Whether this row is shown (filter result). Bound to the <c>TreeViewItem.IsVisible</c>.</summary>
    [ObservableProperty] private bool _isVisible = true;

    /// <summary>Whether this row is part of the multi-selection (mirrored by <c>TreeSelectionBehavior</c>).</summary>
    [ObservableProperty] private bool _isSelected;

    /// <summary>Whether this node is the current Cut clipboard root (dimmed in the View until paste/clear).</summary>
    [ObservableProperty] private bool _isCut;

    /// <summary>Short badge describing what kind of folder this is (drives the tree row chip).</summary>
    public string Kind => IsSyndication ? "syndication" : IsQuery ? "query" : "folder";

    /// <summary>App.axaml geometry resource key for this row's icon (folder open/closed, query, syndication).</summary>
    public string IconKey => IsSyndication ? "IcoSyndication" : IsQuery ? "IcoSearch" : (IsExpanded ? "IcoFolderOpen" : "IcoFolder");

    public ObservableCollection<WorkAreaNode> Children { get; }
}
