using ModelMeister.Model;
using ModelMeister.Model.Primitives;

namespace ModelMeister.ExampleModel.Cvls;

/// <summary>Double-typed CVL — exercises <see cref="CvlDataType.Double"/>.</summary>
public sealed class TaxRatesCvl : Cvl
{
    public override CvlDataType DataType => CvlDataType.Double;

    public override IEnumerable<CvlValue> GetValues() => new[]
    {
        new CvlValue("0",    new LocaleString("Exempt"),       Index: 0),
        new CvlValue("0.06", new LocaleString("Reduced 6%"),   Index: 1),
        new CvlValue("0.12", new LocaleString("Reduced 12%"),  Index: 2),
        new CvlValue("0.25", new LocaleString("Standard 25%"), Index: 3),
    };
}
