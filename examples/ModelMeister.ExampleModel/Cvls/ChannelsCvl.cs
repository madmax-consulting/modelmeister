using ModelMeister.ExampleModel.EntityTypes;
using ModelMeister.Model;
using ModelMeister.Model.Primitives;

namespace ModelMeister.ExampleModel.Cvls;

/// <summary>
/// Entity-backed CVL — values come from instances of the bound entity type, not from a static list.
/// Exercises <see cref="Cvl.EntityType"/>.
/// </summary>
public sealed class ChannelsCvl : Cvl
{
    public override CvlDataType DataType => CvlDataType.String;
    public override Type? EntityType => typeof(Channel);

    // Entity-backed CVLs source values live in inriver; the seed list is empty.
    public override IEnumerable<CvlValue> GetValues() => Array.Empty<CvlValue>();
}
