using ModelMeister.ExampleModel.EntityTypes;
using ModelMeister.Model;
using ModelMeister.Model.Primitives;

namespace ModelMeister.ExampleModel.LinkTypes;

/// <summary>
/// Link type with a link-entity attached. Exercises <see cref="LinkType.LinkEntityType"/>,
/// <see cref="LinkType.SourceName"/>, <see cref="LinkType.TargetName"/>, <see cref="LinkType.Settings"/>,
/// and a non-zero <see cref="LinkType.Index"/>.
/// </summary>
public sealed class SupplierMaterial : LinkType
{
    public SupplierMaterial()
    {
        SourceName = new LocaleString("supplies");
        TargetName = new LocaleString("supplied by");
        Settings["SyndicatedAs"] = "vendor-link";
    }

    public override Type Source => typeof(Supplier);
    public override Type Target => typeof(Material);
    public override Type? LinkEntityType => typeof(MaterialAttribute);
    public override int Index => 10;
}
