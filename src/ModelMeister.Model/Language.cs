namespace ModelMeister.Model;

/// <summary>
/// An inriver language. <paramref name="IsoCode"/> is the case-insensitive locale identifier
/// (e.g. <c>en-US</c>); <paramref name="IsDefault"/> marks the model's fallback language and
/// exactly one language must carry it (validator MM061).
/// </summary>
public sealed record Language(string IsoCode, bool IsDefault = false);
