using ModelMeister.Model;
using ModelMeister.Model.Primitives;

namespace ModelMeister.ExampleModel.EntityTypes;

/// <summary>
/// Link entity type (an entity whose instances live on a link, not on either side). Demonstrates
/// <see cref="EntityType.IsLinkEntityType"/> + an entity-type-level <see cref="EntityType.Settings"/>
/// dictionary that round-trips through Remoting.
/// </summary>
public sealed class MaterialAttribute : EntityType
{
    public MaterialAttribute()
    {
        IsLinkEntityType = true;
        EntityTypeDescription = new LocaleString("Per-link material attributes (purity %, source plant)");
        Settings["LinkRole"] = "Supplier";
    }

    public Field<double> PurityPercent { get; init; } = new() { Mandatory = true };
    public Field<string> SourcePlant { get; init; } = new();
}
