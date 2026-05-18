using IriverCvl = inRiver.Remoting.Objects.CVL;
using IriverCvlValue = inRiver.Remoting.Objects.CVLValue;
using ModelMeister.Inriver.Snapshot;
using ModelMeister.Model.Loading;
using ModelMeister.Model.Primitives;

namespace ModelMeister.Inriver.Mapping;

/// <summary>Bi-directional mapping for CVLs (controlled-value lists).</summary>
public static class CvlMapper
{
    /// <summary>Inriver DTO + its values -> snapshot DTO.</summary>
    public static LiveCvl ToLive(IriverCvl c, IEnumerable<IriverCvlValue> values) => new()
    {
        Id = c.Id,
        DataTypeRaw = c.DataType,
        DataType = DatatypeMapper.CvlFromInriver(c.DataType),
        ParentId = NullIfEmpty(c.ParentId),
        CustomValueList = c.CustomValueList,
        Values = values.Select(CvlValueMapper.ToLive).ToList(),
    };

    /// <summary>Code-defined CVL -> inriver DTO. Note: remoting wants null (not empty) for "no parent".</summary>
    public static IriverCvl ToInriver(LoadedCvl c) => new()
    {
        Id = c.CvlId,
        DataType = DatatypeMapper.CvlToInriver(c.DataType),
        ParentId = NullIfEmpty(c.ParentCvlId),
        CustomValueList = c.CustomValueList,
    };

    private static string? NullIfEmpty(string? s) => string.IsNullOrEmpty(s) ? null : s;
}

/// <summary>Bi-directional mapping for individual CVL values.</summary>
public static class CvlValueMapper
{
    /// <summary>Inriver DTO -> snapshot DTO.</summary>
    public static LiveCvlValue ToLive(IriverCvlValue v)
    {
        // Inriver returns the value as either a LocaleString or a primitive — normalize to LocaleString.
        var name = v.Value switch
        {
            inRiver.Remoting.Objects.LocaleString ls => LocaleStringMapper.ToTp(ls),
            null => new LocaleString(),
            var raw => new LocaleString(raw.ToString() ?? string.Empty),
        };
        return new LiveCvlValue
        {
            Id = v.Id,
            CvlId = v.CVLId,
            Key = v.Key,
            Value = name,
            ParentKey = string.IsNullOrEmpty(v.ParentKey) ? null : v.ParentKey,
            Index = v.Index,
            Deactivated = v.Deactivated,
        };
    }

    /// <summary>Code-defined CVL value -> inriver DTO, attached to <paramref name="cvlId"/>.</summary>
    public static IriverCvlValue ToInriver(Model.CvlValue v, string cvlId) => new()
    {
        CVLId = cvlId,
        Key = v.Key,
        Value = LocaleStringMapper.ToInriver(v.Value),
        ParentKey = string.IsNullOrEmpty(v.Parent) ? null : v.Parent,
        Index = v.Index,
        Deactivated = v.Deactivated,
    };
}
