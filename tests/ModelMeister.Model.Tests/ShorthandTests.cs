using Shouldly;
using ModelMeister.Model;
using ModelMeister.Model.Primitives;
using Xunit;

namespace ModelMeister.Model.Tests;

public class ShorthandTests
{
    public sealed class ShortProduct : EntityType
    {
        public Field<LocaleString> Name { get; init; } = new();
        public Field<double> Price { get; init; } = new();
    }

    [Fact]
    public void Mm_Field_resolves_to_entity_id_plus_property_name()
    {
        Mm.Field<ShortProduct>(p => p.Name).ShouldBe("ShortProductName");
        Mm.Field<ShortProduct>(p => p.Price).ShouldBe("ShortProductPrice");
    }

    [Fact]
    public void Mm_FieldOn_resolves_with_string_property_name()
    {
        Mm.FieldOn<ShortProduct>("Name").ShouldBe("ShortProductName");
    }

    [Fact]
    public void Mm_Loc_with_no_entries_returns_empty_locale_string()
    {
        var ls = Mm.Loc();
        ls.DefaultValue.ShouldBe(string.Empty);
    }

    [Fact]
    public void Mm_Loc_builds_a_locale_string_with_provided_entries()
    {
        var ls = Mm.Loc(("en", "Brand"), ("sv", "Varumärke"));
        ls.For("en").ShouldBe("Brand");
        ls.For("sv").ShouldBe("Varumärke");
    }

    [Fact]
    public void Field_extensions_set_init_only_properties()
    {
        var field = new Field<double>().Required().UniqueValue().Multi().At(7);
        field.Mandatory.ShouldBeTrue();
        field.Unique.ShouldBeTrue();
        field.MultiValue.ShouldBeTrue();
        field.Index.ShouldBe(7);
    }
}
