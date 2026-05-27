using System.IO;
using Shouldly;
using ModelMeister.Excel;
using ModelMeister.Inriver.HtmlTemplates;
using Xunit;

namespace ModelMeister.Excel.Tests;

/// <summary>An HTML-template workbook must round-trip name/type/properties/localized-name, and faithfully
/// preserve oversize bodies by spilling them to a sidecar file.</summary>
public class HtmlTemplateWorkbookRoundTripTests
{
    [Fact]
    public void Save_then_load_preserves_small_templates()
    {
        var templates = new List<HtmlTemplateDto>
        {
            new()
            {
                Name = "Spec sheet",
                TemplateType = "print",
                Properties = "orientation=landscape",
                Content = "<html><body>{{name}}</body></html>",
                LocalizedName = new(StringComparer.OrdinalIgnoreCase) { ["en"] = "Spec sheet", ["sv"] = "Specifikation" },
            },
            new() { Name = "Label", TemplateType = "label", Content = "<b>{{sku}}</b>" },
        };

        var path = Path.Combine(Path.GetTempPath(), "mm-html-" + Guid.NewGuid().ToString("N") + ".xlsx");
        try
        {
            HtmlTemplateWorkbook.Save(templates, path);
            var loaded = HtmlTemplateWorkbook.Load(path);

            loaded.Count.ShouldBe(2);
            var spec = loaded.Single(t => t.Name == "Spec sheet");
            spec.TemplateType.ShouldBe("print");
            spec.Properties.ShouldBe("orientation=landscape");
            spec.Content.ShouldBe("<html><body>{{name}}</body></html>");
            spec.LocalizedName["en"].ShouldBe("Spec sheet");
            spec.LocalizedName["sv"].ShouldBe("Specifikation");
        }
        finally
        {
            CleanUp(path);
        }
    }

    [Fact]
    public void Oversize_content_round_trips_via_sidecar()
    {
        // Exceeds Excel's 32,767-char cell cap — must spill to a sidecar file and come back intact.
        var big = new string('x', 40_000) + "<end/>";
        var templates = new List<HtmlTemplateDto>
        {
            new() { Name = "Huge", TemplateType = "print", Content = big },
        };

        var path = Path.Combine(Path.GetTempPath(), "mm-html-big-" + Guid.NewGuid().ToString("N") + ".xlsx");
        try
        {
            HtmlTemplateWorkbook.Save(templates, path);
            var loaded = HtmlTemplateWorkbook.Load(path);
            loaded.Single().Content.ShouldBe(big);
        }
        finally
        {
            CleanUp(path);
        }
    }

    private static void CleanUp(string path)
    {
        if (File.Exists(path)) File.Delete(path);
        var sidecar = Path.Combine(Path.GetDirectoryName(path)!, Path.GetFileNameWithoutExtension(path) + "_files");
        if (Directory.Exists(sidecar)) Directory.Delete(sidecar, recursive: true);
    }
}
