using Shouldly;
using ModelMeister.Scaffolder;
using Xunit;

namespace ModelMeister.Scaffolder.Tests;

public class ExpressionParserTests
{
    [Fact]
    public void Concatenate_with_FieldValue_emits_typed_call()
    {
        var (cs, warns) = ExpressionParser.ParseTopLevel(
            "=CONCATENATE(\"Dimensions (LxWxH): \", FIELDVALUE(\"Length\"), \" x \", FIELDVALUE(\"Width\"), \" x \", FIELDVALUE(\"Height\"), \" cm\")");
        cs.ShouldContain("Ex.Concatenate(");
        cs.ShouldContain("Ex.FieldValue<string>");
        cs.ShouldContain("Length");
        warns.ShouldBeEmpty();
    }

    [Fact]
    public void Arithmetic_inside_Concatenate_emits_operators()
    {
        var (cs, _) = ExpressionParser.ParseTopLevel(
            "=CONCATENATE(FIELDVALUE(\"Length\") * FIELDVALUE(\"Width\") * FIELDVALUE(\"Height\"), \" cm3\")");
        cs.ShouldContain("Ex.Concatenate(");
        cs.ShouldContain("*");
    }

    [Fact]
    public void Ifs_with_And_and_FieldValue_emits_typed_pairs()
    {
        var (cs, _) = ExpressionParser.ParseTopLevel(
            "=IFS( AND(FIELDVALUE(\"ProductType\")=\"Shoes\", FIELDVALUE(\"Gender\")=\"Men\"), \"Men's Footwear\", AND(FIELDVALUE(\"ProductType\")=\"Shoes\", FIELDVALUE(\"Gender\")=\"Women\"), \"Women's Footwear\", FIELDVALUE(\"ProductType\")=\"Bag\", \"Accessories\", TRUE, \"Other\" )");
        cs.ShouldContain("Ex.Ifs(");
        cs.ShouldContain("Ex.And(");
        cs.ShouldContain("Ex.Eq(");
    }

    [Fact]
    public void Unknown_function_falls_back_to_raw()
    {
        var (cs, warns) = ExpressionParser.ParseTopLevel("=FUTUREFUNC(\"x\", 1, 2)");
        cs.ShouldContain("Ex.Raw");
        warns.ShouldNotBeEmpty();
    }

    [Fact]
    public void Single_quoted_strings_with_escapes_parse()
    {
        var (cs, _) = ExpressionParser.ParseTopLevel("=CONCATENATE('it''s ok', 'fine')");
        cs.ShouldContain("Ex.Concatenate(");
    }

    [Fact]
    public void Pi_emits_property_not_call()
    {
        var (cs, _) = ExpressionParser.ParseTopLevel("=PI()");
        cs.ShouldBe("Ex.Pi");
    }

    [Fact]
    public void RegexTest_inside_If_emits_typed_bool_condition()
    {
        // Regression: REGEXTEST used to fall through to Ex.Raw<string>, which fails to type-check
        // as the bool condition of Ex.If. The typed Ex.RegexTest returns Expr<bool>, and its
        // pattern arg is a bare `string` (not Expr<string>), so it must not be wrapped in Ex.S.
        var (cs, warns) = ExpressionParser.ParseTopLevel(
            "=IF(REGEXTEST(FIELDVALUE(\"MimeType\"), \"^image\"), CONCATENATE(FIELDVALUE(\"ImageUrl\"), \"-S300x\"), \"\")");
        cs.ShouldContain("Ex.If(Ex.RegexTest(");
        cs.ShouldContain("\"^image\"");
        cs.ShouldNotContain("Ex.S(\"^image\")");
        cs.ShouldNotContain("Ex.Raw");
        warns.ShouldBeEmpty();
    }

    [Fact]
    public void RegexReplace_emits_typed_call_with_bare_string_args()
    {
        var (cs, warns) = ExpressionParser.ParseTopLevel(
            "=REGEXREPLACE(FIELDVALUE(\"Name\"), \"\\\\s+\", \"_\")");
        cs.ShouldContain("Ex.RegexReplace(");
        cs.ShouldNotContain("Ex.S(");
        cs.ShouldNotContain("Ex.Raw");
        warns.ShouldBeEmpty();
    }

    [Fact]
    public void FieldValue_id_arg_is_string_literal_not_ExS()
    {
        // Regression: previously the parser wrapped field-id args in Ex.S(...), which doesn't
        // compile because Ex.FieldValue<T> takes `string`, not `Expr<string>`.
        var (cs, _) = ExpressionParser.ParseTopLevel("=FIELDVALUE(\"X\")");
        cs.ShouldNotContain("Ex.S(");
        cs.ShouldBe("Ex.FieldValue<string>(\"X\")");
    }

