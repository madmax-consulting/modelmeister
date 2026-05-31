using Spectre.Console;
using ModelMeister.Excel;
using ModelMeister.Inriver;
using ModelMeister.Inriver.WorkAreas;

namespace ModelMeister.Cli.Commands;

/// <summary>
/// CLI surface for shared work-area folders + saved queries: list, export/import an Excel workbook, and
/// promote folders (with their queries) from one environment to another. Queries are carried verbatim on
/// promote and as an opaque JSON blob in the workbook.
/// </summary>
public static class WorkAreasCommand
{
    /// <summary>Bind the service to the shared surface, or to <paramref name="personalUser"/>'s personal
    /// folders when a username is supplied (the CLI <c>--user</c> option).</summary>
    private static WorkAreaService Svc(InriverClient client, string? personalUser) =>
        string.IsNullOrWhiteSpace(personalUser) ? WorkAreaService.ForShared(client) : WorkAreaService.ForPersonal(client, personalUser);

    private static string ScopeLabel(string? personalUser) =>
        string.IsNullOrWhiteSpace(personalUser) ? "shared" : $"personal:{personalUser}";

    public static async Task<int> ListAsync(string url, InriverAuth auth, string? personalUser, CancellationToken ct)
    {
        using var client = new InriverClient(url);
        var rc = await auth.ConnectAsync(client).ConfigureAwait(false);
        if (rc != ExitCodes.Success) return rc;
        try
        {
            var folders = Svc(client, personalUser).List();
            var table = new Table().AddColumns("Path", "Query", "Syndication");
            foreach (var f in folders)
                table.AddRow(
                    f.Path.EscapeMarkup(),
                    f.IsQuery ? "[green]yes[/]" : "",
                    f.IsSyndication ? "[blue]yes[/]" : "");
            AnsiConsole.Write(table);
            AnsiConsole.MarkupLine($"[grey]{folders.Count} folder(s) · {ScopeLabel(personalUser)}.[/]");
            return ExitCodes.Success;
        }
        catch (Exception ex)
        {
            AnsiConsole.MarkupLine($"[red]List failed:[/] {ex.Message.EscapeMarkup()}");
            return ExitCodes.OperationFailed;
        }
    }

    public static async Task<int> ExportAsync(string url, InriverAuth auth, string outPath, string? personalUser, CancellationToken ct)
    {
        using var client = new InriverClient(url);
        var rc = await auth.ConnectAsync(client).ConfigureAwait(false);
        if (rc != ExitCodes.Success) return rc;
        try
        {
            var folders = Svc(client, personalUser).List();
            WorkAreaWorkbook.Save(folders, outPath);
            AnsiConsole.MarkupLine($"[green]Wrote {folders.Count} folder(s) to[/] {outPath.EscapeMarkup()}");
            return ExitCodes.Success;
        }
        catch (Exception ex)
        {
            AnsiConsole.MarkupLine($"[red]Export failed:[/] {ex.Message.EscapeMarkup()}");
            return ExitCodes.OperationFailed;
        }
    }

    public static async Task<int> ImportAsync(string url, InriverAuth auth, string excelPath, bool allowDeletes, bool dryRun, string? personalUser, CancellationToken ct)
    {
        var desired = WorkAreaWorkbook.Load(excelPath);
        if (dryRun)
        {
            AnsiConsole.MarkupLine($"[yellow]DRY-RUN[/] — {desired.Count} folder(s) in workbook:");
            foreach (var f in desired) AnsiConsole.MarkupLine($"  {f.Path.EscapeMarkup()}{(f.IsQuery ? " [grey](query)[/]" : "")}");
            return ExitCodes.Success;
        }

        using var client = new InriverClient(url);
        var rc = await auth.ConnectAsync(client).ConfigureAwait(false);
        if (rc != ExitCodes.Success) return rc;
        try
        {
            var result = await Svc(client, personalUser).ApplyAsync(desired, allowDeletes, ct).ConfigureAwait(false);
            return Report(result);
        }
        catch (Exception ex)
        {
            AnsiConsole.MarkupLine($"[red]Import failed:[/] {ex.Message.EscapeMarkup()}");
            return ExitCodes.OperationFailed;
        }
    }

