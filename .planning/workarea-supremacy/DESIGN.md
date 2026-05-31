# WorkArea Supremacy — Authoritative Implementation Design

Goal: "fully realize work-area supremacy" — advanced fully-featured query config, a right-click
menu with every option, copy/move/duplicate (shallow, deep, cross-scope), every admin workflow,
perfect UX. **Expand on what exists; do not regress it.** This document is the contract; pin names
and signatures exactly so independently-written slices fit together.

Repo facts that constrain every choice:
- `net9.0`; `Nullable` + `TreatWarningsAsErrors` + `EnforceCodeStyleInBuild` ON (a warning fails CI).
- Central package management (never put `Version=` on a `PackageReference`).
- Avalonia 11 UI uses **compiled bindings** — every view sets `x:DataType`; UI project is `net9.0-windows`.
- WorkArea reconcile matches by **Path** (parent-chain of names joined `/`, OrdinalIgnoreCase).
- A folder's query-ness can be turned **ON but not OFF** (no inriver "clear query" call). Copy must
  preserve `IsQuery`/`Query`, never attempt to strip a query.
- `RemotingSurfaceCoverageTests` gate: a remoting method that is neither in `OutOfScope` nor present
  in the hard-coded scan array fails CI. **All copy/move/duplicate work reuses already-scanned calls.**

---

## A. OPERATION MATRIX

Legend: **[exists]** ships today · **[new]** to build · **[expose]** backend exists, surface it.

### Single-folder ops
| Op | Status | Backs onto |
|---|---|---|
| Rename | [exists] | `Shell.RenameWorkAreaFolderAsync` |
| Delete (with descendant count + bulk confirm) | [exists] | `Shell.DeleteWorkAreaFolderAsync` |
| Duplicate shallow (folder only, "(copy)" name, same parent) | [new] | `WorkAreaService.CopyFolderAsync` |
| Duplicate deep / subtree | [new] | `WorkAreaService.CopySubtreeAsync` |
| Copy-to target folder (pick destination, same scope) | [new] | `CopySubtreeAsync` + folder-picker dialog |
| Move-to target folder (re-parent via picker) | [new VM] | `Shell.MoveWorkAreaFolderAsync` (+ root overload) |
| Reorder up / down | [exists] | `Shell.SetWorkAreaIndexAsync` (loop) |
| Indent / Outdent (re-parent to sibling / grandparent) | [exists] | `Shell.MoveWorkAreaFolderAsync` |
| Set / edit saved query | [exists] | `Shell.SetWorkAreaQueryAsync` |
| Toggle syndication (shared only) | [exists] | `Shell.SetWorkAreaSyndicationAsync` |
| Convert plain → query (define a search) | [exists, reframe] | create query folder / SetQuery on plain folder |
| Convert query → plain | **NOT POSSIBLE** | inriver has no clear-query call — surface a disabled/explained item |
| Copy path / copy query JSON (OS clipboard) | [exists] | `ClipboardHelpers.CopyAsync` |

### Multi-select bulk ops
| Op | Status |
|---|---|
| Bulk delete (selected nodes) | [new] (single-select today) |
| Bulk move-to target | [new] |
| Bulk copy-to target | [new] |
| Bulk export (selected subtree to Excel) | [new] (whole-scope export exists) |

### Cross-scope ops (same env, different scope/user)
| Op | Status |
|---|---|
| Copy shared → personal(user) | [new] `Shell.CopyWorkAreasAcrossScopeAsync` |
| Copy personal(user) → shared | [new] same |
| Copy personal(userA) → personal(userB) | [new] same |
(Move-across-scope is NOT supported by inriver — there is no re-scope remoting call; "move" across
scope is modeled as copy-then-delete and is **out of scope** for v1; offer copy only.)

### Cross-env ops
| Op | Status |
|---|---|
| Promote per-row / selected / all (+ AllowDeletes mirror) | [exists] `WorkAreaCompareViewModel` |

### Tree ops
| Op | Status |
|---|---|
| Expand all / collapse all | [new] |
| Expand / collapse subtree (single node) | [new] |
| Search / filter (keeps ancestors, auto-expands matches) | [new] |
| Breadcrumb navigation in detail pane | [new] |

### Clipboard (in-app folder clipboard, distinct from OS text clipboard)
| Op | Status | Semantics |
|---|---|---|
| Cut (Ctrl+X) | [new] | mark node(s) for **move** (dimmed visual), paste = re-parent |
| Copy (Ctrl+C on a node) | [new] | mark node(s) for **duplicate**, paste = deep copy |
| Paste (Ctrl+V) | [new] | into selected folder (or root). Cut→Move, Copy→deep-copy |
| Duplicate (Ctrl+D) | [new] | deep copy in place under same parent, "(copy)" name |

Note: `Ctrl+C` is overloaded — when the tree has focus and a node is selected it does **node copy**
(in-app clipboard); the existing "Copy query JSON" / "Copy path" remain explicit context-menu items
that hit the OS clipboard. Keep them distinct to avoid surprising users.

---

## B. BACKEND CONTRACT — `WorkAreaService.cs`

All new methods **reuse `AddFolderAsync` + `SetQueryAsync`** (and `GetRawFolders` for faithful
`ComplexQuery`). **No new `IWorkAreaScope` members. No new inriver remoting calls.** Therefore
`RemotingSurfaceCoverageTests` is untouched (Add/SetQuery/SetIndex/Rename/Delete/Move are all already
scanned). Confirmed: copy/duplicate bottom out in `Add*WorkAreaFolder` + `Update*WorkAreaQuery`;
move bottoms out in `Move*WorkAreaFolder` + `Update*WorkAreaFolderIndex` — all in the scan array.

### New public/internal methods (add after `AddFolderAsync`, before the reconcile region)

