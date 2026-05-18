using ModelMeister.ExampleModel.EntityTypes;
using ModelMeister.Model;

namespace ModelMeister.ExampleModel.Fieldsets;

public sealed class TechnicalFieldset : Fieldset
{
    public override Type EntityType => typeof(PackagingProduct);
    public override int Index => 10;
}
