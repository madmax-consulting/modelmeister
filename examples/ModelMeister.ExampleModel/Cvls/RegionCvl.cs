using ModelMeister.Model;
using ModelMeister.Model.Primitives;

namespace ModelMeister.ExampleModel.Cvls;

/// <summary>Parent CVL: region (EU, NA, APAC). Children declare cities/markets.</summary>
public sealed class RegionCvl : Cvl
{
    public override CvlDataType DataType => CvlDataType.LocaleString;

    public override IEnumerable<CvlValue> GetValues() => new[]
    {
        new CvlValue("EU",   new LocaleString("Europe"),       Index: 0),
        new CvlValue("NA",   new LocaleString("North America"), Index: 1),
        new CvlValue("APAC", new LocaleString("Asia Pacific"),  Index: 2),
    };
}

public sealed class CountryCvl : Cvl
{
    public override CvlDataType DataType => CvlDataType.LocaleString;
    public override Type? ParentCvl => typeof(RegionCvl);

    public override IEnumerable<CvlValue> GetValues() => new[]
    {
        new CvlValue("SE", new LocaleString("Sweden"),  Parent: "EU",   Index: 0),
        new CvlValue("DE", new LocaleString("Germany"), Parent: "EU",   Index: 1),
        new CvlValue("US", new LocaleString("USA"),     Parent: "NA",   Index: 2),
        new CvlValue("JP", new LocaleString("Japan"),   Parent: "APAC", Index: 3),
    };
}
