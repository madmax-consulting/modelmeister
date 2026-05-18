using Shouldly;
using ModelMeister.Model;
using ModelMeister.Model.Loading;
using Xunit;

namespace ModelMeister.Model.Tests;

public class InheritanceTests
{
    // ---- Test fixture types: defined here so the test is self-contained and doesn't depend on the example model ----

    public abstract class Translatable : EntityType
    {
        public Field<string> Name { get; init; } = new() { IsDisplayName = true };
    }

    public sealed class Product : Translatable
    {
        public Field<double> Price { get; init; } = new();
    }

    public sealed class Item : Translatable
    {
        public Field<int> Quantity { get; init; } = new();
    }

    public abstract class MarketAwareTranslatable : Translatable
    {
        public Field<bool> Available { get; init; } = new();
    }

    public sealed class Promo : MarketAwareTranslatable
    {
        public Field<double> DiscountPct { get; init; } = new();
    }

    [Fact]
    public void Abstract_base_classes_are_not_registered_as_entity_types()
    {
        var model = ModelLoader.LoadFromAssembly(typeof(InheritanceTests).Assembly);

        var ids = model.EntityTypes.Select(e => e.EntityTypeId).ToHashSet();

        ids.ShouldContain("Product");
        ids.ShouldContain("Item");
        ids.ShouldContain("Promo");
        ids.ShouldNotContain("Translatable");
        ids.ShouldNotContain("MarketAwareTranslatable");
    }

    [Fact]
    public void Inherited_fields_get_concrete_entitytype_prefix()
    {
        var model = ModelLoader.LoadFromAssembly(typeof(InheritanceTests).Assembly);

        var product = model.EntityTypes.Single(e => e.EntityTypeId == "Product");
        var item = model.EntityTypes.Single(e => e.EntityTypeId == "Item");

        product.Fields.Select(f => f.Id).ShouldContain("ProductName");
        product.Fields.Select(f => f.Id).ShouldContain("ProductPrice");

        item.Fields.Select(f => f.Id).ShouldContain("ItemName");
        item.Fields.Select(f => f.Id).ShouldContain("ItemQuantity");

        // Critical: same property name → distinct field IDs per concrete entity
        item.Fields.Select(f => f.Id).ShouldNotContain("ProductName");
        product.Fields.Select(f => f.Id).ShouldNotContain("ItemName");
    }

    [Fact]
    public void Multi_level_inheritance_uses_deepest_concrete_prefix()
    {
        var model = ModelLoader.LoadFromAssembly(typeof(InheritanceTests).Assembly);
        var promo = model.EntityTypes.Single(e => e.EntityTypeId == "Promo");

        // Inherited two levels deep (Translatable.Name → MarketAwareTranslatable.Available → Promo)
        promo.Fields.Select(f => f.Id).ShouldContain("PromoName");
        promo.Fields.Select(f => f.Id).ShouldContain("PromoAvailable");
        promo.Fields.Select(f => f.Id).ShouldContain("PromoDiscountPct");
    }

    [Fact]
    public void Inherited_fields_are_distinct_instances_per_concrete_entity()
    {
        var model = ModelLoader.LoadFromAssembly(typeof(InheritanceTests).Assembly);

        var productName = model.EntityTypes.Single(e => e.EntityTypeId == "Product")
            .Fields.Single(f => f.PropertyName == "Name").Field;
        var itemName = model.EntityTypes.Single(e => e.EntityTypeId == "Item")
            .Fields.Single(f => f.PropertyName == "Name").Field;

        productName.ShouldNotBeSameAs(itemName);
        productName.Id.ShouldBe("ProductName");
        itemName.Id.ShouldBe("ItemName");
    }
}
