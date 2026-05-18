using ModelMeister.ExampleModel.Cvls;
using ModelMeister.ExampleModel.Fieldsets;
using ModelMeister.Model;
using ModelMeister.Model.Primitives;

namespace ModelMeister.ExampleModel.EntityTypes;

/// <summary>
/// Second concrete that inherits Name/Description from TranslatableEntity — proves field IDs
/// get the concrete prefix (MaterialName, MaterialDescription), distinct from PackagingProductName.
/// </summary>
public sealed class Material : TranslatableEntity
{
    public Field<string, MaterialFamilyCvl> Family { get; init; } = new()
    {
        Fieldset = typeof(ChemistryFieldset),
    };

    public Field<double> WeightGrams { get; init; } = new()
    {
        Mandatory = true,
        Fieldset = typeof(ChemistryFieldset),
    };

    public Field<double> Density { get; init; } = new()
    {
        Fieldset = typeof(ChemistryFieldset),
    };
}
