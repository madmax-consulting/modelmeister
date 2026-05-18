using Shouldly;
using ModelMeister.Model.Loading;
using Xunit;

namespace ModelMeister.Model.Tests;

public class GenericLinkTypeTests
{
    public sealed class GenericLinkNode : EntityType { }
    public sealed class GenericLinkLeaf : EntityType { }

    public sealed class GenericLinkNodeGenericLinkLeaf : LinkType<GenericLinkNode, GenericLinkLeaf> { }

    [Fact]
    public void Loader_resolves_source_and_target_from_generic_base()
    {
        var model = ModelLoader.LoadFromAssembly(typeof(GenericLinkTypeTests).Assembly);
        var l = model.LinkTypes.Single(lt => lt.ClrType == typeof(GenericLinkNodeGenericLinkLeaf));

        l.SourceEntityTypeId.ShouldBe("GenericLinkNode");
        l.TargetEntityTypeId.ShouldBe("GenericLinkLeaf");
        l.LinkTypeId.ShouldBe("GenericLinkNodeGenericLinkLeaf");
    }

    [Fact]
    public void Open_generic_base_is_not_registered()
    {
        var model = ModelLoader.LoadFromAssembly(typeof(GenericLinkTypeTests).Assembly);
        model.LinkTypes.ShouldNotContain(l => l.ClrType.IsGenericTypeDefinition);
    }
}