```csharp
/// <summary>Shallow copy of one folder (no children) within THIS scope/env. New folder gets
/// <paramref name="newName"/> (default: source name + " (copy)"), placed under <paramref name="newParentId"/>
/// at <paramref name="newIndex"/>. Copies IsQuery + the live ComplexQuery (faithful, via GetRawFolders)
/// and IsSyndication. Returns the new folder's id.</summary>
public async Task<Guid> CopyFolderAsync(
    Guid sourceId, Guid? newParentId, int newIndex, string? newName = null,
    CancellationToken ct = default);

/// <summary>Deep copy: source folder + its entire subtree within THIS scope/env. Recreates the subtree
/// parents-before-children under <paramref name="newParentId"/>, minting fresh ids, preserving relative
/// Index order, IsQuery/IsSyndication and faithfully copying each saved query. Returns the new ROOT id.</summary>
public async Task<Guid> CopySubtreeAsync(
    Guid sourceId, Guid? newParentId, int newIndex, string? newName = null,
    CancellationToken ct = default);

/// <summary>Cross-scope / cross-service copy: read a source subtree (root + descendants) from THIS
/// service and recreate it under <paramref name="destination"/> (a WorkAreaService bound to the target
/// scope — shared or a specific user's personal). <paramref name="destParentId"/> is resolved in the
/// DESTINATION scope's id-space. <paramref name="deep"/>=false copies just the root folder. The
/// destination scope.Add stamps Username (personal); SetSyndication no-ops where unsupported. Returns the
/// new ROOT folder's id in the destination scope.</summary>
public async Task<Guid> CopyToServiceAsync(
    Guid sourceId, WorkAreaService destination, Guid? destParentId, int destIndex,
    string? newName = null, bool deep = true, CancellationToken ct = default);

/// <summary>Re-parent a folder; allows a null parent to move it to the tree ROOT (the scope.Move shim
/// takes a non-nullable parent today — this widens that one call site). No-op-safe.</summary>
public Task MoveFolderAsync(Guid id, Guid? newParentId, int newIndex, CancellationToken ct = default);
```

Note: the existing `MoveFolderAsync(Guid id, Guid newParentId, int newIndex, ct)` stays for source
compatibility OR is widened to `Guid?`. **Decision: widen to `Guid?`** and route a null parent through
a new scope path (see below) — keeps one method. Callers passing a `Guid` still bind (implicit
conversion to `Guid?`).

### How the subtree is walked / cloned (shared helper)

A pure, unit-testable static planner mirroring `BuildPlan`'s ordering:

```csharp
internal readonly record struct CopyNode(
    Guid SourceId, Guid? SourceParentId, string Name, int Index, bool IsQuery,
    bool IsSyndication, ComplexQuery? Query, int Depth);

/// <summary>Flatten a source subtree rooted at <paramref name="rootId"/> from raw live folders into a
/// depth-then-index ordered list (parents before children). Pure.</summary>
internal static IReadOnlyList<CopyNode> FlattenSubtree(
    IReadOnlyList<IriverWorkAreaFolder> live, Guid rootId);
```

Clone algorithm (used by `CopySubtreeAsync` and `CopyToServiceAsync`):
1. `raw = GetRawFolders()`; `nodes = FlattenSubtree(raw, sourceId)` (already parents-before-children).
2. `var idMap = new Dictionary<Guid, Guid>();` (source id → new id).
3. For the **root** node: `name = newName ?? DefaultCopyName(root.Name, siblingNamesInDest)` (see naming).
   Create via `dest.AddFolderAsync(name, destParentId, destIndex, root.IsQuery, root.IsSyndication)`;
   if `root.Query is not null` → `dest.SetQueryAsync(newId, root.Query)`. `idMap[root.SourceId] = newId`.
4. For each non-root node in order: parent = `idMap[node.SourceParentId!.Value]`; create with the
   node's own `Index` (preserve order) and the same name; `SetQueryAsync` when `Query != null`.
   Store `idMap[node.SourceId] = newId`.
5. Return `idMap[sourceId]`.

`CopyFolderAsync` is the `deep:false` degenerate path (root only).

`CopyToServiceAsync` is identical except creates go through the **destination** service and
`destParentId`/`destIndex` are in the destination id-space (caller resolves; UI uses
`destination.GetRawFolders()`). For `deep:false`, copy only the root.

### "(copy)" naming (same-parent collision avoidance)

```csharp
/// <summary>"X" → "X (copy)"; if taken, "X (copy 2)", "X (copy 3)", … among <paramref name="siblingNames"/>
/// (OrdinalIgnoreCase). Used so a same-parent duplicate doesn't path-collide with the source (the
/// reconcile planner matches by Path — a same-name copy would be treated as an Update of the source).</summary>
internal static string DefaultCopyName(string baseName, IReadOnlyCollection<string> siblingNames);
```

Only the **root** of a copy is renamed; descendants keep their names (their paths already differ
because the root segment changed). Cross-parent copies (Copy-to a different folder) only rename if a
sibling collision exists in the destination.

### `IWorkAreaScope` additions
**None required for copy.** One small change for root-move: today `Move(RemoteManager, Guid id,
Guid newParentId, int newIndex)` takes a non-nullable parent. inriver's `Move*WorkAreaFolder` signature
takes a non-nullable `Guid newParentId`, so **moving to root is only valid if inriver accepts
`Guid.Empty`** — verify against `remoting-surface.txt`. If it does **not**, root-move is unsupported;
`MoveFolderAsync(id, null, …)` should throw `NotSupportedException("Cannot move a folder to the root;
inriver requires a parent.")` and the VM's Outdent/MoveTo-root path must be guarded (it already is:
`CanOutdent` requires `Parent?.Parent`). **Decision: keep root-move guarded/unsupported in v1** to
avoid an unverified remoting call; do not add a scope member. (Indent/Outdent already cover re-parenting
within the existing rules.)

### Coverage gate: NO CHANGE
`AddSharedWorkAreaFolder`, `AddPersonalWorkAreaFolder`, `UpdateShared/PersonalWorkAreaQuery`,
`UpdateShared/PersonalWorkAreaFolderIndex`, `UpdateShared/PersonalWorkAreaFolderName`,
`MoveShared/PersonalWorkAreaFolder`, `DeleteShared/PersonalWorkAreaFolder` are **all** already in the
scan array (`RemotingSurfaceCoverageTests.cs:139-145`) and already invoked by the scope shims. No
`OutOfScope`→scan moves. Do **not** touch entity-membership writes (`AddEntitiesToWorkAreaFolder`,
`RemoveEntitiesFromWorkAreaFolder`) — folders are versioned without their entity lists by design.

---

## C. SHELL CONTRACT — `Shell.cs`

