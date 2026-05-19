using System.Xml.Linq;
using ModelMeister.Model;
using ModelMeister.Model.Categories;
using ModelMeister.Model.Primitives;

namespace ModelMeister.ExampleModel.EntityTypes;

/// <summary>
/// Asset/binary entity. Exercises the data types that don't appear elsewhere — File, Xml, DateTime,
/// Integer, plus the reserved <see cref="FileInformation"/> category, plus DateTimeOffset / decimal /
/// float / long / CvlKey coercions.
/// </summary>
public sealed class Resource : EntityType
{
    // File and Xml are not expression-eligible, so no DefaultExpression on these.
    [Mandatory, FieldCategory(typeof(FileInformation))]
    public Field<FileRef> Asset { get; init; } = new();

    [FieldCategory(typeof(FileInformation))]
    public Field<XElement> ExifXml { get; init; } = new();

    [FieldCategory(typeof(FileInformation))]
    public Field<DateTime> CapturedAt { get; init; } = new();

    /// <summary>DateTimeOffset coerces to Datatype.DateTime — same wire shape as DateTime.</summary>
    [FieldCategory(typeof(FileInformation))]
    public Field<DateTimeOffset> UploadedAt { get; init; } = new();

    public Field<int> WidthPx { get; init; } = new();

    public Field<int> HeightPx { get; init; } = new();

    /// <summary>long → Datatype.Integer (16-bit/32-bit irrelevant on the wire).</summary>
    public Field<long> ByteSize { get; init; } = new();

    /// <summary>decimal → Datatype.Double.</summary>
    public Field<decimal> AspectRatio { get; init; } = new();

    /// <summary>float → Datatype.Double.</summary>
    public Field<float> CompressionQuality { get; init; } = new();

    /// <summary>Multi-value strings — exercises <see cref="Field.MultiValue"/>.</summary>
    [MultiValue]
    public Field<string> Tags { get; init; } = new();

    /// <summary>Field with explicit DefaultValue — distinct from DefaultExpression.</summary>
    public Field<bool> IsPublished { get; init; } = new() { DefaultValue = false };

    /// <summary>CvlKey TData with no TCvl is the legacy stringly-typed CVL bind. Provided for parity.</summary>
    public Field<CvlKey> LegacyMaterialKey { get; init; } = new() { Cvl = typeof(Cvls.MaterialFamilyCvl) };
}
