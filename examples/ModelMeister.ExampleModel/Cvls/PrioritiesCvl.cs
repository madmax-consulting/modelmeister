using ModelMeister.Model;
using ModelMeister.Model.Primitives;

namespace ModelMeister.ExampleModel.Cvls;

/// <summary>Integer-typed CVL — exercises <see cref="CvlDataType.Integer"/>.</summary>
public sealed class PrioritiesCvl : Cvl
{
    public override CvlDataType DataType => CvlDataType.Integer;

    public override IEnumerable<CvlValue> GetValues() => new[]
    {
        new CvlValue("1", new LocaleString("Critical"),  Index: 0),
        new CvlValue("2", new LocaleString("High"),      Index: 1),
        new CvlValue("3", new LocaleString("Normal"),    Index: 2),
        new CvlValue("4", new LocaleString("Low"),       Index: 3),
        // Deactivated value — round-trips through Remoting as Deactivated=true.
        new CvlValue("9", new LocaleString("Deprecated"), Index: 4, Deactivated: true),
    };
}
