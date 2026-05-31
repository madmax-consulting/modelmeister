using System.Text.Json;
using System.Text.Json.Serialization;
using inRiver.Remoting.Query;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using IriverWorkAreaFolder = inRiver.Remoting.Objects.WorkAreaFolder;

namespace ModelMeister.Inriver.WorkAreas;

/// <summary>
/// Wraps the Remoting <c>SharedWorkAreaFolder</c> surface into a coherent API for the WorkArea page,
/// the CLI and Excel round-trip. Folders form a tree (<see cref="WorkAreaFolderDto.ParentId"/>); a
/// folder may carry a saved search (<c>ComplexQuery</c>), which we treat as an opaque JSON blob for
/// display/Excel and copy verbatim (as the live object) when promoting between environments.
/// </summary>
/// <remarks>
/// Personal work areas are deliberately out of scope — only shared folders are model-adjacent enough to
/// version and promote. The query JSON is faithful enough to inspect/diff; criteria whose <c>Value</c> is
/// a non-string scalar may not survive an Excel round-trip (documented limitation), so env→env promote
/// passes the live <c>ComplexQuery</c> object instead of going through JSON.
/// </remarks>
public sealed class WorkAreaService
{
    /// <summary>
    /// Opaque-but-readable serialization of the saved query. Enums render as names.
    /// <para>Uses <see cref="JsonIgnoreCondition.WhenWritingDefault"/> (not <c>WhenWritingNull</c>) on
    /// purpose: some inriver query setters validate their input and <b>reject their own default</b> — most
    /// notably <c>SystemQuery.SegmentIdsOperator</c>, whose getter returns <c>Equal</c> but whose setter only
    /// accepts <c>ContainsAny</c>/<c>NotContainsAny</c>. Writing default-valued operators therefore produces
    /// JSON that throws on deserialization (the query is silently lost). Omitting defaults sidesteps that —
    /// a field left at its default deserializes back to the same default without invoking the setter.</para>
    /// </summary>
    public static readonly JsonSerializerOptions QueryJsonOptions = new()
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingDefault,
        Converters = { new JsonStringEnumConverter() },
        WriteIndented = false,
    };

    private readonly InriverClient _client;
    private readonly IWorkAreaScope _scope;
    private readonly ILogger _log;

    /// <summary>Construct against the <b>shared</b> work-area surface (the default).</summary>
    public WorkAreaService(InriverClient client, ILogger<WorkAreaService>? log = null)
        : this(client, new SharedWorkAreaScope(), log) { }

    internal WorkAreaService(InriverClient client, IWorkAreaScope scope, ILogger<WorkAreaService>? log = null)
    {
        _client = client;
        _scope = scope;
        _log = (ILogger?)log ?? NullLogger.Instance;
    }

    /// <summary>Service bound to the shared work-area folders.</summary>
    public static WorkAreaService ForShared(InriverClient client, ILogger<WorkAreaService>? log = null)
        => new(client, new SharedWorkAreaScope(), log);

    /// <summary>Service bound to <paramref name="username"/>'s personal work-area folders.</summary>
    public static WorkAreaService ForPersonal(InriverClient client, string username, ILogger<WorkAreaService>? log = null)
        => new(client, new PersonalWorkAreaScope(username), log);

    // ---------------- Reads ----------------

    /// <summary>List all shared work-area folders as flat DTOs (each carries its computed tree path).</summary>
    public IReadOnlyList<WorkAreaFolderDto> List() => ToDtos(GetRawFolders());

    /// <summary>Raw folders straight from inriver — keeps the live <c>ComplexQuery</c> for faithful promote.</summary>
    public IReadOnlyList<IriverWorkAreaFolder> GetRawFolders() =>
        _client.Read(m => _scope.GetAll(m));

    private List<WorkAreaFolderDto> ToDtos(IReadOnlyList<IriverWorkAreaFolder> folders)
    {
        var byId = folders.ToDictionary(f => f.Id);
        return folders
            .Select(f => new WorkAreaFolderDto
            {
                Id = f.Id,
                Name = f.Name ?? "",
                ParentId = f.ParentId,
                Index = f.Index,
                IsQuery = f.IsQuery,
                IsSyndication = f.IsSyndication,
                QueryJson = SerializeQuery(f.Query),
                Username = f.Username ?? _scope.OwnerUsername,
                Path = PathOf(f, byId),
            })
            .OrderBy(d => d.Path, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    /// <summary>Parent-chain of names joined by '/', e.g. <c>Marketing/Launch 2026</c>. Stable across envs.</summary>
    private static string PathOf(IriverWorkAreaFolder f, IReadOnlyDictionary<Guid, IriverWorkAreaFolder> byId)
    {
        var names = new List<string>();
        var current = f;
        var guard = 0;
        while (current is not null && guard++ < 64)
        {
            names.Add(current.Name ?? "");
            current = current.ParentId is { } pid && byId.TryGetValue(pid, out var parent) ? parent : null;
        }
        names.Reverse();
        return string.Join('/', names);
    }

    // ---------------- Query (de)serialization ----------------

    public static string? SerializeQuery(ComplexQuery? query)
    {
        if (query is null) return null;
        try { return JsonSerializer.Serialize(query, QueryJsonOptions); }
        catch { return null; }
    }

    public static ComplexQuery? DeserializeQuery(string? json)
    {
        if (string.IsNullOrWhiteSpace(json)) return null;
        try { return JsonSerializer.Deserialize<ComplexQuery>(json, QueryJsonOptions); }
        catch { return null; }
    }

    // ---------------- Manage (single-env CRUD) ----------------

    /// <summary>Create a folder under an optional parent. Returns the created folder's id.</summary>
    public async Task<Guid> CreateFolderAsync(string name, Guid? parentId, int index, bool isQuery, CancellationToken ct = default)
    {
        var dto = new IriverWorkAreaFolder { Name = name, ParentId = parentId, Index = index, IsQuery = isQuery };
        var created = await _client.WriteAsync(m => _scope.Add(m, dto), ct).ConfigureAwait(false);
        return created.Id;
    }

    public Task RenameFolderAsync(Guid id, string name, CancellationToken ct = default) =>
        _client.WriteAsync(m => _scope.Rename(m, id, name), ct);

    /// <summary>Re-parent a folder under <paramref name="newParentId"/> at <paramref name="newIndex"/>.
    /// A <see langword="null"/> parent (root placement) is not supported in v1 — inriver's move requires a
    /// non-null parent (<see cref="IWorkAreaScope.Move"/>) — and throws <see cref="NotSupportedException"/>.</summary>
    public Task MoveFolderAsync(Guid id, Guid? newParentId, int newIndex, CancellationToken ct = default)
    {
        if (newParentId is not { } parentId)
            throw new NotSupportedException(
                "Cannot move a folder to the root; inriver move requires a parent folder. Root placement isn't supported yet.");
        return _client.WriteAsync(m => _scope.Move(m, id, parentId, newIndex), ct);
    }

    /// <summary>Set a folder's index among its siblings (reorder within the same parent).</summary>
    public Task SetIndexAsync(Guid id, int newIndex, CancellationToken ct = default) =>
        _client.WriteAsync(m => _scope.SetIndex(m, id, newIndex), ct);

    /// <summary>Toggle the syndication flag. No-op for scopes that don't support it (personal).</summary>
    public Task SetSyndicationAsync(Guid id, bool isSyndication, CancellationToken ct = default) =>
        _scope.SupportsSyndication
            ? _client.WriteAsync(m => _scope.SetSyndication(m, id, isSyndication), ct)
            : Task.CompletedTask;

    public Task SetQueryAsync(Guid id, ComplexQuery query, CancellationToken ct = default) =>
        _client.WriteAsync(m => _scope.SetQuery(m, id, query), ct);

    public Task DeleteFolderAsync(Guid id, CancellationToken ct = default) =>
        _client.WriteAsync(m => _scope.Delete(m, id), ct);

    /// <summary>Create a folder with the full attribute set (incl. <c>IsSyndication</c>) used by reconcile.
    /// Returns the new folder's id.</summary>
    internal async Task<Guid> AddFolderAsync(string name, Guid? parentId, int index, bool isQuery, bool isSyndication, CancellationToken ct = default)
    {
        var dto = new IriverWorkAreaFolder { Name = name, ParentId = parentId, Index = index, IsQuery = isQuery, IsSyndication = isSyndication };
        var created = await _client.WriteAsync(m => _scope.Add(m, dto), ct).ConfigureAwait(false);
        return created.Id;
    }

    // ---------------- Copy / duplicate (shallow, deep, cross-scope) ----------------

    /// <summary>One folder to clone, captured from the live source subtree. Pure data — no inriver objects
    /// beyond the live <see cref="ComplexQuery"/> we copy verbatim. Produced by <see cref="FlattenSubtree"/>
    /// in parents-before-children order (depth then <see cref="Index"/>).</summary>
    internal readonly record struct CopyNode(
        Guid SourceId, Guid? SourceParentId, string Name, int Index, bool IsQuery, bool IsSyndication, ComplexQuery? Query, int Depth);

    /// <summary>Flatten the subtree rooted at <paramref name="rootId"/> into <see cref="CopyNode"/>s, ordered
    /// parents-before-children (depth ascending, then <see cref="CopyNode.Index"/>). The root has
    /// <c>Depth == 0</c>. Returns empty when the root id isn't present.</summary>
    internal static IReadOnlyList<CopyNode> FlattenSubtree(IReadOnlyList<IriverWorkAreaFolder> live, Guid rootId)
    {
        var byId = live.ToDictionary(f => f.Id);
        if (!byId.ContainsKey(rootId)) return [];

        var childrenByParent = live
            .Where(f => f.ParentId is { } pid && byId.ContainsKey(pid))
            .GroupBy(f => f.ParentId!.Value)
            .ToDictionary(g => g.Key, g => g.OrderBy(f => f.Index).ToList());

        var nodes = new List<CopyNode>();
        // A malformed live tree (self-parent, or a cycle among descendants) would otherwise re-enqueue the
        // same folders forever; the `visited` set bounds the walk to the true node count and keeps the output
        // duplicate-free so the clone never issues bogus, repeated folder-create writes. The 100k counter
        // stays as defence-in-depth.
        var visited = new HashSet<Guid>();
        var queue = new Queue<(IriverWorkAreaFolder folder, int depth)>();
        queue.Enqueue((byId[rootId], 0));
        var guard = 0;
        while (queue.Count > 0 && guard++ < 100_000)
        {
            var (folder, depth) = queue.Dequeue();
            if (!visited.Add(folder.Id)) continue; // already emitted — a cycle or self-parent reached it again
            nodes.Add(new CopyNode(
                folder.Id, folder.ParentId, folder.Name ?? "", folder.Index,
                folder.IsQuery, folder.IsSyndication, folder.Query, depth));
            if (childrenByParent.TryGetValue(folder.Id, out var kids))
                foreach (var kid in kids)
                    if (kid.Id != folder.Id && !visited.Contains(kid.Id))
                        queue.Enqueue((kid, depth + 1));
        }

        // Stable parents-before-children ordering: BFS already yields depth-ascending; re-sort to be explicit.
        return nodes.OrderBy(n => n.Depth).ThenBy(n => n.Index).ToList();
    }

    /// <summary>Pick a non-colliding "(copy)" name. <c>"X" -&gt; "X (copy)"</c>, then <c>"X (copy 2)"</c>,
    /// <c>"X (copy 3)"</c>… skipping any names already in <paramref name="siblingNames"/> (case-insensitive).</summary>
    internal static string DefaultCopyName(string baseName, IReadOnlyCollection<string> siblingNames)
    {
        var taken = new HashSet<string>(siblingNames, StringComparer.OrdinalIgnoreCase);
        var first = $"{baseName} (copy)";
        if (!taken.Contains(first)) return first;
        for (var n = 2; ; n++)
        {
            var candidate = $"{baseName} (copy {n})";
            if (!taken.Contains(candidate)) return candidate;
        }
    }

    /// <summary>Shallow copy: clone just <paramref name="sourceId"/> (no children) under
    /// <paramref name="newParentId"/> at <paramref name="newIndex"/>, preserving its query/flags. Returns the
    /// new folder's id.</summary>
    public Task<Guid> CopyFolderAsync(Guid sourceId, Guid? newParentId, int newIndex, string? newName = null, CancellationToken ct = default)
        => CopyToServiceAsync(sourceId, this, newParentId, newIndex, newName, deep: false, ct);

    /// <summary>Deep copy: clone <paramref name="sourceId"/> and its whole subtree under
    /// <paramref name="newParentId"/> at <paramref name="newIndex"/>, preserving each node's index, query and
    /// flags. Only the root is renamed. Returns the new root's id.</summary>
    public Task<Guid> CopySubtreeAsync(Guid sourceId, Guid? newParentId, int newIndex, string? newName = null, CancellationToken ct = default)
        => CopyToServiceAsync(sourceId, this, newParentId, newIndex, newName, deep: true, ct);

    /// <summary>Clone the source subtree (read from <b>this</b> service) into <paramref name="destination"/>
    /// (cross-scope: shared↔personal, personal↔personal). <paramref name="deep"/> <see langword="false"/>
    /// copies only the root folder. Returns the new root's id in the destination.</summary>
    public async Task<Guid> CopyToServiceAsync(
        Guid sourceId, WorkAreaService destination, Guid? destParentId, int destIndex,
        string? newName = null, bool deep = true, CancellationToken ct = default)
    {
        var raw = GetRawFolders();
        var nodes = FlattenSubtree(raw, sourceId);
        if (nodes.Count == 0)
            throw new InvalidOperationException($"Work-area folder {sourceId} was not found.");

        var root = nodes[0];
        if (!deep)
            nodes = [root];

        // Name the root. When no explicit name is given AND the copy lands among the source's OWN siblings
        // (same service, same parent as the source), de-collide with "(copy)"; otherwise keep the original.
        string rootName;
        if (newName is not null)
        {
            rootName = newName;
        }
        else if (ReferenceEquals(destination, this) && destParentId == root.SourceParentId)
        {
            var siblingNames = raw
                .Where(f => f.ParentId == destParentId && f.Id != sourceId)
                .Select(f => f.Name ?? "")
                .ToList();
            rootName = DefaultCopyName(root.Name, siblingNames);
        }
        else
        {
            rootName = root.Name;
        }

        // Walk parents-before-children, minting fresh ids and resolving each new parent via the id map.
        var newIdBySource = new Dictionary<Guid, Guid>();
        Guid newRootId = Guid.Empty;
        foreach (var node in nodes)
        {
            ct.ThrowIfCancellationRequested();
            var isRoot = node.SourceId == root.SourceId;
            var name = isRoot ? rootName : node.Name;
            var parentId = isRoot
                ? destParentId
                : node.SourceParentId is { } sp && newIdBySource.TryGetValue(sp, out var mapped) ? mapped : destParentId;
            var index = isRoot ? destIndex : node.Index;

            var newId = await destination
                .AddFolderAsync(name, parentId, index, node.IsQuery, node.IsSyndication, ct)
                .ConfigureAwait(false);
            newIdBySource[node.SourceId] = newId;
            if (isRoot) newRootId = newId;

            if (node.IsQuery && node.Query is not null)
                await destination.SetQueryAsync(newId, node.Query, ct).ConfigureAwait(false);
        }

        return newRootId;
    }

    // ---------------- Promote / apply (reconcile by path) ----------------

    /// <summary>Promote shared folders from another env (faithful: carries the live query objects).</summary>
    public Task<WorkAreaApplyResult> ApplyAsync(IReadOnlyList<IriverWorkAreaFolder> source, bool allowDeletes, CancellationToken ct = default)
    {
        var byId = source.ToDictionary(f => f.Id);
        var desired = source.Select(f => new DesiredFolder(
            Path: PathOf(f, byId),
            ParentPath: f.ParentId is { } pid && byId.TryGetValue(pid, out var p) ? PathOf(p, byId) : null,
            Name: f.Name ?? "",
            Index: f.Index,
            IsQuery: f.IsQuery,
            IsSyndication: f.IsSyndication,
            Query: f.Query)).ToList();
        return RunAsync(PlanFromDesired(desired, allowDeletes), ct);
    }

    /// <summary>Apply folders described by DTOs (e.g. an Excel import). Query comes from <see cref="WorkAreaFolderDto.QueryJson"/>.</summary>
    public Task<WorkAreaApplyResult> ApplyAsync(IReadOnlyList<WorkAreaFolderDto> source, bool allowDeletes, CancellationToken ct = default)
        => RunAsync(Plan(source, allowDeletes), ct);

    /// <summary>Diff DTO folders against the live env into an ordered reconcile session (parents-before-
    /// children, deletes deepest-first). Pure read — no writes. The workflow drives
    /// <see cref="WorkAreaReconcileSession.Actions"/> one at a time for per-row progress;
    /// <see cref="ApplyAsync(IReadOnlyList{WorkAreaFolderDto}, bool, CancellationToken)"/> is a thin wrapper.</summary>
    public WorkAreaReconcileSession Plan(IReadOnlyList<WorkAreaFolderDto> source, bool allowDeletes)
    {
        var byId = source.Where(d => d.Id != Guid.Empty).ToDictionary(d => d.Id);
        string? ParentPath(WorkAreaFolderDto d)
        {
            if (d.ParentId is { } pid && byId.TryGetValue(pid, out var parent)) return parent.Path;
            // Fall back to the path minus the last segment when the parent id isn't in the set.
            var slash = d.Path.LastIndexOf('/');
            return slash <= 0 ? null : d.Path[..slash];
        }
        var desired = source.Select(d => new DesiredFolder(
            Path: d.Path,
            ParentPath: ParentPath(d),
            Name: d.Name,
            Index: d.Index,
            IsQuery: d.IsQuery,
            IsSyndication: d.IsSyndication,
            Query: DeserializeQuery(d.QueryJson))).ToList();
        return PlanFromDesired(desired, allowDeletes);
    }

    internal sealed record DesiredFolder(
        string Path, string? ParentPath, string Name, int Index, bool IsQuery, bool IsSyndication, ComplexQuery? Query);

    /// <summary>Build the ordered reconcile session from already-resolved desired folders.</summary>
    private WorkAreaReconcileSession PlanFromDesired(List<DesiredFolder> desired, bool allowDeletes)
    {
        var (idByPath, actions) = BuildPlan(GetRawFolders(), desired, allowDeletes);
        return new WorkAreaReconcileSession(this, idByPath, actions);
    }

    /// <summary>Pure reconcile: given the live folders + desired folders, produce the seed path→id map and
    /// the ordered action list (creates/updates parents-before-children by depth then index; deletes
    /// deepest-first). No I/O — unit-testable.</summary>
    internal static (Dictionary<string, Guid> idByPath, List<WorkAreaAction> actions) BuildPlan(
        IReadOnlyList<IriverWorkAreaFolder> live, List<DesiredFolder> desired, bool allowDeletes)
    {
        var targetById = live.ToDictionary(f => f.Id);
        var idByPath = targetById.Values.ToDictionary(f => PathOf(f, targetById), f => f.Id, StringComparer.OrdinalIgnoreCase);

        var actions = new List<WorkAreaAction>();
        // Parents before children so a child's ParentId resolves to an already-created folder.
        foreach (var d in desired.OrderBy(d => d.Path.Count(c => c == '/')).ThenBy(d => d.Index))
        {
            if (idByPath.TryGetValue(d.Path, out var existingId))
            {
                // A path-matched folder shares its parent chain with the live folder (Path encodes the
                // parent names), so an Update never re-parents — it only reorders, flips syndication, or
                // re-saves the query. Carry the live folder's current state so the session can diff and
                // skip no-op writes (idempotency).
                var liveFolder = targetById.GetValueOrDefault(existingId);
                actions.Add(new WorkAreaAction(
                    WorkAreaActionKind.Update, d.Path, d.ParentPath, d.Name, d.Index, d.IsQuery, d.IsSyndication, d.Query,
                    existingId, liveFolder?.Name,
                    CurrentIndex: liveFolder?.Index ?? 0,
                    CurrentIsSyndication: liveFolder?.IsSyndication ?? false,
                    CurrentQueryJson: SerializeQuery(liveFolder?.Query)));
            }
            else
                actions.Add(new WorkAreaAction(
                    WorkAreaActionKind.Create, d.Path, d.ParentPath, d.Name, d.Index, d.IsQuery, d.IsSyndication, d.Query,
                    Guid.Empty, null));
        }

        if (allowDeletes)
        {
            var desiredPaths = desired.Select(d => d.Path).ToHashSet(StringComparer.OrdinalIgnoreCase);
            // Deepest paths first so children are removed before parents.
            foreach (var (path, id) in idByPath
                         .Where(kv => !desiredPaths.Contains(kv.Key))
                         .OrderByDescending(kv => kv.Key.Count(c => c == '/')))
                actions.Add(new WorkAreaAction(
                    WorkAreaActionKind.Delete, path, null, targetById.GetValueOrDefault(id)?.Name ?? "",
                    0, false, false, null, id, null));
        }

        return (idByPath, actions);
    }

    /// <summary>Run a session as a batch (the legacy whole-set apply). Mirrors the per-row loop the
    /// workflow drives, accumulating the same tallies.</summary>
    private async Task<WorkAreaApplyResult> RunAsync(WorkAreaReconcileSession session, CancellationToken ct)
    {
        var result = new WorkAreaApplyResult();
        foreach (var action in session.Actions)
        {
            ct.ThrowIfCancellationRequested();
            try
            {
                await session.ExecuteAsync(action, ct).ConfigureAwait(false);
                switch (action.Kind)
                {
                    case WorkAreaActionKind.Create: result.Created++; break;
                    case WorkAreaActionKind.Update: result.Updated++; break;
                    case WorkAreaActionKind.Delete: result.Deleted++; break;
                }
            }
            catch (Exception ex)
            {
                _log.LogWarning(ex, "WorkArea {Kind} failed for path {Path}", action.Kind, action.Path);
                result.Failed++;
            }
        }
        return result;
    }
}

/// <summary>What a planned single-folder action does.</summary>
public enum WorkAreaActionKind { Create, Update, Delete }

/// <summary>One reconcile action produced by <see cref="WorkAreaService.Plan"/>. <see cref="LiveId"/> is the
/// existing folder's id for update/delete (empty for create); the <c>Current*</c> fields carry the live
/// folder's state for an update so the session applies only what actually changed (idempotency).</summary>
public sealed record WorkAreaAction(
    WorkAreaActionKind Kind, string Path, string? ParentPath, string Name,
    int Index, bool IsQuery, bool IsSyndication, ComplexQuery? Query, Guid LiveId, string? CurrentName,
    int CurrentIndex = 0, bool CurrentIsSyndication = false, string? CurrentQueryJson = null);

/// <summary>A single Remoting write implied by a <see cref="WorkAreaAction"/>.</summary>
public enum WorkAreaOp { Create, Rename, SetIndex, SetSyndication, SetQuery, Delete }

/// <summary>
/// A stateful reconcile run: holds the path→id map (seeded from the live env, then mutated as folders
/// are created so a child create resolves its freshly-created parent's id). Execute the
/// <see cref="Actions"/> in order — they are already sorted parents-before-children, deletes deepest-first.
/// </summary>
public sealed class WorkAreaReconcileSession
{
    private readonly WorkAreaService _svc;
    private readonly Dictionary<string, Guid> _idByPath;

    internal WorkAreaReconcileSession(WorkAreaService svc, Dictionary<string, Guid> idByPath, IReadOnlyList<WorkAreaAction> actions)
    {
        _svc = svc;
        _idByPath = idByPath;
        Actions = actions;
    }

    /// <summary>The ordered actions to execute.</summary>
    public IReadOnlyList<WorkAreaAction> Actions { get; }

    /// <summary>
    /// The writes an action implies, in order. Pure — drives <see cref="ExecuteAsync"/> and is the
    /// unit-test seam for idempotency: a Create yields <c>Create</c> (+ <c>SetQuery</c> if it has a saved
    /// search); an Update yields only the sub-operations whose value actually differs from live, so a
    /// converged plan produces an empty list and issues zero writes; a Delete yields <c>Delete</c>.
    /// </summary>
    public static IReadOnlyList<WorkAreaOp> ComputeOps(WorkAreaAction a)
    {
        switch (a.Kind)
        {
            case WorkAreaActionKind.Create:
            {
                var ops = new List<WorkAreaOp> { WorkAreaOp.Create };
                if (a.IsQuery && a.Query is not null) ops.Add(WorkAreaOp.SetQuery);
                return ops;
            }
            case WorkAreaActionKind.Update:
            {
                // Note: a folder's query-ness can be turned ON (setting a query promotes a plain folder to a
                // query folder via SetQuery below) but not OFF — inriver has no "clear query" call. So a
                // desired plain folder over a live query folder reconciles every field except that flag.
                var ops = new List<WorkAreaOp>();
                if (!string.Equals(a.CurrentName, a.Name, StringComparison.Ordinal)) ops.Add(WorkAreaOp.Rename);
                if (a.CurrentIndex != a.Index) ops.Add(WorkAreaOp.SetIndex);
                if (a.CurrentIsSyndication != a.IsSyndication) ops.Add(WorkAreaOp.SetSyndication);
                if (a.IsQuery && a.Query is not null
                    && !string.Equals(WorkAreaService.SerializeQuery(a.Query), a.CurrentQueryJson, StringComparison.Ordinal))
                    ops.Add(WorkAreaOp.SetQuery);
                return ops;
            }
            case WorkAreaActionKind.Delete:
                return [WorkAreaOp.Delete];
            default:
                return [];
        }
    }

    /// <summary>Execute one action, mutating the path→id map on create so later children resolve.</summary>
    public async Task ExecuteAsync(WorkAreaAction a, CancellationToken ct = default)
    {
        foreach (var op in ComputeOps(a))
        {
            ct.ThrowIfCancellationRequested();
            switch (op)
            {
                case WorkAreaOp.Create:
                {
                    Guid? parentId = a.ParentPath is null ? null
                        : _idByPath.TryGetValue(a.ParentPath, out var pid) ? pid : null;
                    var created = await _svc.AddFolderAsync(a.Name, parentId, a.Index, a.IsQuery, a.IsSyndication, ct).ConfigureAwait(false);
                    _idByPath[a.Path] = created;
                    break;
                }
                case WorkAreaOp.Rename:
                    await _svc.RenameFolderAsync(a.LiveId, a.Name, ct).ConfigureAwait(false);
                    break;
                case WorkAreaOp.SetIndex:
                    await _svc.SetIndexAsync(a.LiveId, a.Index, ct).ConfigureAwait(false);
                    break;
                case WorkAreaOp.SetSyndication:
                    await _svc.SetSyndicationAsync(a.LiveId, a.IsSyndication, ct).ConfigureAwait(false);
                    break;
                case WorkAreaOp.SetQuery when a.Query is not null:
                {
                    // Create resolves the freshly-minted id from the map; Update uses the live id.
                    var targetId = a.Kind == WorkAreaActionKind.Create ? _idByPath[a.Path] : a.LiveId;
                    await _svc.SetQueryAsync(targetId, a.Query, ct).ConfigureAwait(false);
                    break;
                }
                case WorkAreaOp.Delete:
                    await _svc.DeleteFolderAsync(a.LiveId, ct).ConfigureAwait(false);
                    break;
            }
        }
    }
}

/// <summary>Outcome of an <see cref="WorkAreaService.ApplyAsync(IReadOnlyList{WorkAreaFolderDto}, bool, CancellationToken)"/> run.</summary>
public sealed class WorkAreaApplyResult
{
    public int Created { get; set; }
    public int Updated { get; set; }
    public int Deleted { get; set; }
    public int Failed { get; set; }
    public int Total => Created + Updated + Deleted;
}
