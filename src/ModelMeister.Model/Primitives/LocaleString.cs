namespace ModelMeister.Model.Primitives;

/// <summary>
/// A culture-aware string: a default value plus an optional dictionary of ISO-code-keyed
/// translations. Used everywhere inriver supports localised labels (entity-type names, field
/// names, descriptions, CVL values, etc.). Implicitly convertible from a plain <see cref="string"/>.
/// </summary>
public sealed class LocaleString
{
    /// <summary>Creates an empty locale string.</summary>
    public LocaleString() : this(string.Empty) { }

    /// <summary>Creates a locale string with no translations beyond the default value.</summary>
    public LocaleString(string defaultValue)
        : this(defaultValue, new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)) { }

    /// <summary>Creates a locale string seeded with translations. Keys are ISO codes (case-insensitive).</summary>
    public LocaleString(string defaultValue, IDictionary<string, string> values)
    {
        DefaultValue = defaultValue;
        Values = new Dictionary<string, string>(values, StringComparer.OrdinalIgnoreCase);
    }

    /// <summary>The fallback text used when an ISO-code lookup misses or returns empty.</summary>
    public string DefaultValue { get; set; }

    /// <summary>ISO-code keyed translations. Case-insensitive lookup.</summary>
    public Dictionary<string, string> Values { get; }

    /// <summary>Adds (or replaces) a translation and returns <c>this</c> for fluent chaining.</summary>
    public LocaleString With(string isoCode, string text)
    {
        Values[isoCode] = text;
        return this;
    }

    /// <summary>Returns the translation for <paramref name="isoCode"/>, falling back to <see cref="DefaultValue"/>.</summary>
    public string For(string isoCode) =>
        Values.TryGetValue(isoCode, out var v) && !string.IsNullOrEmpty(v) ? v : DefaultValue;

    public static implicit operator LocaleString(string value) => new(value);

    public override string ToString() => DefaultValue;

    public override bool Equals(object? obj) =>
        obj is LocaleString other
        && DefaultValue == other.DefaultValue
        && Values.Count == other.Values.Count
        && Values.All(kv => other.Values.TryGetValue(kv.Key, out var v) && v == kv.Value);

    public override int GetHashCode()
    {
        var hash = new HashCode();
        hash.Add(DefaultValue, StringComparer.Ordinal);
        foreach (var kvp in Values.OrderBy(k => k.Key, StringComparer.OrdinalIgnoreCase))
        {
            hash.Add(kvp.Key, StringComparer.OrdinalIgnoreCase);
            hash.Add(kvp.Value, StringComparer.Ordinal);
        }
        return hash.ToHashCode();
    }
}
