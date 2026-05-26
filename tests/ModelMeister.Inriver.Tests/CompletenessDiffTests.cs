using Shouldly;
using ModelMeister.Inriver.Diff;
using ModelMeister.Inriver.Snapshot;
using ModelMeister.Model.Completeness;
using ModelMeister.Model.Loading;
using ModelMeister.Model.Primitives;
using Xunit;

namespace ModelMeister.Inriver.Tests;

/// <summary>
/// Completeness diff/apply contract: a code-defined completeness model that matches what the applier
/// wrote (mirrored here as a live snapshot whose rule <c>Type</c>/settings come from
/// <see cref="CompletenessRuleVocabulary"/>) must produce zero changes — otherwise apply→diff loops.
/// Also pins Add / Update / Delete detection at the definition grain.
/// </summary>
public class CompletenessDiffTests
{
    [Fact]
    public void Matching_completeness_is_idempotent()
    {
        var code = CodeModel(weightSku: 50);
        var live = LiveSnapshot(weightSku: 50);

        var diff = ModelDiffer.Diff(code, live);
        diff.Of<AddCompletenessDefinition>().ShouldBeEmpty();
        diff.Of<UpdateCompletenessDefinition>().ShouldBeEmpty();
        diff.Of<DeleteCompletenessDefinition>().ShouldBeEmpty();
    }

    [Fact]
    public void Changed_rule_weight_produces_update()
    {
        var code = CodeModel(weightSku: 70);   // live has 50
        var live = LiveSnapshot(weightSku: 50);

        ModelDiffer.Diff(code, live).Of<UpdateCompletenessDefinition>()
            .ShouldHaveSingleItem()
            .LiveDefinitionId.ShouldBe(7);
    }

    [Fact]
    public void Definition_absent_on_live_produces_add()
    {
        var code = CodeModel(weightSku: 50);
        var live = new LiveModel { EnvironmentUrl = "t", CapturedUtc = DateTime.UtcNow };

        ModelDiffer.Diff(code, live).Of<AddCompletenessDefinition>()
            .ShouldHaveSingleItem()
            .Definition.EntityTypeId.ShouldBe("Product");
    }

    [Fact]
    public void Definition_only_on_live_is_deleted_only_when_policy_allows()
    {
        var code = new LoadedModel();
        var live = LiveSnapshot(weightSku: 50);

        ModelDiffer.Diff(code, live).Of<DeleteCompletenessDefinition>().ShouldBeEmpty();

        var policy = MergePolicy.Default with { AllowDeletes = true };
        ModelDiffer.Diff(code, live, policy).Of<DeleteCompletenessDefinition>()
            .ShouldHaveSingleItem()
            .LiveId.ShouldBe(7);
    }

    // ---------- fixtures ----------

    private static LoadedModel CodeModel(int weightSku) => new()
    {
        CompletenessDefinitions = new[]
        {
            new LoadedCompletenessDefinition
            {
                EntityTypeId = "Product",
                Groups = new[]
                {
                    new LoadedCompletenessGroupInstance
                    {
                        GroupClrType = typeof(object),
                        Name = new LocaleString("Marketing"),
                        Weight = 100,
                        SortOrder = 0,
                        Rules = new[]
                        {
                            new LoadedCompletenessRule
                            {
                                EntityTypeId = "Product", FieldId = "ProductName",
                                Kind = CompletenessRuleKind.FieldNotEmpty, Weight = 50,
                            },
                            new LoadedCompletenessRule
                            {
                                EntityTypeId = "Product", FieldId = "ProductSku",
                                Kind = CompletenessRuleKind.ContainsValue, Weight = weightSku, Value = "SKU-",
                            },
                        },
                    },
                },
            },
        },
    };

    private static LiveModel LiveSnapshot(int weightSku)
    {
        LiveCompletenessRuleSetting Setting(int ruleId, string key, string value) => new()
        {
            Id = 0, BusinessRuleId = ruleId, Type = "String", Key = key, Value = value,
        };

        return new LiveModel
        {
            EnvironmentUrl = "t",
            CapturedUtc = DateTime.UtcNow,
            CompletenessDefinitions = new[]
            {
                new LiveCompletenessDefinition
                {
                    Id = 7,
                    Name = new LocaleString("Product"),
                    EntityTypeId = "Product",
                    Groups = new[]
                    {
                        new LiveCompletenessGroup
                        {
                            Id = 10, Name = new LocaleString("Marketing"), Weight = 100, SortOrder = 0, DefinitionId = 7,
                            Rules = new[]
                            {
                                new LiveCompletenessBusinessRule
                                {
                                    Id = 100, Name = new LocaleString(""), Weight = 50, SortOrder = 0,
                                    Type = CompletenessRuleVocabulary.InriverType(CompletenessRuleKind.FieldNotEmpty),
                                    Settings = new[] { Setting(100, CompletenessRuleVocabulary.FieldTypeIdKey, "ProductName") },
                                },
                                new LiveCompletenessBusinessRule
                                {
                                    Id = 101, Name = new LocaleString(""), Weight = weightSku, SortOrder = 0,
                                    Type = CompletenessRuleVocabulary.InriverType(CompletenessRuleKind.ContainsValue),
                                    Settings = new[]
                                    {
                                        Setting(101, CompletenessRuleVocabulary.FieldTypeIdKey, "ProductSku"),
                                        Setting(101, CompletenessRuleVocabulary.ValueKey, "SKU-"),
                                    },
                                },
                            },
                        },
                    },
                },
            },
        };
    }
}
