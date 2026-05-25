using Shouldly;
using ModelMeister.Model;
using ModelMeister.Model.Loading;
using ModelMeister.Model.Primitives;
using Xunit;

namespace ModelMeister.Model.Tests;

/// <summary>
/// <see cref="NameHumanizer"/> splits PascalCase/camelCase identifiers into display names, and the
/// loader uses it to default field/entity display names when none is set explicitly.
/// </summary>
public class NameHumanizerTests
{
    [Theory]
    [InlineData("ProductList", "Product List")]
    [InlineData("WeightGrams", "Weight Grams")]
    [InlineData("Density", "Density")]
    [InlineData("CvlId", "Cvl Id")]
    [InlineData("XMLField", "XML Field")]
    [InlineData("Weight2", "Weight 2")]
    [InlineData("ProductID", "Product ID")]
    [InlineData("product_list", "product list")]
    [InlineData("", "")]
    public void Humanize_splits_on_word_boundaries(string input, string expected) =>
        NameHumanizer.Humanize(input).ShouldBe(expected);

    public sealed class HumanizedFields : EntityType
    {
        public Field<string> ProductList { get; init; } = new();
        public Field<string> WeightGrams { get; init; } = new() { Name = new LocaleString("Explicit Label") };
    }

    private static LoadedField Load(string prop) =>
        ModelLoader.LoadFromAssembly(typeof(HumanizedFields).Assembly)
            .EntityTypes.Single(e => e.ClrType == typeof(HumanizedFields))
            .Fields.Single(f => f.PropertyName == prop);

    [Fact]
    public void Field_without_name_gets_humanized_property_name() =>
        Load("ProductList").Name.DefaultValue.ShouldBe("Product List");

    [Fact]
    public void Explicit_field_name_is_preserved() =>
        Load("WeightGrams").Name.DefaultValue.ShouldBe("Explicit Label");

    [Fact]
    public void Entity_type_name_defaults_to_humanized_class_name() =>
        ModelLoader.LoadFromAssembly(typeof(HumanizedFields).Assembly)
            .EntityTypes.Single(e => e.ClrType == typeof(HumanizedFields))
            .Name.DefaultValue.ShouldBe("Humanized Fields");
}
