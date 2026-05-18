using ModelMeister.Model.Primitives;

namespace ModelMeister.Inriver.Mapping;

/// <summary>Conversions between code-side <see cref="Datatype"/>/<see cref="CvlDataType"/> and inriver string tokens.</summary>
public static class DatatypeMapper
{
    /// <summary>Parse an inriver field datatype token. Accepts the historical "Cvl" spelling alongside "CVL".</summary>
    public static Datatype FromInriver(string raw) => raw switch
    {
        "String" => Datatype.String,
        "LocaleString" => Datatype.LocaleString,
        "Integer" => Datatype.Integer,
        "Double" => Datatype.Double,
        "Boolean" => Datatype.Boolean,
        "DateTime" => Datatype.DateTime,
        "Xml" => Datatype.Xml,
        "CVL" or "Cvl" => Datatype.Cvl,
        "File" => Datatype.File,
        _ => throw new NotSupportedException($"Unknown inriver datatype '{raw}'."),
    };

    /// <summary>Render a code-side <see cref="Datatype"/> as the inriver string token.</summary>
    public static string ToInriver(Datatype t) => t switch
    {
        Datatype.String => "String",
        Datatype.LocaleString => "LocaleString",
        Datatype.Integer => "Integer",
        Datatype.Double => "Double",
        Datatype.Boolean => "Boolean",
        Datatype.DateTime => "DateTime",
        Datatype.Xml => "Xml",
        Datatype.Cvl => "CVL",
        Datatype.File => "File",
        _ => throw new NotSupportedException($"Unknown datatype '{t}'."),
    };

    /// <summary>Parse an inriver CVL datatype token (a strict subset of field datatypes).</summary>
    public static CvlDataType CvlFromInriver(string raw) => raw switch
    {
        "String" => CvlDataType.String,
        "LocaleString" => CvlDataType.LocaleString,
        "Integer" => CvlDataType.Integer,
        "Double" => CvlDataType.Double,
        "DateTime" => CvlDataType.DateTime,
        _ => throw new NotSupportedException($"Unknown CVL datatype '{raw}'."),
    };

    /// <summary>Render a code-side <see cref="CvlDataType"/> as the inriver string token.</summary>
    public static string CvlToInriver(CvlDataType t) => t switch
    {
        CvlDataType.String => "String",
        CvlDataType.LocaleString => "LocaleString",
        CvlDataType.Integer => "Integer",
        CvlDataType.Double => "Double",
        CvlDataType.DateTime => "DateTime",
        _ => throw new NotSupportedException($"Unknown CVL datatype '{t}'."),
    };
}
