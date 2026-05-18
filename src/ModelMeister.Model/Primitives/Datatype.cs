namespace ModelMeister.Model.Primitives;

/// <summary>The set of field data types supported by inriver.</summary>
public enum Datatype
{
    String,
    LocaleString,
    Integer,
    Double,
    Boolean,
    DateTime,
    Xml,
    Cvl,
    File,
}

/// <summary>The set of CVL value data types supported by inriver.</summary>
public enum CvlDataType
{
    String,
    LocaleString,
    Integer,
    Double,
    DateTime,
}

/// <summary>Kind of restriction applied to a field for a given role or category.</summary>
public enum RestrictionType
{
    Hidden,
    Readonly,
    Visible,
    Editonly,
}

/// <summary>Time-unit token recognised by inriver's DATETIMEADD / DATETIMEDIF functions.</summary>
public enum DateTimeUnit
{
    Years,
    Months,
    Days,
    Hours,
    Minutes,
    Seconds,
}