Follow the existing `…WorkArea…Async(..., string? personalUsername = null, CancellationToken ct = default)`
naming. `WorkAreas(client, personalUsername)` (L780) already maps null→shared, non-null→personal.

```csharp
// Single-folder duplicate / copy within one env+scope (shallow). Returns new id.
public Task<Guid> CopyWorkAreaFolderAsync(
    Guid sourceId, Guid? targetParentId, int index, string? newName = null,
    string? personalUsername = null, CancellationToken ct = default);

// Deep copy a subtree within one env+scope. Returns new root id.
public Task<Guid> CopyWorkAreaSubtreeAsync(
    Guid sourceRootId, Guid? targetParentId, int index, string? newName = null,
    string? personalUsername = null, CancellationToken ct = default);

// Cross-scope copy in the SAME connected env. source* / target* each select a scope:
// null => shared, non-null => that user's personal. deep=true copies the subtree.
// Returns the new root id in the target scope. NO SwitchEnvAsync (same env).
public Task<Guid> CopyWorkAreaAcrossScopeAsync(
    Guid sourceId, string? sourcePersonalUsername,
    Guid? targetParentId, string? targetPersonalUsername,
    int index, string? newName = null, bool deep = true, CancellationToken ct = default);
```

Implementation of `CopyWorkAreaAcrossScopeAsync`:
```csharp
var client = ConnectedClient();
var src = WorkAreas(client, sourcePersonalUsername);
var dst = WorkAreas(client, targetPersonalUsername);
return src.CopyToServiceAsync(sourceId, dst, targetParentId, index, newName, deep, ct);
```

`CopyWorkAreaFolderAsync` / `CopyWorkAreaSubtreeAsync` delegate to
`WorkAreas(ConnectedClient(), personalUsername).CopyFolderAsync / CopySubtreeAsync`.

Re-parent (move-to) uses the **existing** `MoveWorkAreaFolderAsync(Guid id, Guid newParentId,
int newIndex, …)` (L799) — once `WorkAreaService.MoveFolderAsync` is widened to `Guid?`, optionally
widen the Shell signature to `Guid? newParentId` too; **decision: keep Shell move as `Guid newParentId`
(non-null) for v1** (root-move unsupported). Reorder uses existing `SetWorkAreaIndexAsync` (L803).

No CompareViewModel Shell additions needed.

---

## D. MANAGE VIEWMODEL CONTRACT — `WorkAreaViewModel.cs` + `WorkAreaNode`

### Avalonia 11 TreeView multi-select reality
`Avalonia.Controls.TreeView` exposes `SelectedItems` (an `IList`) and `SelectionMode` (set
`AllowMultiple` via `SelectionMode="Multiple"` / `"Toggle"`). **`SelectedItems` is NOT reliably
two-way bindable** in Avalonia 11 (same constraint the compare grid hit). Plan:
- Set `TreeView.SelectionMode="Multiple"` in XAML.
- Add a tiny attached behavior **`TreeSelectionBehavior`** (new file
  `src/ModelMeister.Ui/Services/TreeSelectionBehavior.cs`) modeled on `GridSelectionBehavior`: an
  attached `Nodes` property bound to a VM `ObservableCollection<WorkAreaNode> SelectedNodes`; it
  subscribes to `TreeView.SelectionChanged` and mirrors `tree.SelectedItems` into `SelectedNodes`
  (and sets each node's `IsSelected`). One-way from control → VM is sufficient for bulk commands.
- Keep `Selected` (single, `SelectedItem`-bound) as the **primary** node for single-target commands
  and the detail pane. `SelectedNodes` drives bulk commands. When `SelectedNodes.Count <= 1`, bulk
  commands operate on `Selected`.

### New `[ObservableProperty]` fields on `WorkAreaViewModel`
```csharp
[ObservableProperty] private string _filterText = "";              // search box; OnFilterTextChanged -> ApplyFilter()
[ObservableProperty] private WorkAreaNode? _clipboardNode;         // in-app folder clipboard (single root)
[ObservableProperty] private ClipboardMode _clipboardMode;         // None | Cut | Copy
[ObservableProperty] private bool _hasClipboard;                   // ClipboardNode != null; NotifyCanExecuteChangedFor(Paste)
```
Add `public ObservableCollection<WorkAreaNode> SelectedNodes { get; } = [];` (mirrored by behavior).
Add `public enum ClipboardMode { None, Cut, Copy }` (top-level in the VM file or a small Models file).

`Busy`'s `[NotifyCanExecuteChangedFor(...)]` list gains: Duplicate, DuplicateSubtree, CopyTo, MoveTo,
Cut, Copy, Paste, ExpandAll, CollapseAll, DeleteSelected, MoveSelectedTo, CopySelectedTo, ExportSelected.
`Selected`'s notify list gains the single-target subset (Duplicate, DuplicateSubtree, CopyTo, MoveTo,
Cut, Copy).

### New `[RelayCommand]` methods (all accept an optional `WorkAreaNode? node` param so context-menu
items can target the right-clicked row; fall back to `Selected` when null)

```csharp
[RelayCommand(CanExecute = nameof(CanActOnTarget))]  Task DuplicateFolderAsync(WorkAreaNode? node)      // shallow, "(copy)" name, same parent
[RelayCommand(CanExecute = nameof(CanActOnTarget))]  Task DuplicateSubtreeAsync(WorkAreaNode? node)     // deep copy in place
[RelayCommand(CanExecute = nameof(CanActOnTarget))]  Task CopyToAsync(WorkAreaNode? node)               // pick dest folder, deep copy (same scope)
[RelayCommand(CanExecute = nameof(CanActOnTarget))]  Task MoveToAsync(WorkAreaNode? node)               // pick dest folder, re-parent
[RelayCommand(CanExecute = nameof(CanActOnTarget))]  void Cut(WorkAreaNode? node)                       // ClipboardMode=Cut
[RelayCommand(CanExecute = nameof(CanActOnTarget))]  void CopyNode(WorkAreaNode? node)                  // ClipboardMode=Copy (in-app)
[RelayCommand(CanExecute = nameof(CanPaste))]        Task PasteAsync(WorkAreaNode? targetNode)          // into target (or root)
[RelayCommand(CanExecute = nameof(CanActOnSelectionMulti))] Task DeleteSelectedAsync()                  // bulk delete
[RelayCommand(CanExecute = nameof(CanActOnSelectionMulti))] Task MoveSelectedToAsync()                  // bulk move via picker
[RelayCommand(CanExecute = nameof(CanActOnSelectionMulti))] Task CopySelectedToAsync()                  // bulk copy via picker
[RelayCommand(CanExecute = nameof(CanActOnSelectionMulti))] Task ExportSelectedAsync()                  // selected subtrees -> Excel
[RelayCommand]                                       void ExpandAll()
[RelayCommand]                                       void CollapseAll()
[RelayCommand]                                       void ClearFilter()                                  // FilterText=""
```
Cross-scope copy is reached through `CopyToAsync` / `CopySelectedToAsync` when the destination picker
also lets the admin choose a **scope/user** (see picker dialog §E). Single command, scope is a dialog
field, so we don't multiply commands per scope direction.

