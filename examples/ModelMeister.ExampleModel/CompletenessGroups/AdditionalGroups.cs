using ModelMeister.Model.Completeness;
using ModelMeister.Model.Primitives;

namespace ModelMeister.ExampleModel.CompletenessGroups;

/// <summary>
/// Second completeness group on PackagingProduct. Different <see cref="CompletenessGroup.Weight"/>
/// and <see cref="CompletenessGroup.SortOrder"/> than Marketing to exercise both properties.
/// </summary>
public sealed class Quality : CompletenessGroup
{
    public Quality()
    {
        Name = new LocaleString("Quality")
            .With("en-US", "Quality")
            .With("sv-SE", "Kvalitet");
    }

    public override int Weight => 60;
    public override int SortOrder => 1;
}

public sealed class Logistics : CompletenessGroup
{
    public override int Weight => 40;
    public override int SortOrder => 2;
}
