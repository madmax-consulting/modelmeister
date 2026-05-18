using ClosedXML.Excel;

namespace ModelMeister.Excel;

/// <summary>
/// Excel workbook for the flat <c>string → string</c> server-settings dictionary. Single sheet
/// (<c>Settings</c>) with Key and Value columns. Round-trippable: Save / Load preserves keys and
/// values exactly.
/// </summary>
public static class ServerSettingsWorkbook
{
    public const string SheetSettings = "Settings";
    public const string SheetReadme = "_Readme";

    /// <summary>Write the dictionary to <paramref name="path"/>.</summary>
    public static void Save(IReadOnlyDictionary<string, string> settings, string path)
    {
        using var wb = Build(settings);
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);
        wb.SaveAs(path);
    }

    /// <summary>Read the workbook back into a dictionary. Keys with empty cells become empty strings.</summary>
    public static IReadOnlyDictionary<string, string> Load(string path)
    {
        using var wb = new XLWorkbook(path);
        return Parse(wb);
    }

    /// <summary>Compose the workbook in memory. Public for tests.</summary>
    public static XLWorkbook Build(IReadOnlyDictionary<string, string> settings)
    {
        var wb = new XLWorkbook();
        var readme = wb.AddWorksheet(SheetReadme);
        readme.Cell(1, 1).Value = "Server settings workbook";
        readme.Cell(1, 1).Style.Font.Bold = true;
        readme.Cell(1, 1).Style.Font.FontSize = 14;
        readme.Cell(3, 1).Value = "Each row is one server setting. Edit values, save, then re-import to push back.";
        readme.Cell(4, 1).Value = "Keys not present in the workbook are left unchanged on import.";

        var ws = wb.AddWorksheet(SheetSettings);
        ws.Cell(1, 1).Value = "Key";
        ws.Cell(1, 2).Value = "Value";
        XlIo.StyleHeader(ws.Row(1));
        ws.SheetView.FreezeRows(1);

        var r = 2;
        foreach (var kvp in settings.OrderBy(k => k.Key, StringComparer.Ordinal))
        {
            ws.Cell(r, 1).Value = kvp.Key;
            ws.Cell(r, 2).Value = kvp.Value ?? "";
            r++;
        }
        ws.Columns().AdjustToContents();
        return wb;
    }

    /// <summary>Read the <see cref="SheetSettings"/> sheet into a dictionary.</summary>
    public static IReadOnlyDictionary<string, string> Parse(IXLWorkbook wb)
    {
        var result = new Dictionary<string, string>(StringComparer.Ordinal);
        if (!wb.TryGetWorksheet(SheetSettings, out var ws)) return result;
        var last = ws.LastRowUsed()?.RowNumber() ?? 1;
        for (var r = 2; r <= last; r++)
        {
            var key = ws.Cell(r, 1).GetString();
            if (string.IsNullOrEmpty(key)) continue;
            result[key] = ws.Cell(r, 2).GetString() ?? "";
        }
        return result;
    }
}
