using ModelMeister.Model;

namespace ModelMeister.ExampleModel.Categories;

/// <summary>Demonstrates <see cref="Category.OrderByName"/>.</summary>
public sealed class MarketingCategory : Category
{
    public override int Index => 30;
    public override bool OrderByName => true;
}

public sealed class LegalCategory : Category
{
    public override int Index => 40;
}
