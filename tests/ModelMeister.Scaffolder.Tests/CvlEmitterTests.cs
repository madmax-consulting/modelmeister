using System.Text.Json;
using Shouldly;
using Xunit;

namespace ModelMeister.Scaffolder.Tests;

public class CvlEmitterTests
{
    private static JsonElement Str(string s) => JsonSerializer.SerializeToElement(s);
    private static JsonElement Localised(params (string lang, string text)[] entries)
        => JsonSerializer.SerializeToElement(new { StringMap = entries.ToDictionary(e => e.lang, e => e.text) });

    [Fact]
    public void String_cvl_emits_raw_string_via_implicit_conversion()
    {
        var cvl = new JsonCvl { Id = "ItemSeries", DataType = "String" };
        var values = new List<JsonCvlValue>
        {
            new() { CvlId = "ItemSeries", Key = "4941", Value = Str("5000"), Index = 0, Deactivated = false },
            new() { CvlId = "ItemSeries", Key = "6089", Value = Str("Note8"), Index = 3, Deactivated = true },
        };

        var src = CvlEmitter.Emit(cvl, values, "Acme.PimModel");

        src.ShouldContain("new CvlValue(\"4941\", \"5000\")");
        src.ShouldContain("new CvlValue(\"6089\", \"Note8\", Index: 3, Deactivated: true)");
        src.ShouldNotContain("LocaleString");
        src.ShouldNotContain("{ Key =");
    }

    [Fact]
    public void LocaleString_cvl_emits_LocaleString_constructor()
    {
        var cvl = new JsonCvl { Id = "Brand", DataType = "LocaleString" };
        var values = new List<JsonCvlValue>
        {
            new() { CvlId = "Brand", Key = "Nike", Value = Localised(("en", "Nike"), ("sv", "Najk")) },
        };

        var src = CvlEmitter.Emit(cvl, values, "Acme.PimModel");

        src.ShouldContain("new CvlValue(\"Nike\", new LocaleString(\"Nike\").With(\"sv\", \"Najk\"))");
    }

    [Fact]
    public void Default_values_skipped()
    {
        var cvl = new JsonCvl { Id = "ItemSeries", DataType = "String" };
        var values = new List<JsonCvlValue>
        {
            new() { CvlId = "ItemSeries", Key = "K1", Value = Str("V1"), Index = 0, Deactivated = false },
        };

        var src = CvlEmitter.Emit(cvl, values, "Acme.PimModel");

        src.ShouldNotContain("Index: 0");
        src.ShouldNotContain("Deactivated: false");
    }

    [Fact]
    public void Emit_values_false_omits_value_entries_but_stays_loadable()
    {
        var cvl = new JsonCvl { Id = "ItemSeries", DataType = "String" };
        var values = new List<JsonCvlValue>
        {
            new() { CvlId = "ItemSeries", Key = "K1", Value = Str("V1") },
        };

        var src = CvlEmitter.Emit(cvl, values, "Acme.PimModel", emitValues: false);

        // No actual value entries emitted...
        src.ShouldNotContain("new CvlValue(");
        // ...but the class must still override Values, otherwise the base throws
        // NotImplementedException at load time (Cvl.Values / Cvl.GetValues).
        src.ShouldContain("protected override IEnumerable<CvlValue> Values => Array.Empty<CvlValue>();");
    }
}