    public static async Task<int> PromoteAsync(
        string fromUrl, InriverAuth fromAuth, string toUrl, InriverAuth toAuth, bool allowDeletes, string? personalUser, CancellationToken ct)
    {
        // Read the source env fully into memory (keeps the live query objects), then connect the target.
        IReadOnlyList<inRiver.Remoting.Objects.WorkAreaFolder> source;
        using (var src = new InriverClient(fromUrl))
        {
            var rc = await fromAuth.ConnectAsync(src).ConfigureAwait(false);
            if (rc != ExitCodes.Success) return rc;
            try { source = Svc(src, personalUser).GetRawFolders(); }
            catch (Exception ex) { AnsiConsole.MarkupLine($"[red]Read source failed:[/] {ex.Message.EscapeMarkup()}"); return ExitCodes.OperationFailed; }
        }

        using var tgt = new InriverClient(toUrl);
        var trc = await toAuth.ConnectAsync(tgt).ConfigureAwait(false);
        if (trc != ExitCodes.Success) return trc;
        try
        {
            var result = await Svc(tgt, personalUser).ApplyAsync(source, allowDeletes, ct).ConfigureAwait(false);
            return Report(result);
        }
        catch (Exception ex)
        {
            AnsiConsole.MarkupLine($"[red]Promote failed:[/] {ex.Message.EscapeMarkup()}");
            return ExitCodes.OperationFailed;
        }
    }

    /// <summary>Duplicate a folder in place (under its own parent). Deep when <paramref name="deep"/>.
    /// Resolves the source by path within the chosen scope (shared, or --user).</summary>
    public static async Task<int> DuplicateAsync(
        string url, InriverAuth auth, string path, bool deep, string? personalUser, CancellationToken ct)
    {
        using var client = new InriverClient(url);
        var rc = await auth.ConnectAsync(client).ConfigureAwait(false);
        if (rc != ExitCodes.Success) return rc;
        try
        {
            var svc = Svc(client, personalUser);
            var folders = svc.List();
            if (!TryResolve(folders, path, out var src))
            {
                AnsiConsole.MarkupLine($"[red]No work-area folder at path[/] {path.EscapeMarkup()} [grey]({ScopeLabel(personalUser)}).[/]");
                return ExitCodes.UsageError;
            }

            var newIndex = EndIndexUnder(folders, src.ParentId);
            var newId = deep
                ? await svc.CopySubtreeAsync(src.Id, src.ParentId, newIndex, null, ct).ConfigureAwait(false)
                : await svc.CopyFolderAsync(src.Id, src.ParentId, newIndex, null, ct).ConfigureAwait(false);

            AnsiConsole.MarkupLine(
                $"[green]Duplicated[/] {path.EscapeMarkup()} {(deep ? "[grey](deep)[/] " : "")}[grey]→[/] new folder [blue]{newId}[/] [grey]({ScopeLabel(personalUser)}).[/]");
            return ExitCodes.Success;
        }
        catch (Exception ex)
        {
            AnsiConsole.MarkupLine($"[red]Duplicate failed:[/] {ex.Message.EscapeMarkup()}");
            return ExitCodes.OperationFailed;
        }
    }

