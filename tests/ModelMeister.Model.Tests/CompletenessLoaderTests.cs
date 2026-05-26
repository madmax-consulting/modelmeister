using Shouldly;
using ModelMeister.Model;
using ModelMeister.Model.Completeness;
using ModelMeister.Model.Loading;
using Xunit;

namespace ModelMeister.Model.Tests;

/// <summary>
/// The loader must reflect per-field <see cref="CompletenessRuleAttribute"/>s into a code-side
/// completeness model (one definition per entity type, rules bucketed into their group), resolving
/// kind-specific args (value, link-type id, operator/number) so the differ/scaffolder can round-trip them.
/// </summary>
public class CompletenessLoaderTests
{
    public sealed class CompMarketing : CompletenessGroup { public override int Weight => 100; }
    public sealed class CompQuality : CompletenessGroup { public override int Weight => 60; public override int SortOrder => 1; }

    public sealed class CompTarget : EntityType { }
    public sealed class CompLink : LinkType<CompProduct, CompTarget> { }

    public sealed class CompProduct : EntityType
    {
        [FieldNotEmpty(50, typeof(CompMarketing))]
        public Field<string> Title { get; init; } = new();

        [ContainsValue(50, typeof(CompMarketing), "SKU-")]
        public Field<string> Code { get; init; } = new();

        [NumberEvaluation(60, typeof(CompQuality), NumberEvaluationOperator.GreaterThan, 0)]
        public Field<double> Price { get; init; } = new();

        [LinkTypeExists(40, typeof(CompQuality), typeof(CompLink))]
        public Field<string> Dossier { get; init; } = new();
    }

    private static LoadedCompletenessDefinition Def() =>
        ModelLoader.LoadFromAssembly(typeof(CompProduct).Assembly)
            .CompletenessDefinitions.Single(d => d.EntityTypeId == "CompProduct");

    [Fact]
    public void Builds_one_definition_with_both_groups()
    {
        Def().Groups.Select(g => g.GroupClrType)
            .ShouldBe(new[] { typeof(CompMarketing), typeof(CompQuality) }, ignoreOrder: true);
    }

    [Fact]
    public void Group_carries_weight_and_sort_from_class()
    {
        var quality = Def().Groups.Single(g => g.GroupClrType == typeof(CompQuality));
        quality.Weight.ShouldBe(60);
        quality.SortOrder.ShouldBe(1);
    }

    [Fact]
    public void Marketing_rules_carry_kind_and_value()
    {
        var marketing = Def().Groups.Single(g => g.GroupClrType == typeof(CompMarketing));
        marketing.Rules.Select(r => r.Kind)
            .ShouldBe(new[] { CompletenessRuleKind.FieldNotEmpty, CompletenessRuleKind.ContainsValue }, ignoreOrder: true);
        marketing.Rules.Single(r => r.Kind == CompletenessRuleKind.ContainsValue).Value.ShouldBe("SKU-");
    }

    [Fact]
    public void Link_rule_resolves_link_type_id_and_field()
    {
        var rule = Def().Groups.SelectMany(g => g.Rules).Single(r => r.Kind == CompletenessRuleKind.LinkTypeExists);
        rule.LinkTypeId.ShouldBe("CompLink");
        rule.FieldId.ShouldBe("CompProductDossier");
    }

    [Fact]
    public void Number_rule_carries_operator_and_number()
    {
        var rule = Def().Groups.SelectMany(g => g.Rules).Single(r => r.Kind == CompletenessRuleKind.NumberEvaluation);
        rule.Operator.ShouldBe(NumberEvaluationOperator.GreaterThan);
        rule.Number.ShouldBe(0);
    }

    [Fact]
    public void Entity_types_without_rules_produce_no_definition()
    {
        ModelLoader.LoadFromAssembly(typeof(CompProduct).Assembly)
            .CompletenessDefinitions.ShouldNotContain(d => d.EntityTypeId == "CompTarget");
    }
}
