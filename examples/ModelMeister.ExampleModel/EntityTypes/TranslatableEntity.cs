using ModelMeister.Model;
using ModelMeister.Model.Primitives;

namespace ModelMeister.ExampleModel.EntityTypes;

/// <summary>
/// Abstract base. Not registered as an inriver entity type — it only contributes shared fields
/// to concrete subclasses. The loader stamps the concrete entity's ID prefix on each inherited
/// field, so <c>PackagingProduct.Name</c> becomes <c>PackagingProductName</c>.
/// </summary>
public abstract class TranslatableEntity : EntityType
{
    [DisplayName]
    public Field<LocaleString> Name { get; init; } = new() { Mandatory = true };

    [DisplayDescription]
    public Field<LocaleString> Description { get; init; } = new();
}
