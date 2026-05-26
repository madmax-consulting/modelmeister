using System.IO;
using Shouldly;
using ModelMeister.Scaffolder;
using Xunit;

namespace ModelMeister.Scaffolder.Tests;

/// <summary>
/// The scaffolder must invert the JSON completeness section into the C# DSL: a
/// <c>CompletenessGroup</c> class per group, and the matching rule attribute stamped onto the field its
/// settings reference (keyed by <c>FieldTypeId</c>).
/// </summary>
public class CompletenessScaffoldTests
{
    private static InriverModelJson ModelWithCompleteness() => new()
    {
        EntityTypes =
        {
            new JsonEntityType
            {
                Id = "Product",
                FieldTypes = new List<JsonFieldType>
                {
                    new() { Id = "ProductName", DataType = "String", EntityTypeId = "Product" },
                    new() { Id = "ProductSku", DataType = "String", EntityTypeId = "Product" },
                },
            },
        },
        Completeness = new JsonCompleteness
        {
            CompletenessDefinitions = new()
            {
                new JsonCompletenessDefinition { Id = 7, EntityTypeId = "Product", GroupIds = new() { 10 } },
            },
            CompletenessGroups = new()
            {
                new JsonCompletenessGroup
                {
                    Id = 10, CompletenessDefinitionId = 7, Weight = 100, SortOrder = 0, RuleIds = new() { 100, 101 },
                    Name = new JsonLocaleString { StringMap = new() { ["en-US"] = "Marketing" } },
                },
            },
            CompletenessBusinessRules = new()
            {
                new JsonCompletenessBusinessRule
                {
                    Id = 100, Type = "FieldNotEmpty", Weight = 50, GroupIds = new() { 10 },
                    RuleSettings = new() { new JsonCompletenessRuleSetting { Id = 1, BusinessRuleId = 100, Key = "FieldTypeId", Value = "ProductName" } },
                },
                new JsonCompletenessBusinessRule
                {
                    Id = 101, Type = "FieldContainsValue", Weight = 50, GroupIds = new() { 10 },
                    RuleSettings = new()
                    {
                        new JsonCompletenessRuleSetting { Id = 2, BusinessRuleId = 101, Key = "FieldTypeId", Value = "ProductSku" },
                        new JsonCompletenessRuleSetting { Id = 3, BusinessRuleId = 101, Key = "Value", Value = "SKU-" },
                    },
                },
            },
        },
    };

    [Fact]
    public void Emits_group_class_and_field_attributes()
    {
        var outDir = Path.Combine(Path.GetTempPath(), "mm-comp-scaffold-" + Guid.NewGuid().ToString("N"));
        try
        {
            new ProjectScaffolder().Scaffold(ModelWithCompleteness(), outDir, "Acme.PimModel");
            var projectDir = Path.Combine(outDir, "Acme.PimModel");

            var groupPath = Path.Combine(projectDir, "CompletenessGroups", "Marketing.cs");
            File.Exists(groupPath).ShouldBeTrue("the Marketing completeness group class should be emitted");
            var group = File.ReadAllText(groupPath);
            group.ShouldContain("class Marketing : CompletenessGroup");
            group.ShouldContain("Weight => 100");

            var entity = File.ReadAllText(Path.Combine(projectDir, "EntityTypes", "Product.cs"));
            entity.ShouldContain("using ModelMeister.Model.Completeness;");
            entity.ShouldContain("using Acme.PimModel.CompletenessGroups;");
            entity.ShouldContain("FieldNotEmpty(50, typeof(Marketing))");
            entity.ShouldContain("ContainsValue(50, typeof(Marketing), \"SKU-\")");
        }
        finally
        {
            if (Directory.Exists(outDir)) Directory.Delete(outDir, recursive: true);
        }
    }

    [Fact]
    public void Model_without_completeness_emits_no_group_dir()
    {
        var outDir = Path.Combine(Path.GetTempPath(), "mm-comp-none-" + Guid.NewGuid().ToString("N"));
        try
        {
            var model = new InriverModelJson
            {
                EntityTypes = { new JsonEntityType { Id = "Product", FieldTypes = new() { new() { Id = "ProductName", DataType = "String", EntityTypeId = "Product" } } } },
            };
            new ProjectScaffolder().Scaffold(model, outDir, "Acme.PimModel");

            Directory.Exists(Path.Combine(outDir, "Acme.PimModel", "CompletenessGroups")).ShouldBeFalse();
            File.ReadAllText(Path.Combine(outDir, "Acme.PimModel", "EntityTypes", "Product.cs"))
                .ShouldNotContain("CompletenessGroups");
        }
        finally
        {
            if (Directory.Exists(outDir)) Directory.Delete(outDir, recursive: true);
        }
    }
}