    /// <summary>Copy a folder (subtree when <paramref name="deep"/>) under a target parent. Same-scope copy
    /// unless a destination scope (<paramref name="toShared"/> / <paramref name="toUser"/>) differs from the
    /// source scope (<paramref name="personalUser"/>), in which case it's a cross-scope copy on the same env.
    /// <paramref name="dryRun"/> prints the plan without writing.</summary>
    public static async Task<int> CopyAsync(
        string url, InriverAuth auth, string sourcePath, string targetParentPath, bool deep,
        bool toShared, string? toUser, bool dryRun, string? personalUser, CancellationToken ct)
    {
        if (toShared && !string.IsNullOrWhiteSpace(toUser))
        {
            AnsiConsole.MarkupLine("[red]Specify only one of[/] --to-shared [red]or[/] --to-user.");
            return ExitCodes.UsageError;
        }

        // Destination scope: explicit --to-shared / --to-user override the source scope (--user); otherwise
        // the destination is the same scope as the source.
        var destUser = toShared ? null : (string.IsNullOrWhiteSpace(toUser) ? personalUser : toUser);
        var crossScope = !string.Equals(destUser, personalUser, StringComparison.OrdinalIgnoreCase);

        using var client = new InriverClient(url);
        var rc = await auth.ConnectAsync(client).ConfigureAwait(false);
        if (rc != ExitCodes.Success) return rc;
        try
        {
            var srcSvc = Svc(client, personalUser);
            var srcFolders = srcSvc.List();
            if (!TryResolve(srcFolders, sourcePath, out var src))
            {
                AnsiConsole.MarkupLine($"[red]No source folder at path[/] {sourcePath.EscapeMarkup()} [grey]({ScopeLabel(personalUser)}).[/]");
                return ExitCodes.UsageError;
            }

            var destSvc = crossScope ? Svc(client, destUser) : srcSvc;
            var destFolders = crossScope ? destSvc.List() : srcFolders;

            // Resolve the destination parent (empty/'/' ⇒ root).
            Guid? destParentId = null;
            if (!IsRootPath(targetParentPath))
            {
                if (!TryResolve(destFolders, targetParentPath, out var destParent))
                {
                    AnsiConsole.MarkupLine($"[red]No target parent folder at path[/] {targetParentPath.EscapeMarkup()} [grey]({ScopeLabel(destUser)}).[/]");
                    return ExitCodes.UsageError;
                }
                destParentId = destParent.Id;
            }

            var destIndex = EndIndexUnder(destFolders, destParentId);

            if (dryRun)
            {
                var destWhere = IsRootPath(targetParentPath) ? "(root)" : targetParentPath;
                AnsiConsole.MarkupLine(
                    $"[yellow]DRY-RUN[/] copy {(deep ? "[grey](deep)[/] " : "[grey](shallow)[/] ")}{sourcePath.EscapeMarkup()} " +
                    $"[grey]({ScopeLabel(personalUser)})[/] [grey]→[/] {destWhere.EscapeMarkup()} [grey]({ScopeLabel(destUser)}) at index {destIndex}.[/]");
                if (deep)
                    foreach (var d in srcFolders.Where(f => f.Path == sourcePath || f.Path.StartsWith(sourcePath + "/", StringComparison.OrdinalIgnoreCase)))
                        AnsiConsole.MarkupLine($"  {d.Path.EscapeMarkup()}{(d.IsQuery ? " [grey](query)[/]" : "")}");
                else
                    AnsiConsole.MarkupLine($"  {src.Path.EscapeMarkup()}{(src.IsQuery ? " [grey](query)[/]" : "")}");
                return ExitCodes.Success;
            }

            Guid newId = crossScope
                ? await srcSvc.CopyToServiceAsync(src.Id, destSvc, destParentId, destIndex, null, deep, ct).ConfigureAwait(false)
                : deep
                    ? await srcSvc.CopySubtreeAsync(src.Id, destParentId, destIndex, null, ct).ConfigureAwait(false)
                    : await srcSvc.CopyFolderAsync(src.Id, destParentId, destIndex, null, ct).ConfigureAwait(false);

            AnsiConsole.MarkupLine(
                $"[green]Copied[/] {sourcePath.EscapeMarkup()} [grey]({ScopeLabel(personalUser)})[/] [grey]→[/] new folder [blue]{newId}[/] [grey]({ScopeLabel(destUser)}).[/]");
            return ExitCodes.Success;
        }
        catch (Exception ex)
        {
            AnsiConsole.MarkupLine($"[red]Copy failed:[/] {ex.Message.EscapeMarkup()}");
            return ExitCodes.OperationFailed;
        }
    }

