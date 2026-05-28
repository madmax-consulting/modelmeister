using ClosedXML.Excel;
using ModelMeister.Inriver.WorkAreas;

namespace ModelMeister.Excel;

/// <summary>
/// Round-trip Excel workbook for work-area folders (a tree of folders, some carrying a saved search). One
/// sheet, one row per folder; <c>Path</c> (parent-chain of names) carries the hierarchy so the import side
/// can rebuild it without inriver's per-env folder GUIDs. The saved query rides as an opaque JSON blob in the
/// <c>Query</c> column, and <c>Username</c> records the owner (blank for shared folders).
/// </summary>
/// <remarks>
/// <para><b>Cell-size limit.</b> Excel caps a single cell at 32,767 characters; a GUI-built saved search can
/// exceed that. Oversize <c>Query</c> JSON is therefore spilled to a sidecar file in a
/// <c>&lt;workbook&gt;_files</c> folder next to the xlsx, with the cell holding a <c>@file:&lt;name&gt;</c>
/// reference that <see cref="Load"/> resolves transparently — so the round-trip is faithful for queries of any
/// size as long as the sidecar folder travels with the workbook.</para>
/// </remarks>
public static class WorkAreaWorkbook
{
    public const string SheetFolders = "WorkAreaFolders";

    private const int CellLimit = 32_000;
    private const string FileRefPrefix = "@file:";

    public static void Save(IReadOnlyList<WorkAreaFolderDto> folders, string path)
    {
        var full = Path.GetFullPath(path);
        Directory.CreateDirectory(Path.GetDirectoryName(full)!);
        var sidecarDir = SidecarDir(full);

        using var wb = new XLWorkbook();
        var ws = wb.AddWorksheet(SheetFolders);
        var cols = new[] { "Path", "Name", "Index", "IsQuery", "IsSyndication", "Username", "Query" };
        for (var i = 0; i < cols.Length; i++) ws.Cell(1, i + 1).Value = cols[i];
        XlIo.StyleHeader(ws.Row(1));

        var r = 2;
        var index = 0;
        foreach (var f in folders.OrderBy(x => x.Path, StringComparer.OrdinalIgnoreCase))
        {
            ws.Cell(r, 1).Value = f.Path;
            ws.Cell(r, 2).Value = f.Name;
            ws.Cell(r, 3).Value = f.Index;
            ws.Cell(r, 4).Value = f.IsQuery;
            ws.Cell(r, 5).Value = f.IsSyndication;
            ws.Cell(r, 6).Value = f.Username ?? string.Empty;
            ws.Cell(r, 7).Value = StoreOrSpill(f.QueryJson ?? string.Empty, sidecarDir, $"{index}_{Safe(f.Path)}.query.json");
            r++;
            index++;
        }

        ws.SheetView.FreezeRows(1);
        ws.Columns().AdjustToContents(1, 50, 8, 80);
        wb.SaveAs(full);
    }

    public static IReadOnlyList<WorkAreaFolderDto> Load(string path)
    {
        var full = Path.GetFullPath(path);
        var sidecarDir = SidecarDir(full);
        using var wb = new XLWorkbook(full);
        if (!wb.TryGetWorksheet(SheetFolders, out var ws)) return [];
        var hdr = XlIo.HeaderMap(ws);
        var list = new List<WorkAreaFolderDto>();
        var last = ws.LastRowUsed()?.RowNumber() ?? 1;
        for (var r = 2; r <= last; r++)
        {
            var row = ws.Row(r);
            var path0 = XlIo.ReadString(row, hdr, "Path");
            var name = XlIo.ReadString(row, hdr, "Name");
            if (string.IsNullOrWhiteSpace(path0) && string.IsNullOrWhiteSpace(name)) continue;
            var query = Resolve(ReadRaw(row, hdr, "Query"), sidecarDir);
            var username = XlIo.ReadString(row, hdr, "Username");
            list.Add(new WorkAreaFolderDto
            {
                Path = string.IsNullOrWhiteSpace(path0) ? name : path0,
                Name = string.IsNullOrWhiteSpace(name) ? LastSegment(path0) : name,
                Index = XlIo.ReadInt(row, hdr, "Index"),
                IsQuery = XlIo.ReadBool(row, hdr, "IsQuery"),
                IsSyndication = XlIo.ReadBool(row, hdr, "IsSyndication"),
                Username = string.IsNullOrWhiteSpace(username) ? null : username,
                QueryJson = string.IsNullOrWhiteSpace(query) ? null : query,
            });
        }
        return list;
    }

    // ---- helpers ----

    private static string ReadRaw(IXLRow row, IReadOnlyDictionary<string, int> hdr, string key)
        => hdr.TryGetValue(key, out var c) ? row.Cell(c).GetString() : string.Empty;

    private static string StoreOrSpill(string value, string sidecarDir, string fileName)
    {
        if (value.Length < CellLimit) return value;
        Directory.CreateDirectory(sidecarDir);
        File.WriteAllText(Path.Combine(sidecarDir, fileName), value);
        return FileRefPrefix + fileName;
    }

    private static string Resolve(string cell, string sidecarDir)
    {
        if (!cell.StartsWith(FileRefPrefix, StringComparison.Ordinal)) return cell;
        var fileName = cell[FileRefPrefix.Length..].Trim();
        var p = Path.Combine(sidecarDir, fileName);
        return File.Exists(p) ? File.ReadAllText(p) : cell;
    }

    private static string SidecarDir(string xlsxFullPath) =>
        Path.Combine(Path.GetDirectoryName(xlsxFullPath)!, Path.GetFileNameWithoutExtension(xlsxFullPath) + "_files");

    private static string LastSegment(string path)
    {
        var slash = path.LastIndexOf('/');
        return slash < 0 ? path : path[(slash + 1)..];
    }

    private static string Safe(string name)
    {
        var chars = name.Select(c => char.IsLetterOrDigit(c) || c is '-' or '_' ? c : '_').ToArray();
        var s = new string(chars);
        return s.Length > 40 ? s[..40] : s;
    }
}
