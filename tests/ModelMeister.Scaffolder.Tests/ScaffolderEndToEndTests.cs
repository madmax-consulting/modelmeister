using Shouldly;
using ModelMeister.Scaffolder;
using Xunit;

namespace ModelMeister.Scaffolder.Tests;

public class ScaffolderEndToEndTests
{
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
