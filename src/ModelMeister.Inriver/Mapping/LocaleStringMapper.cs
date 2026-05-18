using System.Collections.Concurrent;
using System.Globalization;
using IriverLocaleString = inRiver.Remoting.Objects.LocaleString;
using TpLocaleString = ModelMeister.Model.Primitives.LocaleString;

namespace ModelMeister.Inriver.Mapping;

/// <summary>Bi-directional mapping between inriver's <see cref="IriverLocaleString"/> and the code-side <see cref="TpLocaleString"/>.</summary>
public static class LocaleStringMapper
{
    // CultureInfo lookups are not trivial (registry/ICU); cache canonical names + parsed instances.
    private static readonly ConcurrentDictionary<string, CultureInfo?> CultureCache =
        new(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Look up a <see cref="CultureInfo"/> from an iso code, normalising case (e.g. <c>en-us</c> -> <c>en-US</c>).
    /// Returns <c>null</c> for unknown codes — safe to call with arbitrary user input.
    /// </summary>
    public static CultureInfo? TryGetCulture(string? isoCode)
    {
        if (string.IsNullOrWhiteSpace(isoCode)) return null;
        return CultureCache.GetOrAdd(isoCode, static code =>
        {
            try { return CultureInfo.GetCultureInfo(code); }
            catch (CultureNotFoundException) { return null; }
        });
    }

    /// <summary>Returns the canonical iso name (case-corrected) for the supplied code, or the original on miss.</summary>
    public static string NormaliseIso(string isoCode) => TryGetCulture(isoCode)?.Name ?? isoCode;

    /// <summary>Inriver <see cref="IriverLocaleString"/> -> code-side <see cref="TpLocaleString"/>. Null in, empty out.</summary>
    public static TpLocaleString ToTp(IriverLocaleString? ls)
    {
        if (ls is null) return new TpLocaleString();
        var values = (ls.Languages ?? [])
            .Select(ci => (Iso: ci.Name, Value: ls[ci]))
            .Where(p => !string.IsNullOrEmpty(p.Value))
            .ToDictionary(p => p.Iso, p => p.Value, StringComparer.OrdinalIgnoreCase);
        var defaultValue = values.Values.FirstOrDefault() ?? string.Empty;
        return new TpLocaleString(defaultValue, values);
    }

    /// <summary>
    /// Code-side <see cref="TpLocaleString"/> -> inriver DTO. If <paramref name="supportedLanguages"/>
    /// is supplied, emit a value for each — otherwise infer from the code-side <c>Values</c> map (or fall
    /// back to invariant culture when only a <c>DefaultValue</c> is set).
    /// </summary>
    public static IriverLocaleString ToInriver(TpLocaleString? tp, IEnumerable<CultureInfo>? supportedLanguages = null)
    {
        var result = new IriverLocaleString();
        if (tp is null) return result;

        var languages = (supportedLanguages ?? []).ToList();
        if (languages.Count == 0)
        {
            languages.AddRange(tp.Values.Keys
                .Select(TryGetCulture)
                .Where(ci => ci is not null)
                .Select(ci => ci!));
        }
        if (languages.Count == 0 && !string.IsNullOrEmpty(tp.DefaultValue))
        {
            languages.Add(CultureInfo.InvariantCulture);
        }

        foreach (var ci in languages)
        {
            var v = tp.For(ci.Name);
            if (string.IsNullOrEmpty(v)) v = tp.DefaultValue;
            result[ci] = v ?? string.Empty;
        }
        return result;
    }
}
