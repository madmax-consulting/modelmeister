using ModelMeister.Model.Primitives;

namespace ModelMeister.Model;

/// <summary>
/// Base class for an inriver Controlled Vocabulary List (CVL). The CLR type name with any
/// trailing <c>Cvl</c> suffix stripped becomes the <see cref="CvlId"/>.
/// </summary>
public abstract class Cvl : IFieldBinding
{
    private const string Suffix = "Cvl";

    protected Cvl()
    {
        var name = GetType().Name;
        if (name.EndsWith(Suffix, StringComparison.Ordinal))
            name = name[..^Suffix.Length];
        CvlId = name;
    }

    /// <summary>The inriver CVL ID.</summary>
    public string CvlId { get; init; }

    /// <summary>The CVL value's data type. Defaults to <see cref="CvlDataType.LocaleString"/>.</summary>
    public virtual CvlDataType DataType => CvlDataType.LocaleString;

    /// <summary>Optional parent CVL, modelling a parent-child relationship.</summary>
    public virtual Type? ParentCvl => null;

    /// <summary>When true, end-users may extend the CVL with their own values in inriver.</summary>
    public virtual bool CustomValueList => false;

    /// <summary>Optional entity type the CVL is keyed by (rare).</summary>
    public virtual Type? EntityType => null;

    /// <summary>
    /// Derived CVLs may override either <see cref="Values"/> (the historic, scaffolder-emitted
    /// pattern) or <see cref="GetValues"/> (the current pattern). <see cref="GetValues"/> is what
    /// the rest of the toolkit calls; if neither is overridden we fail loudly at runtime instead
    /// of returning a silent empty CVL.
    /// </summary>
    protected virtual IEnumerable<CvlValue> Values =>
        throw new NotImplementedException(
            $"CVL '{GetType().Name}' must override either GetValues() or the Values property.");

    /// <summary>Returns the CVL values. Override either this or <see cref="Values"/>.</summary>
    public virtual IEnumerable<CvlValue> GetValues() => Values;
}
