using ClosedXML.Excel;

namespace ModelMeister.Excel;

/// <summary>
/// Excel workbook for bulk user provisioning. Two sheets:
/// <list type="bullet">
///   <item><c>Users</c> — one row per user, with a semicolon-separated <c>Roles</c> column.</item>
///   <item><c>Roles</c> — reference list of roles available in the target env.</item>
/// </list>
/// </summary>
public static class UsersWorkbook
{
    public const string SheetUsers = "Users";
    public const string SheetRoles = "Roles";
    public const string SheetReadme = "_Readme";

    /// <summary>A single row on the <see cref="SheetUsers"/> sheet.</summary>
    public sealed class UserRow
    {
        public string Username { get; set; } = "";
        public string Email { get; set; } = "";
        public string FirstName { get; set; } = "";
        public string LastName { get; set; } = "";
        public string Company { get; set; } = "";
        public List<string> Roles { get; set; } = [];
        public string Language { get; set; } = "en";
        public bool GenerateApiKey { get; set; }
        public string Notes { get; set; } = "";
    }

    public static void Save(IReadOnlyList<UserRow> users, IReadOnlyList<string> availableRoles, string path)
    {
        using var wb = Build(users, availableRoles);
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);
        wb.SaveAs(path);
    }

    public static IReadOnlyList<UserRow> Load(string path)
    {
        using var wb = new XLWorkbook(path);
        return Parse(wb);
    }

    public static XLWorkbook Build(IReadOnlyList<UserRow> users, IReadOnlyList<string> availableRoles)
    {
        var wb = new XLWorkbook();
        var readme = wb.AddWorksheet(SheetReadme);
        readme.Cell(1, 1).Value = "User provisioning workbook";
        readme.Cell(1, 1).Style.Font.Bold = true;
        readme.Cell(1, 1).Style.Font.FontSize = 14;
        readme.Cell(3, 1).Value = "Roles column accepts a semicolon-separated list. Roles must already exist in the target environment.";
        readme.Cell(4, 1).Value = "Set GenerateApiKey=true to mint a REST API key for the user after creation.";
        readme.Cell(5, 1).Value = "Run with:  modelmeister users provision --excel <path>";

        var ws = wb.AddWorksheet(SheetUsers);
        var headers = new[]
        {
            "Username", "Email", "FirstName", "LastName", "Company",
            "Roles", "Language", "GenerateApiKey", "Notes",
        };
        for (var i = 0; i < headers.Length; i++) ws.Cell(1, i + 1).Value = headers[i];
        XlIo.StyleHeader(ws.Row(1));
        ws.SheetView.FreezeRows(1);

        var r = 2;
        foreach (var u in users)
        {
            var c = 1;
            ws.Cell(r, c++).Value = u.Username;
            ws.Cell(r, c++).Value = u.Email;
            ws.Cell(r, c++).Value = u.FirstName;
            ws.Cell(r, c++).Value = u.LastName;
            ws.Cell(r, c++).Value = u.Company;
            ws.Cell(r, c++).Value = string.Join(";", u.Roles);
            ws.Cell(r, c++).Value = string.IsNullOrEmpty(u.Language) ? "en" : u.Language;
            ws.Cell(r, c++).Value = u.GenerateApiKey;
            ws.Cell(r, c++).Value = u.Notes ?? "";
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

    public static IReadOnlyList<UserRow> Parse(IXLWorkbook wb)
    {
        var result = new List<UserRow>();
        if (!wb.TryGetWorksheet(SheetUsers, out var ws)) return result;
        var hdr = XlIo.HeaderMap(ws);
        var last = ws.LastRowUsed()?.RowNumber() ?? 1;
        for (var r = 2; r <= last; r++)
        {
            var row = ws.Row(r);
            var username = XlIo.ReadString(row, hdr, "Username");
            if (string.IsNullOrEmpty(username)) continue;
            var language = XlIo.ReadString(row, hdr, "Language");
            result.Add(new UserRow
            {
                Username = username,
                Email = XlIo.ReadString(row, hdr, "Email"),
                FirstName = XlIo.ReadString(row, hdr, "FirstName"),
                LastName = XlIo.ReadString(row, hdr, "LastName"),
                Company = XlIo.ReadString(row, hdr, "Company"),
                Roles = XlIo.ReadString(row, hdr, "Roles")
                    .Split([';', ','], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                    .ToList(),
                Language = string.IsNullOrEmpty(language) ? "en" : language,
                GenerateApiKey = XlIo.ReadBool(row, hdr, "GenerateApiKey"),
                Notes = XlIo.ReadString(row, hdr, "Notes"),
            });
        }
        return result;
    }
}
