using ModelMeister.Model.Completeness;

namespace ModelMeister.ExampleModel.CompletenessGroups;

public sealed class Marketing : CompletenessGroup
{
    public override int Weight => 100;
    public override int SortOrder => 0;
}
