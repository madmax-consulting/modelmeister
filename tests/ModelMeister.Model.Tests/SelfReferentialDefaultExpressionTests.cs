using ModelMeister.Model.Expressions;
using ModelMeister.Model.Loading;
using ModelMeister.Model.Primitives;
using Shouldly;
using Xunit;

namespace ModelMeister.Model.Tests;

/// <summary>
/// Regression: scaffolded entity types frequently emit a <c>DefaultExpression</c> that references
/// other fields on the SAME entity via <c>Ex.FieldValue((TEntity r) =&gt; r.X)</c>. The selector
/// overload used to do <c>new TEntity().EntityTypeId</c>, which re-runs the entity's property
/// initializers — and those property initializers themselves call into the selector overload,
/// recursing forever. The fix uses <c>typeof(TEntity).Name</c> so no instance is constructed.
/// </summary>
public class SelfReferentialDefaultExpressionTests
{
    private sealed class SelfRefProduct : EntityType
    {
        public Field<string> S4MaterialNumber { get; init; } = new();
        public Field<string> VariantId { get; init; } = new();

        public Field<string> MaterialNumber { get; init; } = new()
        {
            ReadOnly = true,
            SupportsExpression = true,
            DefaultExpression = Ex.Concatenate(
                Ex.FieldValue((SelfRefProduct r) => r.S4MaterialNumber),
                Ex.FieldValue((SelfRefProduct r) => r.VariantId)),
        };
    }

    [Fact]
    public void Self_referential_default_expression_does_not_stack_overflow()
    {
        // The act of constructing SelfRefProduct triggered the recursion before the fix.
        var p = new SelfRefProduct();
        p.MaterialNumber.DefaultExpression.ShouldNotBeNull();
    }

    [Fact]
    public void Self_referential_default_expression_renders_with_correct_field_ids()
    {
        var p = new SelfRefProduct();
        var rendered = p.MaterialNumber.DefaultExpression!.RenderTopLevel();
        rendered.ShouldBe("=CONCATENATE(FIELDVALUE('SelfRefProductS4MaterialNumber'), FIELDVALUE('SelfRefProductVariantId'))");
    }
}