CanExecute predicates:
- `CanActOnTarget()` ⇒ `!Busy && IsConnected && (Selected is not null)` (context param checked in body).
- `CanActOnSelectionMulti()` ⇒ `!Busy && IsConnected && (SelectedNodes.Count > 0 || Selected is not null)`.
- `CanPaste()` ⇒ `!Busy && IsConnected && HasClipboard`.

### `WorkAreaNode` additions (make it observable)
`WorkAreaNode` becomes `public sealed partial class WorkAreaNode : ObservableObject` (CommunityToolkit
source-gen). Add:
```csharp
[ObservableProperty] private bool _isExpanded;     // two-way bound to TreeViewItem.IsExpanded; survives via re-apply after rebuild
[ObservableProperty] private bool _isVisible = true; // filter result; bound to TreeViewItem IsVisible
[ObservableProperty] private bool _isSelected;     // mirrored by TreeSelectionBehavior
[ObservableProperty] private bool _isCut;          // dimmed visual when this node is the Cut clipboard root
public int  Depth { get; set; }                    // set during BuildTree (breadcrumb + indentation)
public string IconKey => IsSyndication ? "IcoSyndication" : IsQuery ? "IcoSearch" : (IsExpanded ? "IcoFolderOpen" : "IcoFolder");
```
`IconKey` raises change notification when `IsSyndication`/`IsQuery`/`IsExpanded` change → add
`[NotifyPropertyChangedFor(nameof(IconKey))]` to `_isExpanded`. (IsQuery/IsSyndication are read-through
to the DTO and change only on rebuild, which re-creates the node — fine.)

### Tree-state preservation across `RefreshAsync` rebuild
`BuildTree` currently re-creates all nodes and re-selects by Path. Extend it to also **capture
expansion + filter state by Path before clear, and re-apply after build** (a `HashSet<string>
expandedPaths` snapshot). Set `Depth` while wiring parents. This makes ExpandAll/CollapseAll, filter,
and drag-drop survive the round-trip.

### Filter (`ApplyFilter`)
Recursive: a node `IsVisible` if its `Name`/`Path` contains `FilterText` (OrdinalIgnoreCase) OR any
descendant matches; matching nodes force ancestors visible and `IsExpanded=true`. Empty filter resets
all `IsVisible=true` and restores prior expansion snapshot.

### Detail-pane breadcrumb
Add `public IReadOnlyList<WorkAreaNode> SelectedBreadcrumb` computed from `Selected` walking `Parent`
to root (reversed); each segment is clickable → sets `Selected`.

---

## E. MANAGE VIEW (XAML) PLAN — `WorkAreaView.axaml` (+ `.axaml.cs`)

Keep `x:DataType="vm:WorkAreaViewModel"` and `x:Name="PageRoot"` (context-menu command rooting).
Compiled bindings throughout.

### Toolbar (`PageShell.Actions`) — extend existing
Order (ghost icon buttons + `Border Width="1" Background="{DynamicResource BorderFaint}"` separators):
1. user picker ComboBox (`IsVisible={Binding ShowUserPicker}`) — unchanged.
2. **Search box**: `TextBox` `Text="{Binding FilterText}"` `Watermark="Search folders…"` width ~180,
   with a clear "x" button (`ClearFilterCommand`, `IsVisible={Binding FilterText, Converter=...not-empty}`).
3. separator.
4. New top-level folder (`IcoPlus`) — `NewFolderCommand`.
5. New query folder (`IcoSearch`) — `NewQueryFolderCommand`.
6. separator.
7. **Expand all** (`IcoExpandAll`) — `ExpandAllCommand`.
8. **Collapse all** (`IcoChevRight` or new `IcoCollapseAll`) — `CollapseAllCommand`.
9. separator, then `<c:ExcelMenu>` and `<c:PageActions>` (unchanged).

### Tree header bar (left card) — extend existing row of icon buttons
Add **Duplicate** (`IcoDuplicate`), **Copy-to** (`IcoCopy`), **Move-to** (`IcoArrowRight`) between
Rename and the move-up/down group. Keep Move up / Move down / Delete (Danger). Indent/Outdent stay
context-menu+keyboard only (current behavior).

### TreeView
- `SelectionMode="Multiple"`, `svc:TreeSelectionBehavior.Nodes="{Binding SelectedNodes}"`,
  `SelectedItem="{Binding Selected, Mode=TwoWay}"`, `DragDrop.AllowDrop="True"`.
- `TreeViewItem` style binding: `IsExpanded="{Binding IsExpanded, Mode=TwoWay}"`,
  `IsVisible="{Binding IsVisible}"` (via `Styles` `<Style Selector="TreeViewItem">` setters; compiled
  binding requires the style's implicit DataType — use `ItemContainerTheme`/`Styles` with the node type
  or bind on the row Grid). **Per-kind icon**: replace the hardcoded `IcoFolder` `Path` with
  `Data="{Binding IconKey, Converter={x:Static svc:IconLookup}}"` (the existing
  `ResourceKeyToGeometryConverter`/`IconLookup` maps a key string → geometry). Cut nodes dim via
  `Opacity` triggered by `IsCut`.

### Right-click ContextMenu (full structure, on the row Grid; commands rooted via `#PageRoot`)
Each item passes `CommandParameter="{Binding}"` (the node) so it targets the right-clicked row. The
code-behind also selects the row on right-click `PointerPressed` (so `Selected` syncs) — add a
`PointerPressed` handler that, on right button, sets `tree.SelectedItem` to the row's DataContext if
not already in `SelectedNodes`.

