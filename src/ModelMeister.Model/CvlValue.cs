using ModelMeister.Model.Primitives;

namespace ModelMeister.Model;

/// <summary>
/// A single value within a <see cref="Cvl"/>. <paramref name="Key"/> is the inriver-side identifier;
/// <paramref name="Value"/> is the localised display. <paramref name="Parent"/> wires up parent-child
/// CVLs; <paramref name="Index"/> controls UI ordering; <paramref name="Deactivated"/> hides the value
/// from end-users without deleting it.
/// </summary>
public sealed record CvlValue(
    string Key,
    LocaleString Value,
    string? Parent = null,
    int Index = 0,
    bool Deactivated = false);
