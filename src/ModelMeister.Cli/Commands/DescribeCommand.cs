using System.Text.Json;
using Spectre.Console;
using ModelMeister.Loading;

namespace ModelMeister.Cli.Commands;

/// <summary>
/// Prints a summary of a code-defined model — either as a Spectre table for humans
/// or as JSON for tooling.
/// </summary>
public static class DescribeCommand
{
    private const int PreviewCount = 8;

    /// <summary>Loads <paramref name="modelPath"/> and prints a summary.</summary>
    public static int Run(string modelPath, bool json)
    {
        var loader = new ModelAssemblyLoader();
        var model = loader.LoadFromPath(modelPath);

        if (json)
        {
            var payload = new
            {
                EntityTypes = model.EntityTypes.Select(e => new { e.EntityTypeId, FieldCount = e.Fields.Count, e.IsLinkEntityType }),
                Cvls = model.Cvls.Select(c => new { c.CvlId, DataType = c.DataType.ToString(), Values = c.Values.Count }),
                LinkTypes = model.LinkTypes.Select(l => new { l.LinkTypeId, l.SourceEntityTypeId, l.TargetEntityTypeId }),
                Categories = model.Categories.Select(c => new { c.CategoryId, c.Index }),
                Fieldsets = model.Fieldsets.Select(f => new { f.FieldsetId, f.EntityTypeId }),
                Roles = model.Roles.Select(r => new { r.Name, PermissionCount = r.PermissionNames.Count }),
                Languages = model.Languages.Select(l => new { l.IsoCode, l.IsDefault }),
            };
            Console.WriteLine(JsonSerializer.Serialize(payload, new JsonSerializerOptions { WriteIndented = true }));
            return ExitCodes.Success;
        }

        var table = new Table()
            .Title("Model summary")
            .AddColumn("Concept")
            .AddColumn("Count")
            .AddColumn("Details");

        table.AddRow("Entity types", model.EntityTypes.Count.ToString(), Preview(model.EntityTypes.Select(e => e.EntityTypeId)));
        table.AddRow("CVLs",         model.Cvls.Count.ToString(),        Preview(model.Cvls.Select(c => c.CvlId)));
        table.AddRow("Link types",   model.LinkTypes.Count.ToString(),   Preview(model.LinkTypes.Select(l => l.LinkTypeId)));
        table.AddRow("Categories",   model.Categories.Count.ToString(),  string.Empty);
        table.AddRow("Fieldsets",    model.Fieldsets.Count.ToString(),   string.Empty);
        table.AddRow("Roles",        model.Roles.Count.ToString(),       Preview(model.Roles.Select(r => r.Name)));
        table.AddRow("Languages",    model.Languages.Count.ToString(),   string.Join(", ", model.Languages.Select(l => l.IsoCode)));

        AnsiConsole.Write(table);
        return ExitCodes.Success;
    }

    /// <summary>Joins up to <see cref="PreviewCount"/> identifiers, appending an ellipsis when more exist.</summary>
    private static string Preview(IEnumerable<string> items)
    {
        var list = items.ToList();
        var head = string.Join(", ", list.Take(PreviewCount));
        return list.Count > PreviewCount ? head + " ..." : head;
    }
}