```
New sub-folder            IcoFolder     -> NewSubfolderCommand            (param node)
New query folder          IcoSearch     -> NewQueryFolderCommand          (param node)
Define / Edit query…      IcoEdit       -> EditQueryCommand               (IsEnabled = IsQuery or always; if plain, "Define query…")
--------------------------------------------------
Rename                    IcoEdit  F2   -> RenameCommand                  (param node)
Duplicate                 IcoDuplicate Ctrl+D -> DuplicateSubtreeCommand  (param node)   [deep]
Duplicate (folder only)   IcoCopy       -> DuplicateFolderCommand         (param node)   [shallow]
Copy to…                  IcoCopy       -> CopyToCommand                  (param node)
Move to…                  IcoArrowRight -> MoveToCommand                  (param node)
--------------------------------------------------
Cut                       IcoCut   Ctrl+X -> CutCommand                   (param node)
Copy                      IcoCopy  Ctrl+C -> CopyNodeCommand              (param node)
Paste                     IcoPaste Ctrl+V -> PasteCommand  IsEnabled=HasClipboard (param node)
--------------------------------------------------
Move up                   IcoChevUp Alt+Up   -> MoveUpCommand
Move down                 IcoChevDown Alt+Dn -> MoveDownCommand
Indent (nest under prev)  Alt+Right -> IndentCommand
Outdent (to grandparent)  Alt+Left  -> OutdentCommand
--------------------------------------------------
Expand subtree            IcoExpandAll -> (node.ExpandSubtree via ExpandAll on node) 
Collapse subtree          -> (node)
--------------------------------------------------
Toggle syndication        IcoSyndication -> ToggleSyndicationCommand  IsVisible=ShowSyndicationToggle
--------------------------------------------------
Copy path                 -> CopyPathCommand (OS clipboard, param node)
Copy query JSON           IsEnabled=IsQuery -> CopyQueryCommand (OS clipboard, param node)
--------------------------------------------------
Delete            IcoTrash Delete  -> DeleteCommand (Danger, param node)
Delete selected   (IsVisible when SelectedNodes.Count>1) -> DeleteSelectedCommand
```

### Drag-and-drop (greenfield — follow `ModelView.axaml.cs` house pattern)
- `WorkAreaView.axaml.cs`: `AddHandler(DragDrop.DragOverEvent, OnDragOver)`,
  `AddHandler(DragDrop.DropEvent, OnDrop)`; start drag on `PointerPressed` + threshold →
  `DragDrop.DoDragDrop(e, dataObject, DragDropEffects.Move | DragDropEffects.Copy)` where `dataObject`
  carries the dragged `WorkAreaNode` (custom format key `"workarea-node"`).
- `OnDragOver`: `e.DragEffects = e.KeyModifiers.HasFlag(KeyModifiers.Control) ? Copy : Move`; reject
  drop onto self/descendant (cycle guard); show insert indicator (before/after sibling vs into folder
  by hit-test zone).
- `OnDrop`: resolve target node + index; **Ctrl ⇒** call `vm.PasteCopyOnto(target, dragged, index)`
  (deep copy via `CopyWorkAreaSubtreeAsync`), **else ⇒** `vm.MoveOnto(target, dragged, index)` (re-parent
  via `MoveWorkAreaFolderAsync` + reorder). Expose two internal VM methods for the code-behind to call
  (commands are awkward to call with two args from code-behind):
  `internal Task MoveOntoAsync(WorkAreaNode target, WorkAreaNode dragged, int index)` and
  `internal Task CopyOntoAsync(WorkAreaNode target, WorkAreaNode dragged, int index)`.

### Keyboard (`UserControl.KeyBindings`) — extend
Keep F2 / Delete / Alt+Up / Alt+Down / Alt+Right / Alt+Left. Add:
`Ctrl+X`→CutCommand, `Ctrl+C`→CopyNodeCommand, `Ctrl+V`→PasteCommand, `Ctrl+D`→DuplicateSubtreeCommand,
`Ctrl+F`→focus search (code-behind handler focusing the search TextBox), `Escape`→ClearFilter (or clear
selection), `Enter`→EditQueryCommand (open/define query). `Ctrl+A` selects all visible (code-behind →
`tree.SelectAll()` or behavior).

### Folder-picker chooser dialog (NEW — none exists)
For Copy-to / Move-to / bulk variants. Add to `DialogHost.cs`:
```csharp
public static Task<FolderPickResult?> PickFolderAsync(
    string title, IReadOnlyList<WorkAreaNode> tree, WorkAreaNode? exclude,
    bool allowScopeSwitch, IReadOnlyList<UserSummary> users, string? currentUser);
```
- New `FolderPickerViewModel` (VM with `bool? Result` + `event Action? Closed`, the standard pattern):
  hosts a read-only copy of the tree (excluding the `exclude` subtree to prevent copy-into-self),
  a "(root)" option, and — when `allowScopeSwitch` — a scope selector (Shared / Personal:user picker)
  so the same dialog covers cross-scope copy. Exposes `WorkAreaNode? SelectedTarget`,
  `bool ToShared`, `UserSummary? TargetUser`.
- New `FolderPickerDialog.axaml` (+ `.axaml.cs`) Window, `x:DataType="vm:FolderPickerViewModel"`,
  reusing the `QueryEditorDialog` window shape and the `WorkAreaView` TreeView template.
- `record FolderPickResult(Guid? TargetParentId, string? TargetPersonalUsername)` (top-level in
  DialogHost or a small Models file). `TargetParentId = SelectedTarget?.Id` (null = root);
  `TargetPersonalUsername = ToShared ? null : TargetUser?.Username`.

### Per-kind icons (new `Ico*` StreamGeometry keys — add to BOTH theme dicts in `App.axaml`)
`IcoFolderOpen`, `IcoDuplicate`, `IcoCut`, `IcoPaste`, `IcoSyndication`, `IcoCollapseAll` (if distinct).
Build fails if a `{DynamicResource}` key exists in only one theme dictionary — add to **Light (149-194)
and Dark (317-364)** both.

