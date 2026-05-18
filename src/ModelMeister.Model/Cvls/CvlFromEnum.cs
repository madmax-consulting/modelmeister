using ModelMeister.Model.Primitives;

namespace ModelMeister.Model.Cvls;

/// <summary>
/// CVL whose values are derived from the members of an enum type. The enum member name becomes
/// the CVL key; the value uses the member name as a default display, which can be overridden via
/// translations.
/// </summary>
public abstract class CvlFromEnum<TEnum> : Cvl where TEnum : struct, Enum
{
    public override CvlDataType DataType => CvlDataType.String;

    public override IEnumerable<CvlValue> GetValues() =>
        Enum.GetNames<TEnum>()
            .Select((name, index) => new CvlValue(name, new LocaleString(name), Index: index));
}
