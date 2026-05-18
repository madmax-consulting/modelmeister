using Shouldly;
using ModelMeister.Inriver.Snapshot;
using ModelMeister.Model.Primitives;
using Xunit;

namespace ModelMeister.Scaffolder.Tests;

public class LiveModelConverterTests
{
    private static LiveModel BuildLive(IReadOnlyList<string>? languages = null) => new()
    {
        EnvironmentUrl = "https://example",
        CapturedUtc = DateTime.UtcNow,
        Languages = languages ?? new[] { "en", "sv" },
        Categories = new[]
        {
            new LiveCategory { Id = "CategoryGeneral", Name = new LocaleString("General").With("sv", "Allmänt"), Index = 0 },
        },
        Cvls = new[]
        {
            new LiveCvl
            {
                Id = "Brand",
                DataTypeRaw = "String",
                DataType = CvlDataType.String,
                CustomValueList = false,
                Values = new[]
                {
                    new LiveCvlValue { Id = 1, CvlId = "Brand", Key = "Nike", Index = 0, Value = new LocaleString("Nike") },
                },
            },
        },
        EntityTypes = new[]
        {
            new LiveEntityType
            {
                Id = "Product",
                Name = new LocaleString("Product"),
                Fields = new[]
                {
                    new LiveFieldType
                    {
                        Id = "ProductName",
                        EntityTypeId = "Product",
                        Name = new LocaleString("Name"),
                        DataType = Datatype.LocaleString,
                        IsDisplayName = true,
                    },
                    new LiveFieldType
                    {
                        Id = "ProductBrand",
                        EntityTypeId = "Product",
                        Name = new LocaleString("Brand"),
                        DataType = Datatype.Cvl,
                        CvlId = "Brand",
                    },
                },
            },
        },
    };

    [Fact]
    public void Converts_live_model_into_inriver_json_shape()
    {
        var json = LiveModelConverter.ToJsonModel(BuildLive());

        json.Languages.Select(l => l.Name).ShouldBe(new[] { "en", "sv" });
        json.Categories.Single().Id.ShouldBe("CategoryGeneral");
        json.EntityTypes.Single().FieldTypes!.Count.ShouldBe(2);
        json.FieldTypes.Count.ShouldBe(2);
        json.Cvls.Single().DataType.ShouldBe("String");
        json.CvlValues.Single().Key.ShouldBe("Nike");
    }

    [Fact]
    public void StringMap_orders_master_language_first()
    {
        // sv is in the languages list before en — first entry should be sv.
        var live = BuildLive(new[] { "sv", "en" });
        var json = LiveModelConverter.ToJsonModel(live);
        var general = json.Categories.Single();
        general.Name!.StringMap!.Keys.First().ShouldBe("sv");
    }

    [Fact]
    public void Scaffolds_directly_from_live_model()
    {
        var outDir = Path.Combine(Path.GetTempPath(), "mm-conv-" + Guid.NewGuid().ToString("N"));
        try
        {
            var json = LiveModelConverter.ToJsonModel(BuildLive());
            var result = new ProjectScaffolder().Scaffold(json, outDir, "ConvTest.Model");
            var projectDir = Path.Combine(outDir, "ConvTest.Model");
            File.Exists(Path.Combine(projectDir, "EntityTypes", "Product.cs")).ShouldBeTrue();
            File.Exists(Path.Combine(projectDir, "Cvls", "BrandCvl.cs")).ShouldBeTrue();
            result.Files.Count.ShouldBeGreaterThan(2);
        }
        finally
        {
            if (Directory.Exists(outDir)) Directory.Delete(outDir, recursive: true);
        }
    }
}
