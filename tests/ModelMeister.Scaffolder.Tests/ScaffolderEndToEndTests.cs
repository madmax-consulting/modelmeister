using System.IO;
using Shouldly;
using ModelMeister.Scaffolder;
using Xunit;

namespace ModelMeister.Scaffolder.Tests;

public class ScaffolderEndToEndTests
{
    [Fact]
    public void Scaffold_emits_project_inside_basedir_with_slnx_and_readme()
    {
        var model = new InriverModelJson
        {
            EntityTypes =
            {
                new JsonEntityType
                {
                    Id = "Product",
                    FieldTypes = new List<JsonFieldType>
                    {
                        new() { Id = "ProductName", DataType = "String", EntityTypeId = "Product" },
                    },
                },
            },
        };

        var outDir = Path.Combine(Path.GetTempPath(), "mm-scaffold-test-" + Guid.NewGuid().ToString("N"));
        try
        {
            new ProjectScaffolder().Scaffold(model, outDir, "Acme.PimModel");

            var projectDir = Path.Combine(outDir, "Acme.PimModel");
            Directory.Exists(projectDir).ShouldBeTrue("project directory should exist under basedir");
            File.Exists(Path.Combine(projectDir, "Acme.PimModel.csproj")).ShouldBeTrue();
            File.Exists(Path.Combine(projectDir, "README.md")).ShouldBeTrue();
            File.Exists(Path.Combine(outDir, "Acme.PimModel.slnx")).ShouldBeTrue();

            var readme = File.ReadAllText(Path.Combine(projectDir, "README.md"));
            readme.ShouldContain("# Acme.PimModel");
            readme.ShouldContain("Generated at");

            var slnx = File.ReadAllText(Path.Combine(outDir, "Acme.PimModel.slnx"));
            slnx.ShouldContain("<Solution>");
            slnx.ShouldContain("Acme.PimModel/Acme.PimModel.csproj");
        }
        finally
        {
            if (Directory.Exists(outDir)) Directory.Delete(outDir, recursive: true);
        }
    }


    [Fact]
    public void BaseClassDetector_finds_shared_members()
    {
        var model = new InriverModelJson
        {
            EntityTypes =
            {
                new JsonEntityType
                {
                    Id = "Product",
                    FieldTypes = new List<JsonFieldType>
                    {
                        new() { Id = "ProductName", DataType = "LocaleString", EntityTypeId = "Product" },
                        new() { Id = "ProductDescription", DataType = "LocaleString", EntityTypeId = "Product" },
                    },
                },
                new JsonEntityType
                {
                    Id = "Item",
                    FieldTypes = new List<JsonFieldType>
                    {
                        new() { Id = "ItemName", DataType = "LocaleString", EntityTypeId = "Item" },
                        new() { Id = "ItemDescription", DataType = "LocaleString", EntityTypeId = "Item" },
                    },
                },
            },
        };
        var bases = BaseClassDetector.Detect(model);
        bases.ShouldHaveSingleItem();
        bases[0].Members.Select(m => m.PropertyName).ShouldBe(new[] { "Name", "Description" }, ignoreOrder: true);
        bases[0].EntityTypeIds.ShouldBe(new[] { "Product", "Item" }, ignoreOrder: true);
    }
}
