using ModelMeister.Model;

namespace ModelMeister.ExampleModel;

public static class Languages
{
    public static IEnumerable<Language> All { get; } = new[]
    {
        new Language("en-US", IsDefault: true),
        new Language("sv-SE"),
        new Language("de-DE"),
        new Language("ja-JP"),
    };
}
