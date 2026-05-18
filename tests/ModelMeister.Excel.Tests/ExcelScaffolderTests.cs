using Shouldly;
using ModelMeister.Excel;
using ModelMeister.Scaffolder;
using Xunit;
using System.Text.Json;

namespace ModelMeister.Excel.Tests;

/// <summary>
/// End-to-end Excel → scaffold path. The same model written through the workbook must scaffold to
/// a buildable typed project (same shape as the JSON-driven flow).
/// </summary>
public class ExcelScaffolderTests
{
    [Fact]
    public void Scaffold_from_excel_produces_a_csproj_with_entity_types_and_cvls()
    {
        var xlsx = Path.Combine(Path.GetTempPath(), $"mm-scaffold-{Guid.NewGuid():N}.xlsx");
        var outDir = Path.Combine(Path.GetTempPath(), $"mm-scaffold-out-{Guid.NewGuid():N}");
        try
        {
            var src = new InriverModelJson
            {
                Languages = new() { new() { Name = "en" } },
                EntityTypes = new()
                {
                    new() { Id = "Product",  Name = new() { StringMap = new() { ["en"] = "Product" } }, GetDisplayNameFieldTypeId = "ProductName" },
                    new() { Id = "Supplier", Name = new() { StringMap = new() { ["en"] = "Supplier" } } },
                },
                FieldTypes = new()
                {
                    new() { Id = "ProductName", EntityTypeId = "Product", DataType = "LocaleString", IsDisplayName = true, Mandatory = true },
                    new() { Id = "ProductPrice", EntityTypeId = "Product", DataType = "Double" },
                    new() { Id = "SupplierCode", EntityTypeId = "Supplier", DataType = "String", Unique = true },
                },
                Cvls = new() { new() { Id = "Markets", DataType = "String" } },
                CvlValues = new()
                {
                    new() { Id = 1, CvlId = "Markets", Key = "EU", Value = JsonSerializer.SerializeToElement("Europe"), Index = 0 },
                },
                LinkTypes = new()
                {
                    new() { Id = "ProductSupplier", SourceEntityTypeId = "Product", TargetEntityTypeId = "Supplier", Index = 1 },
                },
            };

            ModelWorkbook.Save(src, xlsx);
            var result = ExcelScaffolder.ScaffoldFromExcel(xlsx, outDir, "Acme.PimModel", detectBaseClasses: false, emitCvlValues: true);

            // Output now lives under outDir/<projectName>/ so the layout matches a normal .sln/.slnx
            // workspace (sibling .slnx, project dir, etc.).
            var projectDir = Path.Combine(outDir, "Acme.PimModel");
            result.Files.Count.ShouldBeGreaterThan(0);
            Directory.Exists(Path.Combine(projectDir, "EntityTypes")).ShouldBeTrue();
            Directory.Exists(Path.Combine(projectDir, "Cvls")).ShouldBeTrue();
            Directory.GetFiles(projectDir, "*.csproj").ShouldNotBeEmpty();
            var productFile = Path.Combine(projectDir, "EntityTypes", "Product.cs");
            File.Exists(productFile).ShouldBeTrue();
            var content = File.ReadAllText(productFile);
            content.ShouldContain("DisplayName", customMessage: $"Product.cs:\n{content}");
            content.ShouldContain("Mandatory");
        }
        finally
        {
            if (File.Exists(xlsx)) File.Delete(xlsx);
            if (Directory.Exists(outDir)) Directory.Delete(outDir, recursive: true);
        }
    }

    [Fact]
    public void Scaffold_skips_cvl_values_when_requested()
    {
        var xlsx = Path.Combine(Path.GetTempPath(), $"mm-novalues-{Guid.NewGuid():N}.xlsx");
        var outDir = Path.Combine(Path.GetTempPath(), $"mm-novalues-out-{Guid.NewGuid():N}");
        try
        {
            var src = new InriverModelJson
            {
                Languages = new() { new() { Name = "en" } },
                Cvls = new() { new() { Id = "Markets", DataType = "String" } },
                CvlValues = new()
                {
                    new() { Id = 1, CvlId = "Markets", Key = "EU", Value = JsonSerializer.SerializeToElement("Europe") },
                },
            };
            ModelWorkbook.Save(src, xlsx);
            ExcelScaffolder.ScaffoldFromExcel(xlsx, outDir, "Acme.PimModel", detectBaseClasses: false, emitCvlValues: false);

            var cvlFile = Path.Combine(outDir, "Acme.PimModel", "Cvls", "MarketsCvl.cs");
            File.Exists(cvlFile).ShouldBeTrue();
            File.ReadAllText(cvlFile).ShouldNotContain("\"EU\"");
        }
        finally
        {
            if (File.Exists(xlsx)) File.Delete(xlsx);
            if (Directory.Exists(outDir)) Directory.Delete(outDir, recursive: true);
        }
    }
}