### Empty states
Keep the tree-level empty state. Add a **filtered-empty** state ("No folders match '{FilterText}'" +
"Clear filter" button → `ClearFilterCommand`), gated on `Tree.Count > 0 && no visible nodes`. Wire the
existing empty-state hint to a real "Create folder" button (`NewFolderCommand`).

---

## F. QUERY BUILDER PLAN — `WorkAreas/Query/*` + `QueryEditorViewModel` + `QueryEditorDialog`

The typed model is an **editing projection only**; the canonical wire format stays the serialized
`ComplexQuery` JSON on `WorkAreaFolderDto.QueryJson`. Everything must round-trip **byte-stably**
through `WorkAreaService.SerializeQuery`/`DeserializeQuery` (reconcile compares the JSON string for the
SetQuery op — a phantom diff breaks idempotency).

### F1. Fix the current data-loss bug FIRST (mandatory, regression risk)
Today `QueryEditorViewModel.BuildModel` rebuilds only the top-level `DataQuery` group and
`QueryMapper.ToComplexQuery` does not preserve `_original.DataQuery` — **an existing nested
`DataQuery.SubQuery` is silently flattened on first edit-save.** Fix: make `BuildModel` emit the full
group tree (after F2) AND make `ToComplexQuery` honor the rebuilt `DataQuery.SubQuery`. Add a
round-trip test covering a `SubQuery` (currently uncovered).

### F2. Nested And/Or groups (arbitrary UI depth, folded to inriver's single SubQuery chain)
- New recursive VM `GroupRowViewModel : ObservableObject` (in `QueryEditorViewModel.cs` or a new file):
  ```csharp
  [ObservableProperty] QJoin _join;
  ObservableCollection<CriterionRowViewModel> Criteria { get; }
  ObservableCollection<GroupRowViewModel> SubGroups { get; }
  [RelayCommand] void AddCriterion();  [RelayCommand] void AddGroup();  [RelayCommand] void Remove(object child);
  ```
