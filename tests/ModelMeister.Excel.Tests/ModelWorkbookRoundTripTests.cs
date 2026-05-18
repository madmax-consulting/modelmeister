using System.Text.Json;
using Shouldly;
using ModelMeister.Excel;
using ModelMeister.Scaffolder;
using Xunit;

namespace ModelMeister.Excel.Tests;

/// <summary>
/// Pin the Excel ↔ JSON round-trip. After a save-then-load cycle, every concept's key fields must
/// match. Some derived fields (CVL value <c>Id</c>) are regenerated on load — those are exempt.
/// </summary>
public class ModelWorkbookRoundTripTests
{
    [Fact]
    public void Saves_and_loads_an_empty_model_with_default_language()
    {
        var path = Path.Combine(Path.GetTempPath(), $"mm-rt-empty-{Guid.NewGuid():N}.xlsx");
        try
        {
            var src = new InriverModelJson();
            ModelWorkbook.Save(src, path);
            var loaded = ModelWorkbook.Load(path);
            loaded.Languages.Count.ShouldBe(1);
            loaded.Languages[0].Name.ShouldBe("en");
            loaded.EntityTypes.ShouldBeEmpty();
            loaded.Cvls.ShouldBeEmpty();
        }
        finally { if (File.Exists(path)) File.Delete(path); }
    }

    [Fact]
    public void Round_trips_a_realistic_model_shape()
    {
        var src = MakeRichModel();
        var path = Path.Combine(Path.GetTempPath(), $"mm-rt-rich-{Guid.NewGuid():N}.xlsx");
        try
        {
            ModelWorkbook.Save(src, path);
            var loaded = ModelWorkbook.Load(path);

            loaded.Languages.Select(l => l.Name).ShouldBe(new[] { "en", "sv" });
            loaded.Categories.Count.ShouldBe(2);
            loaded.EntityTypes.Count.ShouldBe(2);

            var product = loaded.EntityTypes.Single(e => e.Id == "Product");
            product.GetDisplayNameFieldTypeId.ShouldBe("ProductName");

            loaded.FieldTypes.Count.ShouldBe(3);
            var name = loaded.FieldTypes.Single(f => f.Id == "ProductName");
            name.IsDisplayName.ShouldBeTrue();
            name.Mandatory.ShouldBeTrue();
            name.Name?.StringMap?["en"].ShouldBe("Product name");

            loaded.Cvls.Count.ShouldBe(1);
            loaded.CvlValues.Count.ShouldBe(2);
            loaded.CvlValues.Select(v => v.Key).ShouldBe(new[] { "EU", "US" });

            loaded.LinkTypes.Single().Id.ShouldBe("ProductSupplier");

            (loaded.Security?.Roles?.Count ?? 0).ShouldBe(1);
            loaded.Security!.Roles![0].Permissions!.Select(p => p.Name).ShouldContain("View");
        }
        finally { if (File.Exists(path)) File.Delete(path); }
    }

    [Fact]
    public void CvlValuesWorkbook_round_trips_per_cvl_sheets()
    {
        var src = MakeRichModel();
        var path = Path.Combine(Path.GetTempPath(), $"mm-cvlrt-{Guid.NewGuid():N}.xlsx");
        try
        {
            CvlValuesWorkbook.Save(src, path);
            var loaded = CvlValuesWorkbook.Load(path);
            loaded.Cvls.Single().Id.ShouldBe("Markets");
            loaded.CvlValues.Select(v => v.Key).ShouldBe(new[] { "EU", "US" });
        }
        finally { if (File.Exists(path)) File.Delete(path); }
    }

    static InriverModelJson MakeRichModel()
    {
        var model = new InriverModelJson
        {
            Languages = new() { new() { Name = "en" }, new() { Name = "sv" } },
            Categories = new()
            {
                new() { Id = "Marketing", Index = 1, Name = Loc("Marketing", "Marknadsföring") },
                new() { Id = "Technical", Index = 2, Name = Loc("Technical", "Tekniskt") },
            },
            EntityTypes = new()
            {
                new() { Id = "Product",  Name = Loc("Product"),  GetDisplayNameFieldTypeId = "ProductName" },
                new() { Id = "Supplier", Name = Loc("Supplier") },
            },
            FieldTypes = new()
            {
                new() { Id = "ProductName", EntityTypeId = "Product", DataType = "LocaleString", Mandatory = true, IsDisplayName = true, Name = Loc("Product name", "Produktnamn") },
                new() { Id = "ProductPrice", EntityTypeId = "Product", DataType = "Double", CategoryId = "Marketing" },
                new() { Id = "SupplierCode", EntityTypeId = "Supplier", DataType = "String", Unique = true },
            },
            FieldSets = new()
            {
                new() { Id = "ProductCore", EntityTypeId = "Product", FieldTypes = new() { "ProductName", "ProductPrice" }, Name = Loc("Core") },
            },
            Cvls = new()
            {
                new() { Id = "Markets", DataType = "String", CustomValueList = true },
            },
            CvlValues = new()
            {
                new() { Id = 1, CvlId = "Markets", Key = "EU", Value = JsonSerializer.SerializeToElement("Europe"), Index = 0 },
                new() { Id = 2, CvlId = "Markets", Key = "US", Value = JsonSerializer.SerializeToElement("America"), Index = 1 },
            },
            LinkTypes = new()
            {
                new() { Id = "ProductSupplier", SourceEntityTypeId = "Product", TargetEntityTypeId = "Supplier", Index = 1, SourceName = Loc("supplies"), TargetName = Loc("supplied by") },
            },
            Security = new JsonSecurity
            {
                Roles = new() { new() { Id = 1, Name = "Editor", Description = "Edits stuff", Permissions = new() { new() { Id = 1, Name = "View" }, new() { Id = 2, Name = "Edit" } } } },
            },
        };
        return model;
    }

    static JsonLocaleString Loc(string en, string? sv = null) => new()
    {
        StringMap = sv is null
            ? new Dictionary<string, string> { ["en"] = en }
            : new Dictionary<string, string> { ["en"] = en, ["sv"] = sv },
    };
}
