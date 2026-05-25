using Shouldly;
using ModelMeister.Excel;
using Xunit;

namespace ModelMeister.Excel.Tests;

public class RolesWorkbookTests
{
    [Fact]
    public void Round_trips_roles_and_permissions()
    {
        var path = Path.Combine(Path.GetTempPath(), $"mm-roles-{Guid.NewGuid():N}.xlsx");
        try
        {
            var roles = new List<RolesWorkbook.RoleRow>
            {
                new() { Name = "Editor", Description = "Can edit", Permissions = new() { "EditEntity", "ViewEntity" }, Notes = "Core" },
                new() { Name = "Reader", Description = "Read only", Permissions = new() { "ViewEntity" } },
            };
            var availablePermissions = new List<string> { "EditEntity", "ViewEntity", "DeleteEntity" };
            RolesWorkbook.Save(roles, availablePermissions, path);

            var loaded = RolesWorkbook.Load(path);
            loaded.Count.ShouldBe(2);

            var editor = loaded.Single(r => r.Name == "Editor");
            editor.Description.ShouldBe("Can edit");
            editor.Permissions.ShouldBe(new[] { "EditEntity", "ViewEntity" });
            editor.Notes.ShouldBe("Core");

            var reader = loaded.Single(r => r.Name == "Reader");
            reader.Permissions.ShouldBe(new[] { "ViewEntity" });
        }
        finally { if (File.Exists(path)) File.Delete(path); }
    }

    [Fact]
    public void Skips_rows_with_blank_name()
    {
        var path = Path.Combine(Path.GetTempPath(), $"mm-roles-{Guid.NewGuid():N}.xlsx");
        try
        {
            var roles = new List<RolesWorkbook.RoleRow>
            {
                new() { Name = "Editor", Permissions = new() { "EditEntity" } },
                new() { Name = "", Permissions = new() { "ViewEntity" } },
            };
            RolesWorkbook.Save(roles, new List<string>(), path);

            var loaded = RolesWorkbook.Load(path);
            loaded.Count.ShouldBe(1);
            loaded[0].Name.ShouldBe("Editor");
        }
        finally { if (File.Exists(path)) File.Delete(path); }
    }
}
