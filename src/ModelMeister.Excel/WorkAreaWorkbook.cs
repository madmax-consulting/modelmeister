using ClosedXML.Excel;
using ModelMeister.Inriver.WorkAreas;

namespace ModelMeister.Excel;

/// <summary>
/// Round-trip Excel workbook for shared work-area folders (a tree of folders, some carrying a saved
/// search). One sheet, one row per folder; <c>Path</c> (parent-chain of names) carries the hierarchy so
/// the import side can rebuild it without inriver's per-env folder GUIDs. The saved query rides as an
/// opaque JSON blob in the <c>Query</c> column.
/// </summary>
public static class WorkAreaWorkbook
{
    public const string SheetFolders = "WorkAreaFolders";

    public static void Save(IReadOnlyList<WorkAreaFolderDto> folders, string path)
    {
        using var wb = Build(folders);
        Directory.CreateDirectory(Path.GetDirectoryName(System.IO.Path.GetFullPath(path))!);
        wb.SaveAs(path);
    }

    public static IReadOnlyList<WorkAreaFolderDto> Load(string path)
    {
        using var wb = new XLWorkbook(path);
        return Parse(wb);
    }

    public static XLWorkbook Build(IReadOnlyList<WorkAreaFolderDto> folders)
    {
        var wb = new XLWorkbook();
        var ws = wb.AddWorksheet(SheetFolders);
        var cols = new[] { "Path", "Name", "Index", "IsQuery", "IsSyndication", "Query" };
        for (var i = 0; i < cols.Length; i++) ws.Cell(1, i + 1).Value = cols[i];
        XlIo.StyleHeader(ws.Row(1));

        var r = 2;
        foreach (var f in folders.OrderBy(x => x.Path, StringComparer.OrdinalIgnoreCase))
        {
            ws.Cell(r, 1).Value = f.Path;
            ws.Cell(r, 2).Value = f.Name;
            ws.Cell(r, 3).Value = f.Index;
            ws.Cell(r, 4).Value = f.IsQuery;
            ws.Cell(r, 5).Value = f.IsSyndication;
            ws.Cell(r, 6).Value = f.QueryJson ?? string.Empty;
            r++;
        }

        ws.SheetView.FreezeRows(1);
        ws.Columns().AdjustToContents(1, 50, 8, 80);
        return wb;
    }

    public static IReadOnlyList<WorkAreaFolderDto> Parse(IXLWorkbook wb)
    {
        if (!wb.TryGetWorksheet(SheetFolders, out var ws)) return [];
        var hdr = XlIo.HeaderMap(ws);
        var list = new List<WorkAreaFolderDto>();
        var last = ws.LastRowUsed()?.RowNumber() ?? 1;
        for (var r = 2; r <= last; r++)
        {
            var row = ws.Row(r);
            var path = XlIo.ReadString(row, hdr, "Path");
            var name = XlIo.ReadString(row, hdr, "Name");
            if (string.IsNullOrWhiteSpace(path) && string.IsNullOrWhiteSpace(name)) continue;
            var query = XlIo.ReadString(row, hdr, "Query");
            list.Add(new WorkAreaFolderDto
            {
                Path = string.IsNullOrWhiteSpace(path) ? name : path,
                Name = string.IsNullOrWhiteSpace(name) ? LastSegment(path) : name,
                Index = XlIo.ReadInt(row, hdr, "Index"),
                IsQuery = XlIo.ReadBool(row, hdr, "IsQuery"),
                IsSyndication = XlIo.ReadBool(row, hdr, "IsSyndication"),
                QueryJson = string.IsNullOrWhiteSpace(query) ? null : query,
            });
        }
        return list;
    }

    private static string LastSegment(string path)
    {
        var slash = path.LastIndexOf('/');
        return slash < 0 ? path : path[(slash + 1)..];
    }
}
