using Shouldly;
using ModelMeister.Model;
using ModelMeister.Model.Loading;
using ModelMeister.Model.Primitives;
using Xunit;

namespace ModelMeister.Model.Tests;

/// <summary>
/// Pin the new <see cref="DisplayNameAttribute"/> / <see cref="DisplayDescriptionAttribute"/>
/// surface. Either form (attribute or bool initialiser) must produce a field with the matching
/// flag set on <see cref="Field.IsDisplayName"/> / <see cref="Field.IsDisplayDescription"/>.
/// </summary>
public class DisplayAttributesTests
{
    public sealed class ProductAttr : EntityType
    {
        [DisplayName]
        public Field<LocaleString> Name { get; init; } = new();
        [DisplayDescription]
        public Field<LocaleString> Description { get; init; } = new();
        public Field<double> Price { get; init; } = new();
    }

    public sealed class ProductLegacy : EntityType
    {
        public Field<LocaleString> Name { get; init; } = new() { IsDisplayName = true };
        public Field<LocaleString> Description { get; init; } = new() { IsDisplayDescription = true };
    }

    [Fact]
    public void Attribute_form_sets_IsDisplayName()
    {
        var loaded = ModelLoader.LoadFromAssembly(typeof(ProductAttr).Assembly);
        var product = loaded.EntityTypes.Single(e => e.ClrType == typeof(ProductAttr));
        var name = product.Fields.Single(f => f.PropertyName == "Name");
        name.Field.IsDisplayName.ShouldBeTrue();
        var desc = product.Fields.Single(f => f.PropertyName == "Description");
        desc.Field.IsDisplayDescription.ShouldBeTrue();
    }

    [Fact]
    public void Bool_initialiser_form_still_works_for_back_compat()
    {
        var loaded = ModelLoader.LoadFromAssembly(typeof(ProductLegacy).Assembly);
        var product = loaded.EntityTypes.Single(e => e.ClrType == typeof(ProductLegacy));
        var name = product.Fields.Single(f => f.PropertyName == "Name");
        name.Field.IsDisplayName.ShouldBeTrue();
    }

    [Fact]
    public void Attribute_does_not_set_flag_on_unrelated_fields()
    {
        var loaded = ModelLoader.LoadFromAssembly(typeof(ProductAttr).Assembly);
        var product = loaded.EntityTypes.Single(e => e.ClrType == typeof(ProductAttr));
        var price = product.Fields.Single(f => f.PropertyName == "Price");
        price.Field.IsDisplayName.ShouldBeFalse();
        price.Field.IsDisplayDescription.ShouldBeFalse();
    }
}
