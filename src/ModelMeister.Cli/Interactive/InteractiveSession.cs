using Spectre.Console;
using ModelMeister.Cli.Commands;
using ModelMeister.Inriver.Diff;

namespace ModelMeister.Cli.Interactive;

/// <summary>
/// A guided menu that wraps the individual commands for users who'd rather not
/// memorise flags. Returned exit codes follow the same contract as the non-interactive
/// commands.
/// </summary>
public sealed class InteractiveSession
{
    // Menu labels are kept as constants so the switch below stays tidy.
    private const string Scaffold = "Scaffold from inriver JSON";
    private const string Merge = "Merge two JSON exports";
    private const string Validate = "Validate a local model";
    private const string Describe = "Describe a local model";
    private const string Connect = "Connect to an inriver environment";
    private const string Exit = "Exit";

    /// <summary>Runs the top-level menu loop until the user chooses Exit or <paramref name="ct"/> is cancelled.</summary>
    public async Task<int> RunAsync(CancellationToken ct)
    {
        AnsiConsole.Write(new FigletText("ModelMeister").Color(Color.Aqua));
        AnsiConsole.MarkupLine("[grey]Exit codes — 0:ok 1:changes 2:usage 3:validation 4:operation[/]");

        while (!ct.IsCancellationRequested)
        {
            var choice = AnsiConsole.Prompt(new SelectionPrompt<string>()
                .Title("What do you want to do?")
                .AddChoices(Scaffold, Merge, Validate, Describe, Connect, Exit));

            switch (choice)
            {
                case Scaffold: RunScaffold(); break;
                case Merge: RunMerge(); break;
                case Validate: RunValidate(); break;
                case Describe: RunDescribe(); break;
                case Connect: await RunConnect(ct).ConfigureAwait(false); break;
                case Exit: return ExitCodes.Success;
            }
        }

        return ExitCodes.Success;
    }

    private static void RunScaffold()
    {
        var json = AnsiConsole.Ask<string>("Inriver export [grey](path to .json)[/]:");
        if (!File.Exists(json))
        {
            AnsiConsole.MarkupLine("[red]File not found.[/]");
            return;
        }
        var defaultOut = Path.Combine(Path.GetDirectoryName(json)!, "GeneratedModel");
        var outDir = AnsiConsole.Ask("Output directory:", defaultOut);
        var ns = AnsiConsole.Ask("Root namespace:", "Generated.Model");
        ScaffoldCommand.Run(json, outDir, ns, detectBaseClasses: true);
    }

    private static void RunMerge()
    {
        var baseP = AnsiConsole.Ask<string>("Base JSON path:");
        var overlayP = AnsiConsole.Ask<string>("Overlay JSON path:");
        var outP = AnsiConsole.Ask("Output JSON path:", "merged.json");
        var policy = AnsiConsole.Prompt(new SelectionPrompt<string>()
            .Title("On conflict:")
            .AddChoices("overlay-wins", "base-wins", "fail"));
        MergeCommand.Run(baseP, overlayP, outP, policy);
    }

    private static void RunValidate() =>
        ValidateCommand.Run(AnsiConsole.Ask<string>("Model DLL or csproj path:"), json: false);

    private static void RunDescribe() =>
        DescribeCommand.Run(AnsiConsole.Ask<string>("Model DLL or csproj path:"), json: false);

    private static async Task RunConnect(CancellationToken ct)
    {
        var url = AnsiConsole.Ask<string>("Inriver URL:");
        var auth = PromptForAuth();

        while (!ct.IsCancellationRequested)
        {
            var action = AnsiConsole.Prompt(new SelectionPrompt<string>()
                .Title("Action?")
                .AddChoices(
                    "Status",
                    "View diff",
                    "Apply changes (dry run)",
                    "Apply changes",
                    "Export live model to JSON",
                    "Scaffold from live model",
                    "Back"));

            if (action == "Back") break;

            string? modelPath = action is "View diff" or "Apply changes" or "Apply changes (dry run)"
                ? AnsiConsole.Ask<string>("Model DLL or csproj path:")
                : null;

            await DispatchConnectedAction(action, url, auth, modelPath, ct).ConfigureAwait(false);
        }
    }

    private static InriverAuth PromptForAuth()
    {
        var choice = AnsiConsole.Prompt(new SelectionPrompt<string>()
            .Title("Auth method")
            .AddChoices("API key", "Username/Password"));

        if (choice == "API key")
        {
            var apiKey = AnsiConsole.Prompt(new TextPrompt<string>("API key:").Secret());
            return new InriverAuth(apiKey, null, null, null);
        }

        var user = AnsiConsole.Ask<string>("Username:");
        var pw = AnsiConsole.Prompt(new TextPrompt<string>("Password:").Secret());
        var env = AnsiConsole.Ask<string>("Environment:");
        return new InriverAuth(null, user, pw, env);
    }

    private static async Task DispatchConnectedAction(string action, string url, InriverAuth auth, string? modelPath, CancellationToken ct)
    {
        switch (action)
        {
            case "Status":
                await StatusCommand.RunAsync(url, auth, ct).ConfigureAwait(false);
                break;

            case "View diff":
                await DiffCommand.RunAsync(modelPath!, url, auth, "tree", null, failOnChanges: false, MergePolicy.Default, ct)
                    .ConfigureAwait(false);
                break;

            case "Apply changes (dry run)":
                await ApplyCommand.RunAsync(modelPath!, url, auth, yes: true, dryRun: true, MergePolicy.Default, ct)
                    .ConfigureAwait(false);
                break;

            case "Apply changes":
                var allowDeletes = AnsiConsole.Confirm("Allow deletes?", defaultValue: false);
                var policy = MergePolicy.Default with { AllowDeletes = allowDeletes };
                await ApplyCommand.RunAsync(modelPath!, url, auth, yes: false, dryRun: false, policy, ct)
                    .ConfigureAwait(false);
                break;

            case "Export live model to JSON":
                var outPath = AnsiConsole.Ask("Output JSON path:", "live-model.json");
                await ExportCommand.RunAsync(url, auth, outPath, ct).ConfigureAwait(false);
                break;

            case "Scaffold from live model":
                var scOut = AnsiConsole.Ask("Output directory:", "./GeneratedModel");
                var scNs = AnsiConsole.Ask("Root namespace:", "Generated.Model");
                await ScaffoldCommand
                    .RunFromEnvAsync(url, auth, scOut, scNs, detectBaseClasses: true, ct)
                    .ConfigureAwait(false);
                break;
        }
    }
}
