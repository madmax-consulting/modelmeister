using ClosedXML.Excel;

namespace ModelMeister.Excel;

/// <summary>
/// Excel workbook for bulk restricted-field-permission provisioning. Sheets:
/// <list type="bullet">
///   <item><c>RestrictedFields</c> — one row per permission, keyed by role name + scope ids.</item>
///   <item><c>Roles</c> — reference list of role names available in the target env.</item>
/// </list>
/// Restricted-field permissions have no update operation; importing adds rows whose natural key is
/// not already present (the importer skips duplicates).
/// </summary>
public static class RestrictedFieldsWorkbook
{
    public const string SheetRestrictedFields = "RestrictedFields";
    public const string SheetRoles = "Roles";
    public const string SheetReadme = "_Readme";

    /// <summary>A single row on the <see cref="SheetRestrictedFields"/> sheet.</summary>
    public sealed class RestrictedFieldRow
    {
        public string RoleName { get; set; } = "";
        public string RestrictionType { get; set; } = "";
        public string EntityTypeId { get; set; } = "";
        public string FieldTypeId { get; set; } = "";
        public string CategoryId { get; set; } = "";
        public string Notes { get; set; } = "";
    }

    public static void Save(IReadOnlyList<RestrictedFieldRow> rows, IReadOnlyList<string> availableRoles, string path)
    {
        using var wb = Build(rows, availableRoles);
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);
        wb.SaveAs(path);
    }

    public static IReadOnlyList<RestrictedFieldRow> Load(string path)
    {
        using var wb = new XLWorkbook(path);
        return Parse(wb);
    }

    public static XLWorkbook Build(IReadOnlyList<RestrictedFieldRow> rows, IReadOnlyList<string> availableRoles)
    {
        var wb = new XLWorkbook();
        var readme = wb.AddWorksheet(SheetReadme);
        readme.Cell(1, 1).Value = "Restricted-field permission workbook";
        readme.Cell(1, 1).Style.Font.Bold = true;
        readme.Cell(1, 1).Style.Font.FontSize = 14;
        readme.Cell(3, 1).Value = "RoleName must already exist in the target environment. RestrictionType is the inriver restriction kind.";
        readme.Cell(4, 1).Value = "EntityTypeId / FieldTypeId / CategoryId scope the restriction; leave blank where not applicable.";
        readme.Cell(5, 1).Value = "Restricted-field permissions cannot be updated — import adds rows that aren't already present.";

        var ws = wb.AddWorksheet(SheetRestrictedFields);
        var headers = new[] { "RoleName", "RestrictionType", "EntityTypeId", "FieldTypeId", "CategoryId", "Notes" };
        for (var i = 0; i < headers.Length; i++) ws.Cell(1, i + 1).Value = headers[i];
        XlIo.StyleHeader(ws.Row(1));
        ws.SheetView.FreezeRows(1);

        var r = 2;
        foreach (var row in rows)
        {
            var c = 1;
            ws.Cell(r, c++).Value = row.RoleName;
            ws.Cell(r, c++).Value = row.RestrictionType;
            ws.Cell(r, c++).Value = row.EntityTypeId;
            ws.Cell(r, c++).Value = row.FieldTypeId;
            ws.Cell(r, c++).Value = row.CategoryId;
            ws.Cell(r, c++).Value = row.Notes ?? "";
            r++;
        }
        ws.Columns().AdjustToContents();

        var roles = wb.AddWorksheet(SheetRoles);
        roles.Cell(1, 1).Value = "Available roles in target environment";
        roles.Cell(1, 1).Style.Font.Bold = true;
        roles.Cell(3, 1).Value = "Name";
        roles.Row(3).Style.Font.Bold = true;
        for (var i = 0; i < availableRoles.Count; i++) roles.Cell(4 + i, 1).Value = availableRoles[i];
        roles.Columns().AdjustToContents();
        return wb;
    }

    public static IReadOnlyList<RestrictedFieldRow> Parse(IXLWorkbook wb)
    {
        var result = new List<RestrictedFieldRow>();
        if (!wb.TryGetWorksheet(SheetRestrictedFields, out var ws)) return result;
        var hdr = XlIo.HeaderMap(ws);
        var last = ws.LastRowUsed()?.RowNumber() ?? 1;
        for (var r = 2; r <= last; r++)
        {
            var row = ws.Row(r);
            var roleName = XlIo.ReadString(row, hdr, "RoleName");
            var restrictionType = XlIo.ReadString(row, hdr, "RestrictionType");
            if (string.IsNullOrEmpty(roleName) && string.IsNullOrEmpty(restrictionType)) continue;
            result.Add(new RestrictedFieldRow
            {
                RoleName = roleName,
                RestrictionType = restrictionType,
                EntityTypeId = XlIo.ReadString(row, hdr, "EntityTypeId"),
                FieldTypeId = XlIo.ReadString(row, hdr, "FieldTypeId"),
                CategoryId = XlIo.ReadString(row, hdr, "CategoryId"),
                Notes = XlIo.ReadString(row, hdr, "Notes"),
            });
        }
        return result;
    }
}