    /// <summary>Re-parent a folder under a target parent (same scope). Moving to root is not supported
    /// (inriver move requires a non-null parent) and returns a usage error.</summary>
    public static async Task<int> MoveAsync(
        string url, InriverAuth auth, string sourcePath, string targetParentPath, string? personalUser, CancellationToken ct)
    {
        if (IsRootPath(targetParentPath))
        {
            AnsiConsole.MarkupLine("[red]Cannot move a folder to the root[/] — inriver move requires a target parent folder.");
            return ExitCodes.UsageError;
        }

        using var client = new InriverClient(url);
        var rc = await auth.ConnectAsync(client).ConfigureAwait(false);
        if (rc != ExitCodes.Success) return rc;
        try
        {
            var svc = Svc(client, personalUser);
            var folders = svc.List();
            if (!TryResolve(folders, sourcePath, out var src))
            {
                AnsiConsole.MarkupLine($"[red]No source folder at path[/] {sourcePath.EscapeMarkup()} [grey]({ScopeLabel(personalUser)}).[/]");
                return ExitCodes.UsageError;
            }
            if (!TryResolve(folders, targetParentPath, out var target))
            {
                AnsiConsole.MarkupLine($"[red]No target parent folder at path[/] {targetParentPath.EscapeMarkup()} [grey]({ScopeLabel(personalUser)}).[/]");
                return ExitCodes.UsageError;
            }

            var newIndex = EndIndexUnder(folders, target.Id);
            try
            {
                await svc.MoveFolderAsync(src.Id, target.Id, newIndex, ct).ConfigureAwait(false);
            }
            catch (NotSupportedException nse)
            {
                AnsiConsole.MarkupLine($"[red]Move not supported:[/] {nse.Message.EscapeMarkup()}");
                return ExitCodes.UsageError;
            }

            AnsiConsole.MarkupLine(
                $"[green]Moved[/] {sourcePath.EscapeMarkup()} [grey]→[/] under {targetParentPath.EscapeMarkup()} [grey]({ScopeLabel(personalUser)}).[/]");
            return ExitCodes.Success;
        }
        catch (Exception ex)
        {
            AnsiConsole.MarkupLine($"[red]Move failed:[/] {ex.Message.EscapeMarkup()}");
            return ExitCodes.OperationFailed;
        }
    }

    /// <summary>Empty or "/" denotes the tree root.</summary>
    private static bool IsRootPath(string? path) =>
        string.IsNullOrWhiteSpace(path) || path.Trim() == "/";

    /// <summary>Resolve a folder by its tree path (case-insensitive, leading/trailing slashes trimmed).</summary>
    private static bool TryResolve(IReadOnlyList<WorkAreaFolderDto> folders, string path, out WorkAreaFolderDto folder)
    {
        var norm = (path ?? "").Trim().Trim('/');
        folder = folders.FirstOrDefault(f => string.Equals(f.Path, norm, StringComparison.OrdinalIgnoreCase))!;
        return folder is not null;
    }

    /// <summary>The index to place a new sibling at the end under <paramref name="parentId"/> (null ⇒ root).</summary>
    private static int EndIndexUnder(IReadOnlyList<WorkAreaFolderDto> folders, Guid? parentId)
    {
        var siblings = folders.Where(f => f.ParentId == parentId).ToList();
        return siblings.Count == 0 ? 0 : siblings.Max(f => f.Index) + 1;
    }

    private static int Report(WorkAreaApplyResult r)
    {
        AnsiConsole.MarkupLine(
            $"[green]Created {r.Created}[/], [blue]updated {r.Updated}[/], [yellow]deleted {r.Deleted}[/]" +
            (r.Failed > 0 ? $", [red]{r.Failed} failed[/]" : ""));
        return r.Failed > 0 ? ExitCodes.OperationFailed : ExitCodes.Success;
    }
}
