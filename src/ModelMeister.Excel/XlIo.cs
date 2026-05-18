using ClosedXML.Excel;

namespace ModelMeister.Excel;

/// <summary>
/// Shared, low-level helpers used by every workbook writer/reader in this project. Kept
/// <c>internal</c> on purpose — callers are the three sibling workbook classes
/// (<see cref="ModelWorkbook"/>, <see cref="UsersWorkbook"/>, <see cref="CvlValuesWorkbook"/>);
/// nothing outside the project should depend on this surface.
/// </summary>
internal static class XlIo
{
    /// <summary>Header-row background colour. Matches the project's dark-on-white palette.</summary>
    public const string HeaderBackgroundHex = "#1f2937";

    /// <summary>Applies bold + dark fill + white font to a row, intended for header rows.</summary>
    public static void StyleHeader(IXLRow row)
    {
        row.Style.Font.Bold = true;
        row.Style.Fill.BackgroundColor = XLColor.FromHtml(HeaderBackgroundHex);
        row.Style.Font.FontColor = XLColor.White;
    }

    /// <summary>
    /// Builds a header-name -> column-index map from the supplied <paramref name="headerRow"/>.
    /// Duplicate header names keep the leftmost column. Comparison is case-insensitive.
    /// </summary>
    public static Dictionary<string, int> HeaderMap(IXLRow headerRow)
    {
        var map = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        var lastCol = headerRow.LastCellUsed()?.Address.ColumnNumber ?? 0;
        for (var c = 1; c <= lastCol; c++)
        {
            var v = headerRow.Cell(c).GetString();
            if (!string.IsNullOrEmpty(v) && !map.ContainsKey(v)) map[v] = c;
        }
        return map;
    }

    /// <summary>Convenience overload that reads the header from row 1.</summary>
    public static Dictionary<string, int> HeaderMap(IXLWorksheet ws) => HeaderMap(ws.Row(1));

    /// <summary>Returns the trimmed string value of the cell under <paramref name="key"/>, or empty when the column is absent.</summary>
    public static string ReadString(IXLRow row, IReadOnlyDictionary<string, int> hdr, string key)
        => hdr.TryGetValue(key, out var c) ? row.Cell(c).GetString().Trim() : string.Empty;

    /// <summary>Returns the int value of the cell under <paramref name="key"/>, or 0 when absent/blank/unparseable.</summary>
    public static int ReadInt(IXLRow row, IReadOnlyDictionary<string, int> hdr, string key)
    {
        if (!hdr.TryGetValue(key, out var c)) return 0;
        var cell = row.Cell(c);
        if (cell.IsEmpty()) return 0;
        return cell.TryGetValue<int>(out var i) ? i : int.TryParse(cell.GetString(), out var p) ? p : 0;
    }

    /// <summary>
    /// Returns the bool value of the cell under <paramref name="key"/>. Recognises native Excel booleans
    /// plus the string forms <c>true</c>, <c>yes</c> and <c>1</c> (case-insensitive).
    /// </summary>
    public static bool ReadBool(IXLRow row, IReadOnlyDictionary<string, int> hdr, string key)
    {
        if (!hdr.TryGetValue(key, out var c)) return false;
        var cell = row.Cell(c);
        if (cell.IsEmpty()) return false;
        if (cell.TryGetValue<bool>(out var b)) return b;
        var s = cell.GetString().Trim();
        return s.Equals("true", StringComparison.OrdinalIgnoreCase)
            || s.Equals("yes", StringComparison.OrdinalIgnoreCase)
            || s == "1";
    }
}
