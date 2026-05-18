using Spectre.Console;
using ModelMeister.Inriver.Diff;

namespace ModelMeister.Cli.Interactive;

/// <summary>
/// Spectre <see cref="Tree"/> + <see cref="BarChart"/> renderer for a
/// <see cref="ModelChangeSet"/>. Changes are grouped by concept and colour-coded by verb
/// (the first character of <c>Describe()</c>).
/// </summary>
public static class DiffRenderer
{
    /// <summary>Renders <paramref name="set"/> to <paramref name="console"/> (or <see cref="AnsiConsole.Console"/>).</summary>
    public static void Render(ModelChangeSet set, IAnsiConsole? console = null)
    {
        var c = console ?? AnsiConsole.Console;

        if (set.IsEmpty && set.Warnings.Count == 0)
        {
            c.MarkupLine("[green]✓ No changes — environment matches code.[/]");
            return;
        }

        c.Write(BuildTree(set));
        c.Write(BuildSummaryChart(set));
    }

    private static Tree BuildTree(ModelChangeSet set)
    {
        var tree = new Tree($"[bold]Changes:[/] [yellow]{set.Changes.Count}[/]   [bold]Warnings:[/] [yellow]{set.Warnings.Count}[/]");

        var groups = set.Changes
            .GroupBy(ch => GroupOf(ch.GetType().Name))
            .OrderBy(g => g.Key);

        foreach (var group in groups)
        {
            var groupNode = tree.AddNode($"[bold blue]{group.Key}[/] [grey]({group.Count()})[/]");
            foreach (var change in group)
            {
                var description = change.Describe();
                var color = ColorFor(Verb(description));
                groupNode.AddNode($"[{color}]{Markup.Escape(description)}[/]");
            }
        }

        if (set.Warnings.Count > 0)
        {
            var warningsNode = tree.AddNode("[bold yellow]Warnings[/]");
            foreach (var warning in set.Warnings)
                warningsNode.AddNode($"[yellow]{Markup.Escape(warning.Code)}[/]: {Markup.Escape(warning.Message)}");
        }

        return tree;
    }

    private static BarChart BuildSummaryChart(ModelChangeSet set)
    {
        int Count(char verb) => set.Changes.Count(ch => Verb(ch.Describe()) == verb);

        return new BarChart()
            .Width(60)
            .Label("[bold]Change summary[/]")
            .AddItem("Add",       Count('+'), Color.Green)
            .AddItem("Update",    Count('~'), Color.Yellow)
            .AddItem("Remove",    Count('-'), Color.Red)
            .AddItem("Dangerous", Count('!'), Color.Magenta1);
    }

    /// <summary>Returns the leading verb character of a change description, or space when empty.</summary>
    private static char Verb(string description) => string.IsNullOrEmpty(description) ? ' ' : description[0];

    private static string ColorFor(char verb) => verb switch
    {
        '+' => "green",
        '-' => "red",
        '~' => "yellow",
        '!' => "magenta",
        _ => "white",
    };

    /// <summary>Maps a change-type class name to a human-readable group label.</summary>
    private static string GroupOf(string kind) => kind switch
    {
        _ when kind.Contains("EntityType", StringComparison.Ordinal) => "EntityTypes",
        _ when kind.Contains("FieldType", StringComparison.Ordinal)
            || kind.Contains("FieldToFieldset", StringComparison.Ordinal) => "Fields",
        _ when kind.Contains("Cvl", StringComparison.Ordinal) => "CVLs",
        _ when kind.Contains("Fieldset", StringComparison.Ordinal) => "Fieldsets",
        _ when kind.Contains("LinkType", StringComparison.Ordinal) => "LinkTypes",
        _ when kind.Contains("Category", StringComparison.Ordinal) => "Categories",
        _ when kind.Contains("Role", StringComparison.Ordinal)
            || kind.Contains("Permission", StringComparison.Ordinal) => "Security",
        _ when kind.Contains("Language", StringComparison.Ordinal) => "Languages",
        _ when kind.Contains("Completeness", StringComparison.Ordinal) => "Completeness",
        _ => "Other",
    };
}
