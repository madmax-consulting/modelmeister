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
        return ReconcileAsync(desired, allowDeletes, ct);
    }

    /// <summary>Apply folders described by DTOs (e.g. an Excel import). Query comes from <see cref="WorkAreaFolderDto.QueryJson"/>.</summary>
    public Task<WorkAreaApplyResult> ApplyAsync(IReadOnlyList<WorkAreaFolderDto> source, bool allowDeletes, CancellationToken ct = default)
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
        return ReconcileAsync(desired, allowDeletes, ct);
    }

    private sealed record DesiredFolder(
        string Path, string? ParentPath, string Name, int Index, bool IsQuery, bool IsSyndication, ComplexQuery? Query);

    private async Task<WorkAreaApplyResult> ReconcileAsync(List<DesiredFolder> desired, bool allowDeletes, CancellationToken ct)
    {
        var result = new WorkAreaApplyResult();

        // Resolve the target's current folders, keyed by path.
        var targetById = GetRawFolders().ToDictionary(f => f.Id);
        var idByPath = targetById.Values.ToDictionary(f => PathOf(f, targetById), f => f.Id, StringComparer.OrdinalIgnoreCase);

        // Parents before children so a child's ParentId resolves to an already-created folder.
        foreach (var d in desired.OrderBy(d => d.Path.Count(c => c == '/')).ThenBy(d => d.Index))
        {
            ct.ThrowIfCancellationRequested();
            try
            {
                Guid? parentId = d.ParentPath is null ? null
                    : idByPath.TryGetValue(d.ParentPath, out var pid) ? pid : null;

                if (idByPath.TryGetValue(d.Path, out var existingId))
                {
                    if (!string.Equals(targetById.GetValueOrDefault(existingId)?.Name, d.Name, StringComparison.Ordinal))
                        await RenameFolderAsync(existingId, d.Name, ct).ConfigureAwait(false);
                    if (d.IsQuery && d.Query is not null)
                        await SetQueryAsync(existingId, d.Query, ct).ConfigureAwait(false);
                    result.Updated++;
                }
                else
                {
                    var dto = new IriverWorkAreaFolder
                    {
                        Name = d.Name, ParentId = parentId, Index = d.Index, IsQuery = d.IsQuery, IsSyndication = d.IsSyndication,
                    };
                    var created = await _client.WriteAsync(m => m.UtilityService.AddSharedWorkAreaFolder(dto), ct).ConfigureAwait(false);
                    idByPath[d.Path] = created.Id;
                    if (d.IsQuery && d.Query is not null)
                        await SetQueryAsync(created.Id, d.Query, ct).ConfigureAwait(false);
                    result.Created++;
                }
            }
            catch (Exception ex)
            {
                _log.LogWarning(ex, "WorkArea apply failed for path {Path}", d.Path);
                result.Failed++;
            }
        }

        if (allowDeletes)
        {
            var desiredPaths = desired.Select(d => d.Path).ToHashSet(StringComparer.OrdinalIgnoreCase);
            // Deepest paths first so children are removed before parents.
            var toDelete = idByPath
                .Where(kv => !desiredPaths.Contains(kv.Key))
                .OrderByDescending(kv => kv.Key.Count(c => c == '/'));
            foreach (var (path, id) in toDelete)
            {
                ct.ThrowIfCancellationRequested();
                try { await DeleteFolderAsync(id, ct).ConfigureAwait(false); result.Deleted++; }
                catch (Exception ex) { _log.LogWarning(ex, "WorkArea delete failed for path {Path}", path); result.Failed++; }
            }
        }

        return result;
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
