using ModelMeister.Model;
using ModelMeister.Model.Primitives;

namespace ModelMeister.ExampleModel.Cvls;

/// <summary>
/// Custom-value-list CVL: end users can add values from the inriver UI. Seed values declared here
/// are still applied on first sync but the list is open-ended.
/// </summary>
public sealed class BrandsCvl : Cvl
{
    public override CvlDataType DataType => CvlDataType.String;
    public override bool CustomValueList => true;

    public override IEnumerable<CvlValue> GetValues() => new[]
    {
        new CvlValue("Acme",   new LocaleString("Acme"),   Index: 0),
        new CvlValue("Globex", new LocaleString("Globex"), Index: 1),
        new CvlValue("Initech", new LocaleString("Initech"), Index: 2),
    };
}
