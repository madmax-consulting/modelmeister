using ModelMeister.Model;
using ModelMeister.Model.Primitives;

namespace ModelMeister.ExampleModel.Cvls;

/// <summary>DateTime-typed CVL — exercises <see cref="CvlDataType.DateTime"/>.</summary>
public sealed class ReleaseDatesCvl : Cvl
{
    public override CvlDataType DataType => CvlDataType.DateTime;

    public override IEnumerable<CvlValue> GetValues() => new[]
    {
        new CvlValue("2025-Q1", new LocaleString("Q1 2025"), Index: 0),
        new CvlValue("2025-Q2", new LocaleString("Q2 2025"), Index: 1),
        new CvlValue("2025-Q3", new LocaleString("Q3 2025"), Index: 2),
        new CvlValue("2025-Q4", new LocaleString("Q4 2025"), Index: 3),
    };
}
