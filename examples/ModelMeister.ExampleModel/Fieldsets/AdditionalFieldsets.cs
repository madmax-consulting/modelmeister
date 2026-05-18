using ModelMeister.ExampleModel.EntityTypes;
using ModelMeister.Model;
using ModelMeister.Model.Primitives;

namespace ModelMeister.ExampleModel.Fieldsets;

/// <summary>Second fieldset for <see cref="PackagingProduct"/> so a field can belong to two fieldsets.</summary>
public sealed class LogisticsFieldset : Fieldset
{
    public LogisticsFieldset()
    {
        Description = new LocaleString("Warehouse / shipping subset");
    }

    public override Type EntityType => typeof(PackagingProduct);
    public override int Index => 20;
}

public sealed class ChemistryFieldset : Fieldset
{
    public override Type EntityType => typeof(Material);
    public override int Index => 10;
}
