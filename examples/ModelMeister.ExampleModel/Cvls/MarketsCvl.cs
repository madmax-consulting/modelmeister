using ModelMeister.Model;
using ModelMeister.Model.Markets;
using ModelMeister.Model.Primitives;

namespace ModelMeister.ExampleModel.Cvls;

/// <summary>
/// Markets list — required for the <c>PerMarket = true</c> fields to fan out. Subclass
/// <see cref="MarketsCvl"/> to opt in.
/// </summary>
public sealed class Markets : MarketsCvl
{
    public override CvlDataType DataType => CvlDataType.LocaleString;

    public override IEnumerable<CvlValue> GetValues() => new[]
    {
        new CvlValue("EU", new LocaleString("Europe"),        Index: 0),
        new CvlValue("NA", new LocaleString("North America"), Index: 1),
        new CvlValue("APAC", new LocaleString("Asia Pacific"), Index: 2),
    };
}
