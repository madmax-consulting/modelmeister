using System.Text.Json;
using ClosedXML.Excel;
using ModelMeister.Scaffolder;

namespace ModelMeister.Excel;

/// <summary>
/// One worksheet per CVL — optimised for editing CVL values by hand. The first sheet (<c>_Index</c>)
/// lists every CVL with metadata; one sheet per CVL holds its values.
/// </summary>
public static class CvlValuesWorkbook
{
    public const string SheetIndex = "_Index";

    public static void Save(InriverModelJson model, string path)
    {
        using var wb = Build(model);
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);
        wb.SaveAs(path);
    }

    public static InriverModelJson Load(string path)
    {
        using var wb = new XLWorkbook(path);
        return Parse(wb);
    }

    /// <summary>Write a minimal one-CVL example workbook (same columns as a real export) that the user
    /// can edit and re-import. Produced by the same <see cref="Save"/> path so it always round-trips.</summary>
    public static void SaveTemplate(string path)
    {
        var model = new InriverModelJson();
        model.Languages.Add(new JsonLanguage { Name = "en" });
        model.Cvls.Add(new JsonCvl { Id = "ExampleColor", DataType = "String" });
        model.CvlValues.Add(new JsonCvlValue { Id = 1, CvlId = "ExampleColor", Key = "red", Index = 1, Value = JsonSerializer.SerializeToElement("Red") });
        model.CvlValues.Add(new JsonCvlValue { Id = 2, CvlId = "ExampleColor", Key = "green", Index = 2, Value = JsonSerializer.SerializeToElement("Green") });
        Save(model, path);
    }

    public static XLWorkbook Build(InriverModelJson model)
    {
        var wb = new XLWorkbook();
        var langs = model.Languages.Select(l => l.Name).Where(s => !string.IsNullOrEmpty(s)).Distinct().ToList();
        if (langs.Count == 0) langs.Add("en");

        WriteIndex(wb, model);

        var byCvl = model.CvlValues
            .GroupBy(v => v.CvlId)
            .ToDictionary(g => g.Key, g => g.OrderBy(v => v.Index).ThenBy(v => v.Key).ToList());

        foreach (var cvl in model.Cvls.OrderBy(c => c.Id))
        {
            var ws = wb.AddWorksheet(SheetName(cvl.Id));
            ws.Cell(1, 1).Value = "CvlId";
            ws.Cell(1, 2).Value = "DataType";
            ws.Cell(1, 3).Value = cvl.Id;
            ws.Cell(1, 4).Value = cvl.DataType;
            ws.Cell(1, 1).Style.Font.Bold = true;
            ws.Cell(1, 2).Style.Font.Bold = true;

            var headerRow = 3;
            var cols = new List<string> { "Key", "Index", "ParentKey", "Deactivated", "Value" };
            cols.AddRange(langs.Select(l => $"Value[{l}]"));
            for (var i = 0; i < cols.Count; i++) ws.Cell(headerRow, i + 1).Value = cols[i];
            XlIo.StyleHeader(ws.Row(headerRow));
            ws.SheetView.FreezeRows(headerRow);

            var r = headerRow + 1;
            if (byCvl.TryGetValue(cvl.Id, out var values))
            {
                foreach (var v in values)
                {
                    var col = 1;
                    ws.Cell(r, col++).Value = v.Key;
                    ws.Cell(r, col++).Value = v.Index;
                    ws.Cell(r, col++).Value = v.ParentKey ?? string.Empty;
                    ws.Cell(r, col++).Value = v.Deactivated;
                    ws.Cell(r, col++).Value = ScalarValue(v.Value);
                    foreach (var l in langs) ws.Cell(r, col++).Value = LocValue(v.Value, l);
                    r++;
                }
            }
            ws.Columns().AdjustToContents(1, 50, 10, 60);
        }
        return wb;
    }

    static void WriteIndex(IXLWorkbook wb, InriverModelJson model)
    {
        var ws = wb.AddWorksheet(SheetIndex);
        ws.Cell(1, 1).Value = "CVL value workbook";
        ws.Cell(1, 1).Style.Font.Bold = true;
        ws.Cell(1, 1).Style.Font.FontSize = 14;

        ws.Cell(3, 1).Value = "CvlId";
        ws.Cell(3, 2).Value = "DataType";
        ws.Cell(3, 3).Value = "ParentId";
        ws.Cell(3, 4).Value = "ValueCount";
        ws.Cell(3, 5).Value = "Sheet";
        ws.Row(3).Style.Font.Bold = true;

        var r = 4;
        var counts = model.CvlValues.GroupBy(v => v.CvlId).ToDictionary(g => g.Key, g => g.Count());
        foreach (var c in model.Cvls.OrderBy(c => c.Id))
        {
            ws.Cell(r, 1).Value = c.Id;
            ws.Cell(r, 2).Value = c.DataType;
            ws.Cell(r, 3).Value = c.ParentId ?? string.Empty;
            ws.Cell(r, 4).Value = counts.GetValueOrDefault(c.Id);
            ws.Cell(r, 5).Value = SheetName(c.Id);
            r++;
        }
        ws.Columns().AdjustToContents();
    }

    /// <summary>
    /// Parse a CVL workbook. Returns a fresh <see cref="InriverModelJson"/> containing only the
    /// CVLs + CVL values discovered in the workbook (caller merges as needed).
    /// </summary>
    public static InriverModelJson Parse(IXLWorkbook wb)
    {
        var model = new InriverModelJson();
        var langs = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var id = 1;
        foreach (var ws in wb.Worksheets)
        {
            if (ws.Name.Equals(SheetIndex, StringComparison.OrdinalIgnoreCase)) continue;
            var cvlId = ws.Cell(1, 3).GetString();
            var dataType = ws.Cell(1, 4).GetString();
            if (string.IsNullOrEmpty(cvlId)) cvlId = ws.Name;
            if (string.IsNullOrEmpty(cvlId)) continue;
            if (!model.Cvls.Any(c => c.Id == cvlId))
                model.Cvls.Add(new JsonCvl { Id = cvlId, DataType = string.IsNullOrEmpty(dataType) ? "String" : dataType });

            // Header row may be row 3 (with metadata above) or row 1 (compact).
            var headerRow = ws.Cell(3, 1).GetString().Equals("Key", StringComparison.OrdinalIgnoreCase) ? 3 : 1;
            var hdr = XlIo.HeaderMap(ws.Row(headerRow));
            foreach (var kvp in hdr)
                if (kvp.Key.StartsWith("Value[") && kvp.Key.EndsWith("]"))
                    langs.Add(kvp.Key[6..^1]);

            var last = ws.LastRowUsed()?.RowNumber() ?? headerRow;
            for (var r = headerRow + 1; r <= last; r++)
            {
                var row = ws.Row(r);
                var key = XlIo.ReadString(row, hdr, "Key");
                if (string.IsNullOrEmpty(key)) continue;

                JsonElement value;
                var localeMap = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                foreach (var lang in langs)
                {
                    var v = XlIo.ReadString(row, hdr, $"Value[{lang}]");
                    if (!string.IsNullOrEmpty(v)) localeMap[lang] = v;
                }
                if (localeMap.Count > 0)
                    value = JsonSerializer.SerializeToElement(new JsonLocaleString { StringMap = localeMap }, InriverModelJson.Options);
                else
                    value = JsonSerializer.SerializeToElement(XlIo.ReadString(row, hdr, "Value"));

                model.CvlValues.Add(new JsonCvlValue
                {
                    Id = id++,
                    CvlId = cvlId,
                    Key = key,
                    Index = XlIo.ReadInt(row, hdr, "Index"),
                    ParentKey = NullIfEmpty(XlIo.ReadString(row, hdr, "ParentKey")),
                    Deactivated = XlIo.ReadBool(row, hdr, "Deactivated"),
                    Value = value,
                });
            }
        }
        foreach (var l in langs) model.Languages.Add(new JsonLanguage { Name = l });
        if (model.Languages.Count == 0) model.Languages.Add(new JsonLanguage { Name = "en" });
        return model;
    }

    /// <summary>Sanitises <paramref name="cvlId"/> into a legal Excel sheet name (<=31 chars, restricted charset).</summary>
    private static string SheetName(string cvlId)
    {
        var safe = new string(cvlId.Select(c => char.IsLetterOrDigit(c) || c is ' ' or '_' or '-' ? c : '_').ToArray());
        return safe.Length > 31 ? safe[..31] : safe;
    }

    private static string? NullIfEmpty(string s) => string.IsNullOrEmpty(s) ? null : s;

    private static string ScalarValue(JsonElement v) => v.ValueKind switch
    {
        JsonValueKind.String => v.GetString() ?? string.Empty,
        JsonValueKind.Number => v.ToString(),
        JsonValueKind.True => "true",
        JsonValueKind.False => "false",
        _ => string.Empty,
    };
    private static string LocValue(JsonElement v, string lang)
    {
        if (v.ValueKind != JsonValueKind.Object) return string.Empty;
        if (!v.TryGetProperty("StringMap", out var map) || map.ValueKind != JsonValueKind.Object) return string.Empty;
        foreach (var prop in map.EnumerateObject())
            if (string.Equals(prop.Name, lang, StringComparison.OrdinalIgnoreCase) && prop.Value.ValueKind == JsonValueKind.String)
                return prop.Value.GetString() ?? string.Empty;
        return string.Empty;
    }
}