    [Fact]
    public void Context_emits_typed_selector_for_known_field_ids()
    {
        var ctx = new ExpressionContext(
            fields: new Dictionary<string, FieldRef>(StringComparer.OrdinalIgnoreCase)
            {
                ["ProductS4MaterialNumber"] = new FieldRef("Product", "S4MaterialNumber"),
            },
            linkTypes: new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase),
            cvls: new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase));

        var (cs, _) = ExpressionParser.ParseTopLevel("=FIELDVALUE(\"ProductS4MaterialNumber\")", ctx);
        cs.ShouldBe("Ex.FieldValue((Product r) => r.S4MaterialNumber)");
        cs.ShouldNotContain("nameof(");
    }

    [Fact]
    public void Context_falls_back_to_string_for_unknown_field_ids()
    {
        var ctx = new ExpressionContext(
            fields: new Dictionary<string, FieldRef>(StringComparer.OrdinalIgnoreCase),
            linkTypes: new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase),
            cvls: new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase));

        var (cs, _) = ExpressionParser.ParseTopLevel("=FIELDVALUE(\"Unknown\")", ctx);
        cs.ShouldBe("Ex.FieldValue<string>(\"Unknown\")");
    }

    [Fact]
    public void Context_emits_generics_for_known_link_types_and_cvls()
    {
        var ctx = new ExpressionContext(
            fields: new Dictionary<string, FieldRef>(StringComparer.OrdinalIgnoreCase),
            linkTypes: new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["ProductMaterial"] = "ProductMaterial",
            },
            cvls: new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["Colors"] = "ColorsCvl",
            });

        var (linked, _) = ExpressionParser.ParseTopLevel("=LINKEDENTITIES(\"ProductMaterial\")", ctx);
        linked.ShouldBe("Ex.LinkedEntities<ProductMaterial>()");
        linked.ShouldNotContain("nameof(");

        var (first, _) = ExpressionParser.ParseTopLevel("=FIRSTLINKEDENTITY(\"ProductMaterial\")", ctx);
        first.ShouldBe("Ex.FirstLinkedEntity<ProductMaterial>()");

        var (cvl, _) = ExpressionParser.ParseTopLevel("=CVLVALUE(\"Colors\", \"Red\")", ctx);
        cvl.ShouldBe("Ex.CvlValue<ColorsCvl>(\"Red\")");
        cvl.ShouldNotContain("nameof(");
    }

    [Fact]
    public void Context_falls_back_to_string_for_unknown_link_types_and_cvls()
    {
        var ctx = ExpressionContext.Empty;

        var (linked, _) = ExpressionParser.ParseTopLevel("=LINKEDENTITIES(\"X\")", ctx);
        linked.ShouldBe("Ex.LinkedEntities(\"X\")");

        var (cvl, _) = ExpressionParser.ParseTopLevel("=CVLVALUE(\"X\", \"k\")", ctx);
        cvl.ShouldBe("Ex.CvlValue(\"X\", \"k\")");
    }

    [Fact]
    public void Char_emits_typed_call()
    {
        var (cs, warns) = ExpressionParser.ParseTopLevel("=CHAR(10)");
        cs.ShouldBe("Ex.Char(10)");
        warns.ShouldBeEmpty();
    }

    [Fact]
    public void LocaleStringValue_emits_typed_call_without_context()
    {
        var (cs, warns) = ExpressionParser.ParseTopLevel("=LOCALESTRINGVALUE(\"ProductDescription\", \"en\")");
        cs.ShouldBe("Ex.LocaleStringValue(\"ProductDescription\", \"en\")");
        warns.ShouldBeEmpty();
    }

    [Fact]
    public void LocaleStringValue_emits_typed_selector_when_field_known()
    {
        var ctx = new ExpressionContext(
            fields: new Dictionary<string, FieldRef>(StringComparer.OrdinalIgnoreCase)
            {
                ["ProductDescription"] = new FieldRef("Product", "Description"),
            },
            linkTypes: new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase),
            cvls: new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase));
        var (cs, warns) = ExpressionParser.ParseTopLevel("=LOCALESTRINGVALUE(\"ProductDescription\", \"en\")", ctx);
        cs.ShouldBe("Ex.LocaleStringValue((Product r) => r.Description, \"en\")");
        warns.ShouldBeEmpty();
    }

    [Fact]
    public void TextJoin_emits_typed_call_with_bool_literal()
    {
        var (cs, warns) = ExpressionParser.ParseTopLevel("=TEXTJOIN(\", \", TRUE, FIELDVALUE(\"A\"), FIELDVALUE(\"B\"))");
        cs.ShouldContain("Ex.TextJoin(\", \", true, ");
        cs.ShouldContain("Ex.FieldValue<string>(\"A\")");
        cs.ShouldNotContain("Ex.Raw");
        warns.ShouldBeEmpty();
    }
}
