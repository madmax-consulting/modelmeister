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
    /// <summary>Opaque-but-readable serialization of the saved query. Enums render as names.</summary>
    public static readonly JsonSerializerOptions QueryJsonOptions = new()
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        Converters = { new JsonStringEnumConverter() },
        WriteIndented = false,
    };

    private readonly InriverClient _client;
    private readonly ILogger _log;

    public WorkAreaService(InriverClient client, ILogger<WorkAreaService>? log = null)
    {
        _client = client;
        _log = (ILogger?)log ?? NullLogger.Instance;
    }

    // ---------------- Reads ----------------

    /// <summary>List all shared work-area folders as flat DTOs (each carries its computed tree path).</summary>
    public IReadOnlyList<WorkAreaFolderDto> List() => ToDtos(GetRawFolders());

    /// <summary>Raw shared folders straight from inriver — keeps the live <c>ComplexQuery</c> for faithful promote.</summary>
    public IReadOnlyList<IriverWorkAreaFolder> GetRawFolders() =>
        _client.Read(m => m.UtilityService.GetAllSharedWorkAreaFolders(includeEntities: false) ?? []);

    private static List<WorkAreaFolderDto> ToDtos(IReadOnlyList<IriverWorkAreaFolder> folders)
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

    /// <summary>Create a shared folder under an optional parent. Returns the created folder's id.</summary>
    public async Task<Guid> CreateFolderAsync(string name, Guid? parentId, int index, bool isQuery, CancellationToken ct = default)
    {
        var dto = new IriverWorkAreaFolder { Name = name, ParentId = parentId, Index = index, IsQuery = isQuery };
        var created = await _client.WriteAsync(m => m.UtilityService.AddSharedWorkAreaFolder(dto), ct).ConfigureAwait(false);
        return created.Id;
    }

    public Task RenameFolderAsync(Guid id, string name, CancellationToken ct = default) =>
        _client.WriteAsync(m => m.UtilityService.UpdateSharedWorkAreaFolderName(id, name), ct);

    public Task MoveFolderAsync(Guid id, Guid newParentId, int newIndex, CancellationToken ct = default) =>
        _client.WriteAsync(m => m.UtilityService.MoveSharedWorkAreaFolder(id, newParentId, newIndex), ct);

    public Task SetQueryAsync(Guid id, ComplexQuery query, CancellationToken ct = default) =>
        _client.WriteAsync(m => m.UtilityService.UpdateSharedWorkAreaQuery(id, query), ct);

    public Task DeleteFolderAsync(Guid id, CancellationToken ct = default) =>
        _client.WriteAsync(m => m.UtilityService.DeleteSharedWorkAreaFolder(id), ct);

    /// <summary>Create a folder with the full attribute set (incl. <c>IsSyndication</c>) used by reconcile.
    /// Returns the new folder's id.</summary>
    internal async Task<Guid> AddFolderAsync(string name, Guid? parentId, int index, bool isQuery, bool isSyndication, CancellationToken ct = default)
    {
        var dto = new IriverWorkAreaFolder { Name = name, ParentId = parentId, Index = index, IsQuery = isQuery, IsSyndication = isSyndication };
        var created = await _client.WriteAsync(m => m.UtilityService.AddSharedWorkAreaFolder(dto), ct).ConfigureAwait(false);
        return created.Id;
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
                actions.Add(new WorkAreaAction(
                    WorkAreaActionKind.Update, d.Path, d.ParentPath, d.Name, d.Index, d.IsQuery, d.IsSyndication, d.Query,
                    existingId, targetById.GetValueOrDefault(existingId)?.Name));
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
/// existing folder's id for update/delete (empty for create); <see cref="CurrentName"/> is the live name
/// for an update (drives the rename-only-if-changed check).</summary>
public sealed record WorkAreaAction(
    WorkAreaActionKind Kind, string Path, string? ParentPath, string Name,
    int Index, bool IsQuery, bool IsSyndication, ComplexQuery? Query, Guid LiveId, string? CurrentName);

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

    /// <summary>Execute one action, mutating the path→id map on create so later children resolve.</summary>
    public async Task ExecuteAsync(WorkAreaAction a, CancellationToken ct = default)
    {
        switch (a.Kind)
        {
            case WorkAreaActionKind.Create:
            {
                Guid? parentId = a.ParentPath is null ? null
                    : _idByPath.TryGetValue(a.ParentPath, out var pid) ? pid : null;
                var created = await _svc.AddFolderAsync(a.Name, parentId, a.Index, a.IsQuery, a.IsSyndication, ct).ConfigureAwait(false);
                _idByPath[a.Path] = created;
                if (a.IsQuery && a.Query is not null) await _svc.SetQueryAsync(created, a.Query, ct).ConfigureAwait(false);
                break;
            }
            case WorkAreaActionKind.Update:
                if (!string.Equals(a.CurrentName, a.Name, StringComparison.Ordinal))
                    await _svc.RenameFolderAsync(a.LiveId, a.Name, ct).ConfigureAwait(false);
                if (a.IsQuery && a.Query is not null) await _svc.SetQueryAsync(a.LiveId, a.Query, ct).ConfigureAwait(false);
                break;
            case WorkAreaActionKind.Delete:
                await _svc.DeleteFolderAsync(a.LiveId, ct).ConfigureAwait(false);
                break;
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
