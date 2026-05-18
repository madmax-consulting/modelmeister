using Shouldly;
using ModelMeister.Model.Expressions;
using ModelMeister.Model.Primitives;
using Xunit;

namespace ModelMeister.Model.Tests;

/// <summary>
/// Golden tests verifying the C# expression DSL renders to the inriver Expression Engine text shown
/// in docs/expression-engine.txt. The renderer is intentionally compact (no extra whitespace) so
/// each golden compares with the docs' canonicalised form.
/// </summary>
public class ExpressionRenderingTests
{
    [Fact]
    public void Operator_arithmetic_renders()
    {
        Expr<double> e = (Expr<double>)2.0 + 2.0;
        e.RenderTopLevel().ShouldBe("=(2+2)");
    }

    [Fact]
    public void Round_with_operator_arithmetic()
    {
        var e = Ex.Round(Ex.FieldValue<double>("PriceInUsd") * 0.85, 2);
        e.RenderTopLevel().ShouldBe("=ROUND((FIELDVALUE('PriceInUsd')*0.85), 2)");
    }

    [Fact]
    public void Abs_renders()
    {
        Ex.Abs(-16.0).RenderTopLevel().ShouldBe("=ABS(-16)");
    }

    [Fact]
    public void Convert_validates_units_and_renders()
    {
        Ex.Convert(100.0, "m", "in").RenderTopLevel().ShouldBe("=CONVERT(100, 'm', 'in')");
        Should.Throw<ArgumentException>(() => Ex.Convert(1.0, "bogus", "m"));
    }

    [Fact]
    public void Concatenate_implicit_string_lift()
    {
        var e = Ex.Concatenate("AB", "-", "CD");
        e.RenderTopLevel().ShouldBe("=CONCATENATE('AB', '-', 'CD')");
    }

    [Fact]
    public void If_function_renders()
    {
        var e = Ex.If((Expr<int>)2 + 2 > 0, Ex.S("Indeed!"), Ex.S("No way"));
        e.RenderTopLevel().ShouldBe("=IF(((2+2)>0), 'Indeed!', 'No way')");
    }

    [Fact]
    public void And_or_not_render()
    {
        Ex.And((Expr<int>)2 + 2 > 0, (Expr<int>)2 * 2 > 0).RenderTopLevel()
            .ShouldBe("=AND(((2+2)>0), ((2*2)>0))");
        Ex.Not(Ex.IsEmpty(Ex.FieldValue<string>("X"))).RenderTopLevel()
            .ShouldBe("=NOT(ISEMPTY(FIELDVALUE('X')))");
    }

    [Fact]
    public void FirstLinkedEntity_in_FieldValue()
    {
        var e = Ex.FieldValue<string>("ProductName", Ex.FirstLinkedEntity("ProductItem"));
        e.RenderTopLevel().ShouldBe("=FIELDVALUE('ProductName', FIRSTLINKEDENTITY('ProductItem'))");
    }

    [Fact]
    public void Map_introduces_VALUE_loop_variable()
    {
        var e = Ex.Map((Expr<List<int>>)new FunctionExpr<List<int>>("LIST", new LiteralExpr<int>(1), new LiteralExpr<int>(2), new LiteralExpr<int>(3)),
            v => v * 2);
        e.RenderTopLevel().ShouldBe("=MAP(LIST(1, 2, 3), ($VALUE*2))");
    }

    [Fact]
    public void Sum_with_projection_renders_VALUE()
    {
        var e = Ex.Sum<EntityRef>(Ex.LinkedEntities("MyLinkType"), v => Ex.FieldValue<double>("ADouble", v));
        e.RenderTopLevel().ShouldBe("=SUM(LINKEDENTITIES('MyLinkType'), FIELDVALUE('ADouble', $VALUE))");
    }

    [Fact]
    public void LsGenerate_introduces_LANG_loop_variable()
    {
        var e = Ex.LsGenerate(lang => Ex.LsConcatenate(Ex.LocaleStringValue("X", "en-US")));
        // We don't pin the full output (it depends on body); but $LANG should be referenceable inside body.
        e.RenderTopLevel().ShouldContain("LSGENERATE(");
        e.RenderTopLevel().ShouldContain("LSCONCATENATE(");
    }

    [Fact]
    public void Cvl_value_lookup()
    {
        Ex.CvlValue("Authors", "astrid_lindgren").RenderTopLevel()
            .ShouldBe("=CVLVALUE('Authors', 'astrid_lindgren')");
    }

    [Fact]
    public void String_with_apostrophe_is_escaped()
    {
        var e = (Expr<string>)"O'Brien";
        e.RenderTopLevel().ShouldBe("='O''Brien'");
    }