- `QueryEditorViewModel.DataRoot : GroupRowViewModel` replaces the flat `DataCriteria`/`DataJoin`.
- Mapper: extend `QueryMapper.ToComplexQuery` / `ToModel` to fold the UI group tree into inriver's
  single `Query.SubQuery` chain and back. `CriteriaGroup.SubQuery` already exists; `QueryValidator.Walk`
  and `QueryDiff.Flatten` already traverse it. If the UI tree is n-ary but inriver only supports one
  SubQuery chain, fold deterministically (left-nest) and flag truly-unfoldable shapes as a validity
  warning rather than dropping. **Decision: support a single nested SubQuery level in v1** (matches
  inriver's actual capability; the recursive VM still gives a clean UX and is future-proof).
- View: recursive `TreeDataTemplate`/`ItemsControl` for groups (Avalonia 11 supports recursive
  `ItemsControl` via the same `DataType`).

### F3. Typed value pickers (CVL / bool / date / number / entity)
- Extend `QueryMetadata` (record) with:
  ```csharp
  IReadOnlyDictionary<string,string> FieldDataTypeById;            // fieldTypeId -> "String"|"CVL"|"Boolean"|"DateTime"|"Integer"|"Double"|"LocaleString"...
  IReadOnlyDictionary<string,string> CvlIdByFieldId;               // fieldTypeId -> cvlId (when DataType=CVL)
  IReadOnlyDictionary<string,IReadOnlyList<string>> CvlValuesByCvlId;
  ```
- `QueryMetadataService.Capture` adds `m.ModelService.GetAllFieldTypes()` already read → fill
  `FieldDataTypeById`/`CvlIdByFieldId`; add `m.ModelService.GetAllCVLs()` + per-CVL value reads
  (`GetCVLValuesForCVL`) for `CvlValuesByCvlId`. **Verify these are in/added to the coverage scan
  array** if newly called from a `src/` file — `GetAllCVLs`/`GetCVLValuesForCVL` are model reads; if
  they are currently `OutOfScope`, this is the one place that needs an `OutOfScope`→scan move
  (`RemotingSurfaceCoverageTests.cs`). **Action item: confirm and, if so, remove from `OutOfScope`
  AND add the exact `.GetAllCVLs(` / `.GetCVLValuesForCVL(` names to the scan array.**
- `CriterionRowViewModel` gains `string FieldDataType` (resolved from metadata on field change) and a
  `ValueKind` enum {Text, Cvl, Bool, Date, Number, Entity}; a XAML `DataTemplateSelector` (or a
  converter-driven `ContentControl`) swaps the value editor: `TextBox` → `ComboBox`(CVL values) /
  `CheckBox`(bool) / `DatePicker`(date) / `NumericUpDown`(number) / `AutoCompleteBox`(entity ids).
- Operator dropdown optionally filtered by datatype (e.g. `IsTrue`/`IsFalse` only for bool). Keep the
  full set available as a fallback; filtering is UX polish, not a wire change.

### F4. Entity-scoped field picker
Bind row field pickers to `_meta.FieldsFor(EntityTypeId)` (already exists, currently unused) and refresh
the candidate list on entity-type change (`OnEntityTypeIdChanged` hook exists).

### F5. Per-criterion Interval / Language
Surface `CriterionRowViewModel.Interval`/`Language` (mapper already round-trips them) so new/edited rows
don't drop them. Optional "advanced" expander per row.

### F6. System / segment / completeness fields
- System scalar fields already editable. Add **segment** editing: surface `SystemQuery.SegmentIds` +
  `SegmentIdsOperator` (only `ContainsAny`/`NotContainsAny` valid — restrict the operator combo).
  Requires `GetAllSegments` metadata (coverage scan check as in F3).
- Completeness: **keep preserve-only in v1** (round-tripped via `preserveFrom`,
  `HasUnsupportedParts`). Document as future work. Specification stays preserve-only.

### F7. Raw-JSON view + edit toggle
- `QueryEditorViewModel`: `[ObservableProperty] bool _showRawJson; [ObservableProperty] string _rawJson;
  [ObservableProperty] string? _rawJsonError;`. On toggle-to-raw: serialize `BuildModel`→`ToComplexQuery`
  →`SerializeQuery`. On edit-and-apply: `DeserializeQuery`→`ToModel`→repopulate rows (guard parse
  failure into `RawJsonError`, don't crash). A `TextBox AcceptsReturn` behind a tab/expander.

### F8. Live human-readable summary + optional preview count
- `[ObservableProperty] string _summary;` recomputed on any change via a `QueryDiff`-style key
  formatter over `BuildModel()` (pure, no env). 
- **Optional** preview: `[RelayCommand] Task PreviewAsync()` calling a new
  `Shell.PreviewWorkAreaQueryCountAsync(string queryJson)` → `m.DataService.Search(ComplexQuery,
  LoadLevel.Shallow).Count`. Keep the editor VM **env-free for unit tests**: inject the preview as an
  optional `Func<ComplexQuery, Task<int>>?` ctor param (null in tests). Coverage: `Search` is likely
  already scanned/OutOfScope — verify before wiring.

### F9. Validity warnings
Extend `QueryValidator.Validate` for datatype mismatches (IsTrue/IsFalse on non-bool, empty value on a
value-requiring operator, unknown CVL value). Already wired into the editor and Compare.

### F10. Byte-compat guard
Add a test: a query built through the new nested/typed editor → serialize → deserialize → re-serialize
yields an identical string (idempotent). This protects the reconcile SetQuery comparison.

---

## G. COMPARE / CLI / EXCEL / BACKUP PARITY

- **Compare** (`WorkAreaCompareViewModel`): no structural change. It already promotes by Path; the new
  editor's richer queries serialize to the same JSON, so `QueryDiff.Describe` and validity warnings keep
  working. Optionally surface `_rightMeta` CVL data so cross-env validity catches unknown CVL values.
- **CLI** (`workareas` command): **optional new verbs** `copy`/`duplicate` and `move`. Backend exists;
  zero coverage churn. If added: `duplicate --path <src> [--to <destParentPath>] [--name <newName>]
  [--shallow] [--user <u>]`, `move --path <src> --to <destParentPath> [--user <u>]`. Path→id resolution
  via `GetRawFolders()` + `PathOf`. Document but mark **stretch** (UI is the priority surface).
- **Excel** (`WorkAreaWorkbook`): no change to columns/sidecar — the 7 columns + `@file:` Query spill
  still round-trip. New queries are larger but the sidecar already handles >32k. Add a round-trip test
  with a nested-group query.
- **Backup** (`WorkAreasBackup`): no change — Capture/Restore reconcile by Path, never delete. Verify a
  backup of a scope containing copied/duplicated folders restores cleanly (Path-keyed).

---

## H. TEST PLAN (TreatWarningsAsErrors — fix every warning; `[InternalsVisibleTo]` already grants
internal access to `ModelMeister.Inriver.Tests`)

### `tests/ModelMeister.Inriver.Tests/WorkAreaCopyTests.cs` (NEW)
- `Flatten_subtree_is_parents_before_children_and_index_ordered`.
- `Deep_copy_clones_subtree_with_fresh_ids` — every cloned id ≠ source id; tree shape + relative Index
  preserved; each `Query` copied (faithful `ComplexQuery`).
- `Shallow_copy_clones_only_root`.
- `Default_copy_name_avoids_sibling_collision` — "X"→"X (copy)"→"X (copy 2)".
- `Same_parent_duplicate_changes_path_so_it_is_a_create_not_update` — proves path differs (guards the
  reconcile-treats-as-Update trap).
- `Cross_scope_copy_routes_through_destination_service` — using a **fake `IWorkAreaScope`** pair
  (in-memory shared + personal), assert personal target stamps `Username`; shared→personal drops
  syndication (SetSyndication no-op); personal→personal preserves tree.
- (Build copy on `AddFolderAsync`+`SetQueryAsync` only — assert no new remoting names via the existing
  coverage gate staying green.)

### `tests/ModelMeister.Inriver.Tests/WorkAreaPlanTests.cs` (EXTEND)
- Confirm copy plans never emit `Delete`; existing idempotency facts unaffected.

### `tests/ModelMeister.Inriver.Tests/QueryModelTests.cs` (EXTEND)
- `Nested_subquery_round_trips_byte_stable` (closes the current uncovered regression).
- `Typed_value_round_trips` for CVL/bool/date/number criteria.

### `tests/ModelMeister.Ui.Tests/QueryEditorViewModelTests.cs` (EXTEND)
- `Edit_save_preserves_existing_nested_group` (the F1 bug guard).
- `Raw_json_toggle_round_trips`.
- `Nested_group_builds_expected_complexquery`.

### `tests/ModelMeister.Ui.Tests/WorkAreaViewModelTests.cs` (NEW, if a Shell fake exists; else light)
- `Cut_then_paste_moves` / `Copy_then_paste_duplicates` against a fake Shell.
- `Filter_keeps_ancestors_and_expands_matches`.
- `Duplicate_uses_copy_suffix`.

### `tests/ModelMeister.Excel.Tests/WorkAreaWorkbookRoundTripTests.cs` (EXTEND)
- `Nested_query_round_trips_via_sidecar`.

---

## I. IMPLEMENTATION ORDER (strictly sequential; file-coherent so steps don't collide)

1. **Backend copy primitives** — edit `src/ModelMeister.Inriver/WorkAreas/WorkAreaService.cs`:
   add `FlattenSubtree`, `DefaultCopyName`, `CopyFolderAsync`, `CopySubtreeAsync`, `CopyToServiceAsync`;
   widen `MoveFolderAsync` to `Guid?` (guard null → throw NotSupported).
   *Compiles when:* `dotnet build ModelMeister.sln` green; no new remoting names.
2. **Backend tests** — add `tests/ModelMeister.Inriver.Tests/WorkAreaCopyTests.cs` (+ fake scope helpers).
   *Compiles when:* `dotnet test tests\ModelMeister.Inriver.Tests\…` passes; coverage gate green.
3. **Shell wrappers** — edit `src/ModelMeister.Ui/Services/Shell.cs`: add `CopyWorkAreaFolderAsync`,
   `CopyWorkAreaSubtreeAsync`, `CopyWorkAreaAcrossScopeAsync`.
   *Compiles when:* solution builds.
4. **VM model additions** — edit `src/ModelMeister.Ui/ViewModels/WorkAreaViewModel.cs`: make
   `WorkAreaNode` `: ObservableObject` with `IsExpanded/IsVisible/IsSelected/IsCut/Depth/IconKey`;
   add `enum ClipboardMode`, `SelectedNodes`, clipboard/filter `[ObservableProperty]`s; new
   `[RelayCommand]`s (Duplicate/DuplicateSubtree/CopyTo/MoveTo/Cut/CopyNode/Paste/DeleteSelected/
   MoveSelectedTo/CopySelectedTo/ExportSelected/ExpandAll/CollapseAll/ClearFilter) + CanExecute preds +
   `ApplyFilter`/`SelectedBreadcrumb`; extend `BuildTree` to preserve expansion + set Depth; add
   internal `MoveOntoAsync`/`CopyOntoAsync` for DnD.
   *Compiles when:* solution builds (commands may still be unbound in XAML).
5. **Selection + chooser infra** — add `src/ModelMeister.Ui/Services/TreeSelectionBehavior.cs`;
   add icons to **both** theme dicts in `App.axaml`; add `FolderPickerViewModel` +
   `Views/FolderPickerDialog.axaml(.cs)` + `DialogHost.PickFolderAsync` + `FolderPickResult`.
   *Compiles when:* solution builds; dialog opens from a temporary test hook.
6. **Manage view wiring** — edit `Views/WorkAreaView.axaml` (+ `.axaml.cs`): toolbar search/expand/
   collapse, header copy/move/duplicate buttons, full ContextMenu, per-kind icon binding, `IsExpanded`/
   `IsVisible` item bindings, `SelectionMode="Multiple"` + behavior, DnD handlers, new KeyBindings,
   filtered-empty state, breadcrumb. Keep `x:DataType`.
   *Compiles when:* solution builds; manual smoke: right-click shows full menu, duplicate/copy-to/cut-
   paste/expand/collapse/search work; DnD reparents (Ctrl=copy).
7. **Query builder** — edit `WorkAreas/Query/QueryModel.cs`, `QueryMapper.cs`, `QueryMetadata.cs`,
   `QueryMetadataService.cs`, `QueryValidator.cs`; rewrite `QueryEditorViewModel.cs` (GroupRowViewModel,
   typed value kinds, raw-JSON, summary, entity-scoped fields, Interval/Language) + `QueryEditorDialog.axaml`.
   **Do F1 (SubQuery preservation) first within this step.** If metadata adds `GetAllCVLs`/
   `GetCVLValuesForCVL`/`GetAllSegments`, do the `OutOfScope`→scan move in
   `RemotingSurfaceCoverageTests.cs` in the same step.
   *Compiles when:* solution builds; query editor round-trips byte-stable; coverage gate green.
8. **Parity + CLI/Excel/Backup** — optional CLI `copy`/`duplicate`/`move` verbs in
   `Cli/Commands/WorkAreasCommand.cs` + `Program.cs`; verify Excel/Backup round-trip (no code change
   expected). 
   *Compiles when:* solution builds; CLI verbs (if added) run against the example model.
9. **Tests** — extend `WorkAreaPlanTests`, `QueryModelTests`, `QueryEditorViewModelTests`,
   `WorkAreaWorkbookRoundTripTests`; add `WorkAreaViewModelTests`.
   *Compiles when:* `dotnet test ModelMeister.sln` all green.

---

## J. RISKS & GOTCHAS

- **TreatWarningsAsErrors**: every unused field, nullable mismatch, or missing `x:DataType` reference
  fails CI. Build after each step.
- **Compiled bindings**: every new view/dialog must set `x:DataType`; context-menu commands inside
  `TreeDataTemplate` must root via `#PageRoot.((vm:WorkAreaViewModel)DataContext).XCommand`; the recursive
  group template needs an explicit `DataType="vm:GroupRowViewModel"`.
- **Avalonia 11 TreeView multi-select**: `SelectedItems` not reliably two-way bindable — use
  `TreeSelectionBehavior` (control→VM mirror) + `SelectionMode="Multiple"`. `IsExpanded`/`IsVisible`
  on `TreeViewItem` need a style/`ItemContainerTheme` binding, not a direct attribute on the data row.
- **Coverage gate**: copy/move/duplicate add **no** remoting calls (all reused). The **only** risk is
  the query-metadata expansion (F3/F6) if it calls `GetAllCVLs`/`GetCVLValuesForCVL`/`GetAllSegments`
  for the first time — then do the exact `OutOfScope` removal **and** scan-array addition together.
- **Idempotency contract**: the typed query model is an editing projection; it MUST round-trip
  byte-stably through `SerializeQuery`/`DeserializeQuery` (whitespace-tolerant for expressions only).
  The reconcile SetQuery op compares JSON strings — a phantom diff re-writes queries on every promote.
  Pin with the F10 byte-compat test. Do not change `QueryJsonOptions` (`WhenWritingDefault` is
  load-bearing for `SegmentIdsOperator`).
- **Cross-scope Username**: never plumb `Username` manually — the destination scope's `Add` stamps it.
  Cross-scope copy = source service reads, destination service creates. Shared→personal silently drops
  syndication (correct; `SetSyndication` no-ops on personal).
- **Name-clash on duplicate**: same-parent duplicate MUST rename the root (`DefaultCopyName`), else the
  Path matches the source and reconcile treats it as an Update (no new folder). Only the root renames.
- **Deep-copy ordering**: always create parents before children (`FlattenSubtree` depth-then-index);
  thread minted parent ids via the source→new id map. Preserve each node's own `Index`.
- **Convert query→plain is impossible** (no inriver clear-query). Surface the limitation (disabled
  item / tooltip), never attempt it — copy/duplicate always preserve `IsQuery`/`Query`.
- **F1 silent data loss**: shipping the new editor without the SubQuery-preservation fix would flatten
  existing nested saved searches on first edit-save. F1 is mandatory and must precede any nested-group UX.
- **Right-click vs selection**: context-menu mutating items currently act on `Selected`, not the
  right-clicked node. New items pass `CommandParameter={Binding}` AND the code-behind selects the row on
  right-click — both, to keep `Selected`/detail-pane in sync.
- **Detail-pane / DnD optimistic UI**: `WorkAreaNode` is now observable, but mutations still do a full
  `Shell.List…` + rebuild; preserve expansion/selection by Path snapshot (BuildTree change) so DnD and
  expand state don't reset on every op.
