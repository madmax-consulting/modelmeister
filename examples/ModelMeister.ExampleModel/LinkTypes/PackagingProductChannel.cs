using ModelMeister.ExampleModel.EntityTypes;
using ModelMeister.Model;

namespace ModelMeister.ExampleModel.LinkTypes;

/// <summary>
/// Plain link, no link-entity — second link off PackagingProduct so expression refs that count link
/// targets per type have something distinct to point at.
/// </summary>
public sealed class PackagingProductChannel : LinkType
{
    public override Type Source => typeof(PackagingProduct);
    public override Type Target => typeof(Channel);
    public override int Index => 5;
}
