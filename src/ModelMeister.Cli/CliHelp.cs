using System.CommandLine;
using System.CommandLine.Binding;
using System.CommandLine.Builder;
using System.CommandLine.Help;
using System.Runtime.CompilerServices;

namespace ModelMeister.Cli;

/// <summary>
/// Help-system extensions for the ModelMeister CLI. Attaches examples, "See also" cross-references,
/// and an optional root preamble to <see cref="Command"/> instances and surfaces them through a
/// custom <see cref="HelpBuilder"/> layout. Metadata is stored in a
/// <see cref="ConditionalWeakTable{TKey,TValue}"/> so we don't touch the third-party command type.
/// </summary>
internal static class CliHelp
{
    private static readonly ConditionalWeakTable<Command, CommandExtras> Extras = new();

    /// <summary>Attach one or more (label, command-line) example pairs that render under "Examples:".</summary>
    public static Command WithExamples(this Command cmd, params (string Label, string CommandLine)[] examples)
    {
        Get(cmd).Examples.AddRange(examples);
        return cmd;
    }

    /// <summary>Attach "See also" references that render at the end of help.</summary>
    public static Command WithSeeAlso(this Command cmd, params (string Command, string Description)[] refs)
    {
        Get(cmd).SeeAlso.AddRange(refs);
        return cmd;
    }

    /// <summary>Attach a preamble paragraph that renders right after the description (used on the root command).</summary>
    public static Command WithPreamble(this Command cmd, string preamble)
    {
        Get(cmd).Preamble = preamble;
        return cmd;
    }

    /// <summary>
    /// Hook to be passed to <see cref="CommandLineBuilderExtensions.UseHelpBuilder"/>. Returns a
    /// <see cref="HelpBuilder"/> whose layout calls the default sections, then appends our custom
    /// Examples and See-also sections, with an optional preamble inserted after the description.
    /// </summary>
    public static HelpBuilder Build(BindingContext _)
    {
        var width = SafeConsoleWidth();
        var hb = new HelpBuilder(LocalizationResources.Instance, width);
        hb.CustomizeLayout(_ =>
        {
            var sections = new List<HelpSectionDelegate>(HelpBuilder.Default.GetLayout());
            // Slot the preamble in just after the synopsis (the description block).
            sections.Insert(1, ctx =>
            {
                if (Extras.TryGetValue(ctx.Command, out var ex) && !string.IsNullOrEmpty(ex.Preamble))
                {
                    ctx.Output.WriteLine(ex.Preamble);
                    ctx.Output.WriteLine();
                }
            });
            sections.Add(WriteExamples);
            sections.Add(WriteSeeAlso);
            return sections;
        });
        return hb;
    }

    private static void WriteExamples(HelpContext ctx)
    {
        if (!Extras.TryGetValue(ctx.Command, out var ex) || ex.Examples.Count == 0) return;
        ctx.Output.WriteLine("Examples:");
        foreach (var (label, command) in ex.Examples)
        {
            if (!string.IsNullOrWhiteSpace(label))
                ctx.Output.WriteLine("  # " + label);
            ctx.Output.WriteLine("  $ " + command);
            ctx.Output.WriteLine();
        }
    }

    private static void WriteSeeAlso(HelpContext ctx)
    {
        if (!Extras.TryGetValue(ctx.Command, out var ex) || ex.SeeAlso.Count == 0) return;
        ctx.Output.WriteLine("See also:");
        foreach (var (com, desc) in ex.SeeAlso)
            ctx.Output.WriteLine($"  {com.PadRight(28)}{desc}");
        ctx.Output.WriteLine();
    }

    private static CommandExtras Get(Command cmd)
    {
        if (Extras.TryGetValue(cmd, out var ex)) return ex;
        ex = new CommandExtras();
        Extras.Add(cmd, ex);
        return ex;
    }

    private static int SafeConsoleWidth()
    {
        try { return Math.Max(80, Console.WindowWidth); }
        catch { return 100; }
    }

    private sealed class CommandExtras
    {
        public readonly List<(string Label, string CommandLine)> Examples = [];
        public readonly List<(string Command, string Description)> SeeAlso = [];
        public string? Preamble;
    }
}
