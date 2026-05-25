using ClosedXML.Excel;

namespace ModelMeister.Excel;

/// <summary>
/// Excel workbook for bulk role provisioning. Sheets:
/// <list type="bullet">
///   <item><c>Roles</c> — one row per role, with a semicolon-separated <c>Permissions</c> column.</item>
///   <item><c>Permissions</c> — reference list of permissions available in the target env.</item>
/// </list>
/// Importing upserts by role name: missing roles are created, existing ones get their description +
/// permission set reconciled.
/// </summary>
public static class RolesWorkbook
{
    public const string SheetRoles = "Roles";
    public const string SheetPermissions = "Permissions";
    public const string SheetReadme = "_Readme";

    /// <summary>A single row on the <see cref="SheetRoles"/> sheet.</summary>
    public sealed class RoleRow
    {
        public string Name { get; set; } = "";
        public string Description { get; set; } = "";
        public List<string> Permissions { get; set; } = [];
        public string Notes { get; set; } = "";
    }

    public static void Save(IReadOnlyList<RoleRow> roles, IReadOnlyList<string> availablePermissions, string path)
    {
        using var wb = Build(roles, availablePermissions);
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);
        wb.SaveAs(path);
    }

    public static IReadOnlyList<RoleRow> Load(string path)
    {
        using var wb = new XLWorkbook(path);
        return Parse(wb);
    }

    public static XLWorkbook Build(IReadOnlyList<RoleRow> roles, IReadOnlyList<string> availablePermissions)
    {
        var wb = new XLWorkbook();
        var readme = wb.AddWorksheet(SheetReadme);
        readme.Cell(1, 1).Value = "Role provisioning workbook";
        readme.Cell(1, 1).Style.Font.Bold = true;
        readme.Cell(1, 1).Style.Font.FontSize = 14;
        readme.Cell(3, 1).Value = "Permissions column accepts a semicolon-separated list. Permissions must already exist in the target environment.";
        readme.Cell(4, 1).Value = "Importing upserts by role Name: missing roles are created, existing roles get their description + permission set synced.";

        var ws = wb.AddWorksheet(SheetRoles);
        var headers = new[] { "Name", "Description", "Permissions", "Notes" };
        for (var i = 0; i < headers.Length; i++) ws.Cell(1, i + 1).Value = headers[i];
        XlIo.StyleHeader(ws.Row(1));
        ws.SheetView.FreezeRows(1);

        var r = 2;
        foreach (var role in roles)
        {
            var c = 1;
            ws.Cell(r, c++).Value = role.Name;
            ws.Cell(r, c++).Value = role.Description;
            ws.Cell(r, c++).Value = string.Join(";", role.Permissions);
            ws.Cell(r, c++).Value = role.Notes ?? "";
            r++;
        }
        ws.Columns().AdjustToContents();

        var perms = wb.AddWorksheet(SheetPermissions);
        perms.Cell(1, 1).Value = "Available permissions in target environment";
        perms.Cell(1, 1).Style.Font.Bold = true;
        perms.Cell(3, 1).Value = "Name";
        perms.Row(3).Style.Font.Bold = true;
        for (var i = 0; i < availablePermissions.Count; i++) perms.Cell(4 + i, 1).Value = availablePermissions[i];
        perms.Columns().AdjustToContents();
        return wb;
    }

    public static IReadOnlyList<RoleRow> Parse(IXLWorkbook wb)
    {
        var result = new List<RoleRow>();
        if (!wb.TryGetWorksheet(SheetRoles, out var ws)) return result;
        var hdr = XlIo.HeaderMap(ws);
        var last = ws.LastRowUsed()?.RowNumber() ?? 1;
        for (var r = 2; r <= last; r++)
        {
            var row = ws.Row(r);
            var name = XlIo.ReadString(row, hdr, "Name");
            if (string.IsNullOrEmpty(name)) continue;
            result.Add(new RoleRow
            {
                Name = name,
                Description = XlIo.ReadString(row, hdr, "Description"),
                Permissions = XlIo.ReadString(row, hdr, "Permissions")
                    .Split([';', ','], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                    .ToList(),
                Notes = XlIo.ReadString(row, hdr, "Notes"),
            });
        }
        return result;
    }
}