    [Fact]
    public void DateTime_literal_lowers_to_DATETIME_call()
    {
        Expr<DateTime> e = new DateTime(2026, 1, 1, 12, 30, 0);
        e.RenderTopLevel().ShouldBe("=DATETIME(2026, 1, 1, 12, 30, 0)");
    }

    [Fact]
    public void DateTimeAdd_with_unit_token()
    {
        var e = Ex.DateTimeAdd(Ex.DateTime(2026, 1, 1), DateTimeUnit.Days, 1);
        e.RenderTopLevel().ShouldBe("=DATETIMEADD(DATETIME(2026, 1, 1), 'D', 1)");
    }

    // -------- Strongly-typed selector / generic overloads --------

    public sealed class TypedProduct : EntityType
    {
        public Field<string> MimeType { get; init; } = new();
        public Field<LocaleString> Name { get; init; } = new();
    }

    public sealed class TypedColorsCvl : Cvl
    {
        public override IEnumerable<CvlValue> GetValues() => Array.Empty<CvlValue>();
    }

    public sealed class TypedAccessoriesCategory : Category { }

    public sealed class TypedProductHasResource : LinkType<TypedProduct, TypedProduct> { }

    [Fact]
    public void FieldValue_lambda_renders_same_as_string_id()
    {
        var lambda = Ex.FieldValue((TypedProduct r) => r.MimeType).RenderTopLevel();
        var stringy = Ex.FieldValue<string>("TypedProductMimeType").RenderTopLevel();
        lambda.ShouldBe(stringy);
        lambda.ShouldBe("=FIELDVALUE('TypedProductMimeType')");
    }

    [Fact]
    public void FieldCvlValue_lambda_renders_same_as_string_id()
    {
        var lambda = Ex.FieldCvlValue((TypedProduct r) => r.MimeType).RenderTopLevel();
        var stringy = Ex.FieldCvlValue("TypedProductMimeType").RenderTopLevel();
        lambda.ShouldBe(stringy);
    }

    [Fact]
    public void LocaleStringValue_lambda_renders_same_as_string_id()
    {
        var lambda = Ex.LocaleStringValue((TypedProduct r) => r.Name, "en").RenderTopLevel();
        var stringy = Ex.LocaleStringValue("TypedProductName", "en").RenderTopLevel();
        lambda.ShouldBe(stringy);
    }

    [Fact]
    public void LinkedEntities_generic_renders_same_as_string_id()
    {
        var generic = Ex.LinkedEntities<TypedProductHasResource>().RenderTopLevel();
        var stringy = Ex.LinkedEntities("TypedProductHasResource").RenderTopLevel();
        generic.ShouldBe(stringy);
        generic.ShouldBe("=LINKEDENTITIES('TypedProductHasResource')");
    }

    [Fact]
    public void FirstLinkedEntity_generic_renders_same_as_string_id()
    {
        var generic = Ex.FirstLinkedEntity<TypedProductHasResource>().RenderTopLevel();
        var stringy = Ex.FirstLinkedEntity("TypedProductHasResource").RenderTopLevel();
        generic.ShouldBe(stringy);
    }

    [Fact]
    public void CvlValue_generic_renders_same_as_string_id()
    {
        // `Cvl` strips a trailing "Cvl" suffix from the CLR type name to produce CvlId, so
        // TypedColorsCvl -> "TypedColors" in the rendered text.
        var generic = Ex.CvlValue<TypedColorsCvl>("Red").RenderTopLevel();
        var stringy = Ex.CvlValue("TypedColors", "Red").RenderTopLevel();
        generic.ShouldBe(stringy);
        generic.ShouldBe("=CVLVALUE('TypedColors', 'Red')");
    }

    [Fact]
    public void FieldValues_generic_renders_same_as_string_id()
    {
        // `Category` strips a trailing "Category" suffix, so TypedAccessoriesCategory -> "TypedAccessories".
        var generic = Ex.FieldValues<TypedAccessoriesCategory>(currentFieldSetOnly: true).RenderTopLevel();
        var stringy = Ex.FieldValues(true, "TypedAccessories").RenderTopLevel();
        generic.ShouldBe(stringy);
    }

    [Fact]
    public void FieldValue_lambda_inside_closure_uses_inner_entity_var()
    {
        var e = Ex.Any<EntityRef>(
            Ex.LinkedEntities<TypedProductHasResource>(),
            v => Ex.RegexTest(Ex.FieldValue((TypedProduct p) => p.MimeType, v), "^image"));
        e.RenderTopLevel().ShouldContain("FIELDVALUE('TypedProductMimeType'");
    }
}
