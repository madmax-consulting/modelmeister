using ModelMeister.ExampleModel.EntityTypes;
using ModelMeister.Model;

namespace ModelMeister.ExampleModel.LinkTypes;

public sealed class PackagingProductMaterial : LinkType
{
    public override Type Source => typeof(PackagingProduct);
    public override Type Target => typeof(Material);
}
