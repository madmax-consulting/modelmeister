using System.Text.Json;
using ClosedXML.Excel;
using ModelMeister.Inriver.HtmlTemplates;

namespace ModelMeister.Excel;

/// <summary>
/// Round-trip Excel workbook for HTML templates. One sheet, one row per template; columns
/// <c>Name, TemplateType, LocalizedName, Properties, Content</c>. Templates are matched across
/// environments by name + type (inriver ids differ per env), so the id is intentionally not exported.
/// </summary>
/// <remarks>
/// <para><b>Cell-size limit.</b> Excel caps a single cell at 32,767 characters and HTML template bodies
/// routinely exceed that. Oversize <c>Content</c> (or <c>Properties</c>) is therefore spilled to a
/// sidecar file in a <c>&lt;workbook&gt;_files</c> folder next to the xlsx, and the cell holds a
/// <c>@file:&lt;name&gt;</c> reference. <see cref="Load"/> resolves those references transparently, so the
/// round-trip is faithful for templates of any size as long as the sidecar folder travels with the
/// workbook.</para>
/// </remarks>
public static class HtmlTemplateWorkbook
{
    public const string SheetTemplates = "HtmlTemplates";

    /// <summary>Below this length a value stays in-cell; at or above it spills to a sidecar file.</summary>
    private const int CellLimit = 32_000;
    private const string FileRefPrefix = "@file:";

    private static readonly JsonSerializerOptions LocalizedJsonOptions = new() { WriteIndented = false };

    public static void Save(IReadOnlyList<HtmlTemplateDto> templates, string path)
    {
        var full = Path.GetFullPath(path);
        Directory.CreateDirectory(Path.GetDirectoryName(full)!);
        var sidecarDir = SidecarDir(full);

        using var wb = new XLWorkbook();
        var ws = wb.AddWorksheet(SheetTemplates);
        var cols = new[] { "Name", "TemplateType", "LocalizedName", "Properties", "Content" };
        for (var i = 0; i < cols.Length; i++) ws.Cell(1, i + 1).Value = cols[i];
        XlIo.StyleHeader(ws.Row(1));

        var r = 2;
        var index = 0;
        foreach (var t in templates.OrderBy(x => x.Name, StringComparer.OrdinalIgnoreCase))
        {
            ws.Cell(r, 1).Value = t.Name;
            ws.Cell(r, 2).Value = t.TemplateType;
            ws.Cell(r, 3).Value = t.LocalizedName.Count == 0
                ? string.Empty
                : JsonSerializer.Serialize(t.LocalizedName, LocalizedJsonOptions);
            ws.Cell(r, 4).Value = StoreOrSpill(t.Properties, sidecarDir, $"{index}_{Safe(t.Name)}.properties.txt");
            ws.Cell(r, 5).Value = StoreOrSpill(t.Content, sidecarDir, $"{index}_{Safe(t.Name)}.html");
            r++;
            index++;
        }

        ws.SheetView.FreezeRows(1);
        ws.Columns().AdjustToContents(1, 50, 8, 80);
        wb.SaveAs(full);
    }

    public static IReadOnlyList<HtmlTemplateDto> Load(string path)
    {
        var full = Path.GetFullPath(path);
        var sidecarDir = SidecarDir(full);
        using var wb = new XLWorkbook(full);
        if (!wb.TryGetWorksheet(SheetTemplates, out var ws)) return [];
        var hdr = XlIo.HeaderMap(ws);
        var list = new List<HtmlTemplateDto>();
        var last = ws.LastRowUsed()?.RowNumber() ?? 1;
        for (var r = 2; r <= last; r++)
        {
            var row = ws.Row(r);
            var name = XlIo.ReadString(row, hdr, "Name");
            if (string.IsNullOrWhiteSpace(name)) continue;
            var localized = XlIo.ReadString(row, hdr, "LocalizedName");
            list.Add(new HtmlTemplateDto
            {
                Name = name,
                TemplateType = XlIo.ReadString(row, hdr, "TemplateType"),
                Properties = Resolve(ReadRaw(row, hdr, "Properties"), sidecarDir),
                Content = Resolve(ReadRaw(row, hdr, "Content"), sidecarDir),
                LocalizedName = ParseLocalized(localized),
            });
        }
        return list;
    }

    // ---- helpers ----

    /// <summary>Reads a cell without the trimming <see cref="XlIo.ReadString"/> applies — template bodies
    /// must round-trip byte-for-byte (leading/trailing whitespace preserved).</summary>
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

    private static Dictionary<string, string> ParseLocalized(string json)
    {
        if (string.IsNullOrWhiteSpace(json)) return new(StringComparer.OrdinalIgnoreCase);
        try
        {
            var map = JsonSerializer.Deserialize<Dictionary<string, string>>(json);
            return map is null ? new(StringComparer.OrdinalIgnoreCase) : new(map, StringComparer.OrdinalIgnoreCase);
        }
        catch { return new(StringComparer.OrdinalIgnoreCase); }
    }

    private static string SidecarDir(string xlsxFullPath) =>
        Path.Combine(Path.GetDirectoryName(xlsxFullPath)!, Path.GetFileNameWithoutExtension(xlsxFullPath) + "_files");

    private static string Safe(string name)
    {
        var chars = name.Select(c => char.IsLetterOrDigit(c) || c is '-' or '_' ? c : '_').ToArray();
        var s = new string(chars);
        return s.Length > 40 ? s[..40] : s;
    }
}
