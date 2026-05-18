using ModelMeister.ExampleModel.Categories;
using ModelMeister.ExampleModel.EntityTypes;
using ModelMeister.Model;
using ModelMeister.Model.Primitives;

namespace ModelMeister.ExampleModel.SpecificationTemplates;

/// <summary>
/// Spec template bound to <see cref="ProductSpec"/>. ProductSpec intentionally carries no completeness
/// rules and no parent-child CVL bindings — that keeps the template MM070/MM071-clean.
/// </summary>
public sealed class PackagingProductSpec : SpecificationTemplate
{
    public PackagingProductSpec()
    {
        Name = new LocaleString("Packaging spec sheet");
        Description = new LocaleString("Per-SKU spec sheet bound to the ProductSpec entity");
    }

    public override IReadOnlyList<Type> Categories => new[]
    {
        typeof(MarketingCategory),
        typeof(LegalCategory),
    };

    public override IReadOnlyList<Type> EntityTypes => new[]
    {
        typeof(ProductSpec),
    };
}
