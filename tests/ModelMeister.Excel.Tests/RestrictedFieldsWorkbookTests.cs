using Shouldly;
using ModelMeister.Excel;
using Xunit;

namespace ModelMeister.Excel.Tests;

public class RestrictedFieldsWorkbookTests
{
    [Fact]
    public void Round_trips_restricted_field_rows()
    {
        var path = Path.Combine(Path.GetTempPath(), $"mm-restricted-{Guid.NewGuid():N}.xlsx");
        try
        {
            var rows = new List<RestrictedFieldsWorkbook.RestrictedFieldRow>
            {
                new() { RoleName = "Editor", RestrictionType = "ReadOnly", FieldTypeId = "ProductName", Notes = "lock name" },
                new() { RoleName = "Reader", RestrictionType = "Hidden", EntityTypeId = "Product", CategoryId = "Specs" },
            };
            var availableRoles = new List<string> { "Editor", "Reader", "Admin" };
            RestrictedFieldsWorkbook.Save(rows, availableRoles, path);

            var loaded = RestrictedFieldsWorkbook.Load(path);
            loaded.Count.ShouldBe(2);

            var first = loaded.Single(r => r.RoleName == "Editor");
            first.RestrictionType.ShouldBe("ReadOnly");
            first.FieldTypeId.ShouldBe("ProductName");
            first.Notes.ShouldBe("lock name");

            var second = loaded.Single(r => r.RoleName == "Reader");
            second.RestrictionType.ShouldBe("Hidden");
            second.EntityTypeId.ShouldBe("Product");
            second.CategoryId.ShouldBe("Specs");
        }
        finally { if (File.Exists(path)) File.Delete(path); }
    }

    [Fact]
    public void Skips_fully_blank_rows()
    {
        var path = Path.Combine(Path.GetTempPath(), $"mm-restricted-{Guid.NewGuid():N}.xlsx");
        try
        {
            var rows = new List<RestrictedFieldsWorkbook.RestrictedFieldRow>
            {
                new() { RoleName = "Editor", RestrictionType = "ReadOnly", FieldTypeId = "ProductName" },
                new() { RoleName = "", RestrictionType = "" },
            };
            RestrictedFieldsWorkbook.Save(rows, new List<string>(), path);

            var loaded = RestrictedFieldsWorkbook.Load(path);
            loaded.Count.ShouldBe(1);
            loaded[0].RoleName.ShouldBe("Editor");
        }
        finally { if (File.Exists(path)) File.Delete(path); }
    }
}
