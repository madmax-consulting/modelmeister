using System.CommandLine;
using System.CommandLine.Builder;
using System.CommandLine.Parsing;
using System.Reflection;
using Spectre.Console;
using ModelMeister.Cli;
using ModelMeister.Cli.Commands;
using ModelMeister.Cli.Interactive;
using ModelMeister.Inriver.Diff;

// --- Global colour handling --------------------------------------------------
// Honour --no-color or the conventional NO_COLOR env var so CI logs stay clean.
if (args.Contains("--no-color") || !string.IsNullOrEmpty(Environment.GetEnvironmentVariable("NO_COLOR")))
{
    AnsiConsole.Profile.Capabilities.ColorSystem = ColorSystem.NoColors;
    AnsiConsole.Profile.Capabilities.Ansi = false;
}

// --- No-args -> interactive mode --------------------------------------------
// `--no-color` on its own counts as no-args. `--version` is intercepted before help so the
// command tree doesn't have to declare it as an option on every subcommand.
if (args is [] or ["--no-color"])
    return await new InteractiveSession().RunAsync(CancellationToken.None).ConfigureAwait(false);

if (args is ["--version"] or ["-v"])
{
    AnsiConsole.WriteLine(GetVersion());
    return ExitCodes.Success;
}

// --- Root + per-command wiring ----------------------------------------------
var root = new RootCommand(
    "ModelMeister — inriver model management CLI.\n" +
    "Exit codes: 0 success · 1 changes pending · 2 usage error · 3 validation failed · 4 operation failed.");

root.AddGlobalOption(new Option<bool>("--no-color", "Disable ANSI colour output (also: NO_COLOR env var)"));

root.AddCommand(BuildModel());
root.AddCommand(BuildEnv());
root.AddCommand(BuildScaffold());
root.AddCommand(BuildExcel());
root.AddCommand(BuildCvl());
root.AddCommand(BuildJson());
root.AddCommand(BuildUsers());
root.AddCommand(BuildExtensions());
root.AddCommand(BuildWorkAreas());
root.AddCommand(BuildHtmlTemplates());
root.AddCommand(BuildWorkflows());
root.AddCommand(BuildInteractive());

root.WithPreamble(
    """
    Common workflows:
      Daily dev          model validate  →  model diff  →  model apply
      CI gating          model diff --format json --fail-on-changes
      Capture a snapshot env snapshot --url <U> --out snap.json
      Compare two envs   env compare --left-url <U1> --right-url <U2>
      Excel handoff      excel export --url <U> --out m.xlsx  (edit)  scaffold --excel m.xlsx --out ./Model

    Run `modelmeister workflows` for the full cheat-sheet, or `modelmeister <command> --help` for examples.
    """);

var parser = new CommandLineBuilder(root)
    .UseDefaults()
    .UseHelpBuilder(CliHelp.Build)
    .Build();

return await parser.InvokeAsync(args).ConfigureAwait(false);

// --- Command builders --------------------------------------------------------
// Each builder is a local function so wiring stays declarative; handlers store
// their exit code in Environment.ExitCode (the System.CommandLine idiom).

static Command BuildModel()
{
    var cmd = new Command("model", "Operate on a code-defined inriver model.");

    // ---- model validate
    var validate = new Command("validate", "Statically validate a code-defined model.");
    var vModel = ModelOpt();
    var vJson = new Option<bool>("--json", "Emit machine-readable JSON output");
    validate.AddOption(vModel);
    validate.AddOption(vJson);
    validate.SetHandler((m, j) => Environment.ExitCode = ValidateCommand.Run(m, j), vModel, vJson);
    validate
        .WithExamples(
            ("Validate during development", "modelmeister model validate --model ./MyModel.csproj"),
            ("CI: machine-readable output for the step summary",
             "modelmeister model validate --model ./MyModel.csproj --json --no-color"))
        .WithSeeAlso(
            ("model diff", "compare validated model to a live env"),
            ("model describe", "print a human summary of the model"));

    // ---- model describe
    var describe = new Command("describe", "Print a summary of a code-defined model.");
    var dModel = ModelOpt();
    var dJson = new Option<bool>("--json", "Emit machine-readable JSON output");
    describe.AddOption(dModel);
    describe.AddOption(dJson);
    describe.SetHandler((m, j) => Environment.ExitCode = DescribeCommand.Run(m, j), dModel, dJson);
    describe
        .WithExamples(
            ("Quick overview of a model project", "modelmeister model describe --model ./MyModel.csproj"))
        .WithSeeAlso(("model validate", "check the model for errors"));

    // ---- model diff
    var diff = new Command("diff", "Compare a code-defined model against a live inriver environment.");
    var dfModel = ModelOpt();
    var dfConn = new ConnectionOptions();
    var dfFormat = new Option<string>("--format", () => "tree", "Output format: tree | text | json");
    var dfOut = new Option<string?>("--out", "Also write the diff to this file");
    var dfFail = new Option<bool>("--fail-on-changes", () => false, "Exit 1 if any changes are detected (for CI gating)");
    var dfAllowDel = new Option<bool>("--allow-deletes", () => false, "Treat code-side removals as deletes rather than warnings");
    var dfAllowDt = new Option<bool>("--allow-datatype-change", () => false, "Permit field datatype changes (destructive)");
    diff.AddOption(dfModel);
    dfConn.AddTo(diff);
    foreach (var o in new Option[] { dfFormat, dfOut, dfFail, dfAllowDel, dfAllowDt })
        diff.AddOption(o);
    diff.SetHandler(async ctx =>
    {
        var policy = MergePolicy.Default with
        {
            AllowDeletes = ctx.ParseResult.GetValueForOption(dfAllowDel),
            AllowDatatypeChange = ctx.ParseResult.GetValueForOption(dfAllowDt),
        };
        Environment.ExitCode = await DiffCommand.RunAsync(
            ctx.ParseResult.GetValueForOption(dfModel)!,
            ctx.ParseResult.GetValueForOption(dfConn.Url)!,
            dfConn.ToAuth(ctx),
            ctx.ParseResult.GetValueForOption(dfFormat) ?? "tree",
            ctx.ParseResult.GetValueForOption(dfOut),
            ctx.ParseResult.GetValueForOption(dfFail),
            policy,
            ctx.GetCancellationToken()).ConfigureAwait(false);
    });
    diff
        .WithExamples(
            ("Inspect pending changes during development",
             "modelmeister model diff --model ./MyModel.csproj --url $URL"),
            ("CI gate: fail the build when the env drifts from code",
             "modelmeister model diff --model ./MyModel.csproj --url $URL --format json --out diff.json --fail-on-changes"))
        .WithSeeAlso(
            ("model apply", "push the diff to the env"),
            ("env compare", "compare two live envs (not code-vs-env)"));

    // ---- model apply
    var apply = new Command("apply", "Apply a code-defined model to an inriver environment.");
    var apModel = ModelOpt();
    var apConn = new ConnectionOptions();
    var apYes = new Option<bool>("--yes", () => false, "Skip the confirmation prompt (required for CI)");
    var apDry = new Option<bool>("--dry-run", () => false, "Report what would change but don't write");
    var apAllowDel = new Option<bool>("--allow-deletes", () => false, "Permit deletions");
    var apAllowDt = new Option<bool>("--allow-datatype-change", () => false, "Permit field datatype changes (destructive)");
    var apAllowRen = new Option<bool>("--allow-cvl-value-rename", () => false, "Permit renaming existing CVL value keys");
    apply.AddOption(apModel);
    apConn.AddTo(apply);
    foreach (var o in new Option[] { apYes, apDry, apAllowDel, apAllowDt, apAllowRen })
        apply.AddOption(o);
    apply.SetHandler(async ctx =>
    {
        var policy = MergePolicy.Default with
        {
            AllowDeletes = ctx.ParseResult.GetValueForOption(apAllowDel),
            AllowDatatypeChange = ctx.ParseResult.GetValueForOption(apAllowDt),
            AllowCvlValueRename = ctx.ParseResult.GetValueForOption(apAllowRen),
        };
        Environment.ExitCode = await ApplyCommand.RunAsync(
            ctx.ParseResult.GetValueForOption(apModel)!,
            ctx.ParseResult.GetValueForOption(apConn.Url)!,
            apConn.ToAuth(ctx),
            ctx.ParseResult.GetValueForOption(apYes),
            ctx.ParseResult.GetValueForOption(apDry),
            policy,
            ctx.GetCancellationToken()).ConfigureAwait(false);
    });
    apply
        .WithExamples(
            ("Dry-run first to inspect what would change",
             "modelmeister model apply --model ./MyModel.csproj --url $URL --yes --dry-run"),
            ("Then apply for real (CI flow)",
             "modelmeister model apply --model ./MyModel.csproj --url $URL --yes"))
        .WithSeeAlso(
            ("model diff", "preview the change set without writing"));

    // ---- model export-xml
    var exportXml = new Command("export-xml", "Export a live env's whole model as inriver-native XML (lift-and-shift).");
    var exConn = new ConnectionOptions();
    var exOut = new Option<string>("--out", "Output .xml path") { IsRequired = true };
    var exCvl = new Option<bool>("--include-cvl-values", () => true, "Embed CVL value items in the XML");
    exConn.AddTo(exportXml);
    exportXml.AddOption(exOut);
    exportXml.AddOption(exCvl);
    exportXml.SetHandler(async ctx =>
    {
        Environment.ExitCode = await ModelXmlCommand.ExportAsync(
            ctx.ParseResult.GetValueForOption(exConn.Url)!,
            exConn.ToAuth(ctx),
            ctx.ParseResult.GetValueForOption(exOut)!,
            ctx.ParseResult.GetValueForOption(exCvl),
            ctx.GetCancellationToken()).ConfigureAwait(false);
    });
    exportXml
        .WithExamples(("Lift a whole model out of an env", "modelmeister model export-xml --url $URL --out model.xml"))
        .WithSeeAlso(("model import-xml", "shift it into another env"));

    // ---- model import-xml
    var importXml = new Command("import-xml", "Import an inriver-native model XML into a live env (backs up first).");
    var imConn = new ConnectionOptions();
    var imIn = new Option<string>("--in", "Source .xml path") { IsRequired = true };
    var imYes = new Option<bool>("--yes", () => false, "Confirm the wholesale merge (required — there is no dry-run)");
    var imNoBackup = new Option<bool>("--no-backup", () => false, "Skip the pre-import JSON model backup");
    imConn.AddTo(importXml);
    foreach (var o in new Option[] { imIn, imYes, imNoBackup }) importXml.AddOption(o);
    importXml.SetHandler(async ctx =>
    {
        Environment.ExitCode = await ModelXmlCommand.ImportAsync(
            ctx.ParseResult.GetValueForOption(imConn.Url)!,
            imConn.ToAuth(ctx),
            ctx.ParseResult.GetValueForOption(imIn)!,
            ctx.ParseResult.GetValueForOption(imYes),
            ctx.ParseResult.GetValueForOption(imNoBackup),
            ctx.GetCancellationToken()).ConfigureAwait(false);
    });
    importXml
        .WithExamples(("Shift a model into an env (backs up first)", "modelmeister model import-xml --url $URL --in model.xml --yes"))
        .WithSeeAlso(("model export-xml", "produce the XML from a source env"));

    cmd.AddCommand(validate);
    cmd.AddCommand(describe);
    cmd.AddCommand(diff);
    cmd.AddCommand(apply);
    cmd.AddCommand(exportXml);
    cmd.AddCommand(importXml);
    return cmd;

    static Option<string> ModelOpt() =>
        new("--model", "Path to model project (csproj), built DLL, or directory") { IsRequired = true };
}

static Command BuildEnv()
{
    var cmd = new Command("env", "Operate against a live inriver environment.");

    // ---- env status
    var status = new Command("status", "Ping an environment and show concept counts.");
    var sConn = new ConnectionOptions();
    sConn.AddTo(status);
    status.SetHandler(async ctx =>
    {
        Environment.ExitCode = await StatusCommand.RunAsync(
            ctx.ParseResult.GetValueForOption(sConn.Url)!,
            sConn.ToAuth(ctx),
            ctx.GetCancellationToken()).ConfigureAwait(false);
    });
    status.WithExamples(("Sanity-check a connection", "modelmeister env status --url $URL"));

    // ---- env snapshot (formerly: export)
    var snapshot = new Command("snapshot", "Capture a live model to a JSON file.");
    var snConn = new ConnectionOptions();
    snConn.AddTo(snapshot);
    var snOut = new Option<string>("--out", "Output JSON path") { IsRequired = true };
    snapshot.AddOption(snOut);
    snapshot.SetHandler(async ctx =>
    {
        Environment.ExitCode = await ExportCommand.RunAsync(
            ctx.ParseResult.GetValueForOption(snConn.Url)!,
            snConn.ToAuth(ctx),
            ctx.ParseResult.GetValueForOption(snOut)!,
            ctx.GetCancellationToken()).ConfigureAwait(false);
    });
    snapshot
        .WithExamples(("Save the live model for offline diffing",
                       "modelmeister env snapshot --url $URL --out snap.json"))
        .WithSeeAlso(
            ("env compare", "compare a snapshot to another env"),
            ("excel export", "snapshot to a workbook instead of JSON"));

    // ---- env compare (formerly: compare-envs)
    var compare = new Command("compare", "Compare two environments (or a JSON snapshot vs a live env).");
    var lJson = new Option<string?>("--left-json", "JSON snapshot for the left side");
    var lUrl = new Option<string?>("--left-url", "Live URL for the left side (omit if using --left-json)");
    var lKey = new Option<string?>("--left-api-key", "API key for the left URL");
    var rUrl = new Option<string>("--right-url", "Live URL for the right side") { IsRequired = true };
    var rKey = new Option<string?>("--right-api-key", "API key for the right URL (falls back to INRIVER_API_KEY)");
    var cOut = new Option<string?>("--out", "Write the report to this file");
    var cFormat = new Option<string>("--format", () => "text", "Output format: text | json");
    foreach (var o in new Option[] { lJson, lUrl, lKey, rUrl, rKey, cOut, cFormat })
        compare.AddOption(o);
    compare.SetHandler(async ctx =>
    {
        var lu = ctx.ParseResult.GetValueForOption(lUrl);
        var ru = ctx.ParseResult.GetValueForOption(rUrl)!;
        var lk = ctx.ParseResult.GetValueForOption(lKey);
        var rk = ctx.ParseResult.GetValueForOption(rKey) ?? Environment.GetEnvironmentVariable("INRIVER_API_KEY");
        InriverAuth? lAuth = lu is not null ? new InriverAuth(lk, null, null, null) : null;
        var rAuth = new InriverAuth(rk, null, null, null);

        Environment.ExitCode = await CompareEnvsCommand.RunAsync(
            ctx.ParseResult.GetValueForOption(lJson), lu, lAuth, ru, rAuth,
            ctx.ParseResult.GetValueForOption(cOut) ?? "",
            ctx.ParseResult.GetValueForOption(cFormat) ?? "text",
            ctx.GetCancellationToken()).ConfigureAwait(false);
    });
    compare
        .WithExamples(
            ("Snapshot-vs-live comparison",
             "modelmeister env compare --left-json snap.json --right-url $URL"),
            ("Two live envs",
             "modelmeister env compare --left-url $TEST --right-url $PROD"))
        .WithSeeAlso(("model diff", "compare code to env (not env to env)"));

    // ---- env stats
    var stats = new Command("stats", "Print per-entity-type instance counts (data volume at a glance).");
    var stConn = new ConnectionOptions();
    var stJson = new Option<bool>("--json", "Emit machine-readable JSON output");
    stConn.AddTo(stats);
    stats.AddOption(stJson);
    stats.SetHandler(async ctx =>
    {
        Environment.ExitCode = await StatsCommand.RunAsync(
            ctx.ParseResult.GetValueForOption(stConn.Url)!,
            stConn.ToAuth(ctx),
            ctx.ParseResult.GetValueForOption(stJson),
            ctx.GetCancellationToken()).ConfigureAwait(false);
    });
    stats
        .WithExamples(
            ("How many instances exist per type", "modelmeister env stats --url $URL"),
            ("CI/monitoring: machine-readable", "modelmeister env stats --url $URL --json --no-color"));

    // ---- env changes
    var changes = new Command("changes", "Report whether the env's model changed since a snapshot or timestamp.");
    var chConn = new ConnectionOptions();
    var chSince = new Option<string?>("--since", "ISO-8601 timestamp to check changes since (UTC)");
    var chSnap = new Option<string?>("--snapshot", "JSON snapshot whose capture time is the 'since' instant");
    var chFail = new Option<bool>("--fail-on-changes", () => false, "Exit 1 if the env changed (CI guard against drift)");
    chConn.AddTo(changes);
    foreach (var o in new Option[] { chSince, chSnap, chFail }) changes.AddOption(o);
    changes.SetHandler(async ctx =>
    {
        Environment.ExitCode = await ChangesCommand.RunAsync(
            ctx.ParseResult.GetValueForOption(chConn.Url)!,
            chConn.ToAuth(ctx),
            ctx.ParseResult.GetValueForOption(chSince),
            ctx.ParseResult.GetValueForOption(chSnap),
            ctx.ParseResult.GetValueForOption(chFail),
            ctx.GetCancellationToken()).ConfigureAwait(false);
    });
    changes
        .WithExamples(
            ("Did prod drift since our snapshot?", "modelmeister env changes --url $URL --snapshot approved.json"),
            ("CI gate against drift", "modelmeister env changes --url $URL --snapshot approved.json --fail-on-changes"))
        .WithSeeAlso(("env snapshot", "capture the snapshot to compare against"));

    cmd.AddCommand(status);
    cmd.AddCommand(stats);
    cmd.AddCommand(changes);
    cmd.AddCommand(snapshot);
    cmd.AddCommand(compare);
    return cmd;
}

static Command BuildScaffold()
{
    var cmd = new Command("scaffold", "Generate a starter C# model project from JSON, Excel, or a live env.");
    var src = new SourceOptions(SourceKind.Json, SourceKind.Excel, SourceKind.Url);
    src.AddTo(cmd);
    var outOpt = new Option<string>("--out", () => "./GeneratedModel", "Output directory");
    var ns = new Option<string>("--namespace", () => "Generated.Model", "Root namespace");
    var detect = new Option<bool>("--detect-base-classes", () => true, "Factor shared field-sets into abstract base classes");
    var noCvlValues = new Option<bool>("--no-cvl-values", "Skip emitting CVL values (smaller project, faster build)");
    foreach (var o in new Option[] { outOpt, ns, detect, noCvlValues })
        cmd.AddOption(o);
    cmd.SetHandler(async ctx =>
    {
        ResolvedSource picked;
        try { picked = src.Resolve(ctx); }
        catch (SourceResolutionException ex)
        {
            AnsiConsole.MarkupLine($"[red]{ex.Message.EscapeMarkup()}[/]");
            Environment.ExitCode = ExitCodes.UsageError;
            return;
        }

        var outDir = ctx.ParseResult.GetValueForOption(outOpt) ?? "./GeneratedModel";
        var nsVal = ctx.ParseResult.GetValueForOption(ns) ?? "Generated.Model";
        var detectVal = ctx.ParseResult.GetValueForOption(detect);
        var emitCvls = !ctx.ParseResult.GetValueForOption(noCvlValues);

        Environment.ExitCode = picked.Kind switch
        {
            SourceKind.Json => ScaffoldCommand.Run(picked.Path!, outDir, nsVal, detectVal, emitCvls),
            SourceKind.Excel => ScaffoldCommand.RunFromExcel(picked.Path!, outDir, nsVal, detectVal, emitCvls),
            SourceKind.Url => await ScaffoldCommand.RunFromEnvAsync(
                picked.Url!, picked.Auth!, outDir, nsVal, detectVal,
                ctx.GetCancellationToken(), emitCvls).ConfigureAwait(false),
            _ => ExitCodes.UsageError,
        };
    });
    cmd
        .WithExamples(
            ("From a JSON export",
             "modelmeister scaffold --json export.json --out ./Model --namespace Acme.PimModel"),
            ("From an Excel workbook (round-trip back from `excel export`)",
             "modelmeister scaffold --excel model.xlsx --out ./Model"),
            ("Directly from a live env",
             "modelmeister scaffold --url $URL --out ./Model"))
        .WithSeeAlso(("excel export", "round-trip a model back out to a workbook"));
    return cmd;
}

static Command BuildExcel()
{
    var cmd = new Command("excel", "Excel workbook export and import.");

    // ---- excel export
    var exportCmd = new Command("export", "Write an .xlsx from a JSON file, a live env, or a C# model project.");
    var src = new SourceOptions(SourceKind.Json, SourceKind.Model, SourceKind.Url);
    src.AddTo(exportCmd);
    var outOpt = new Option<string>("--out", "Output .xlsx path") { IsRequired = true };
    exportCmd.AddOption(outOpt);
    exportCmd.SetHandler(async ctx =>
    {
        ResolvedSource picked;
        try { picked = src.Resolve(ctx); }
        catch (SourceResolutionException ex)
        {
            AnsiConsole.MarkupLine($"[red]{ex.Message.EscapeMarkup()}[/]");
            Environment.ExitCode = ExitCodes.UsageError;
            return;
        }
        var o = ctx.ParseResult.GetValueForOption(outOpt)!;
        Environment.ExitCode = picked.Kind switch
        {
            SourceKind.Json => ExcelCommand.ExportFromJson(picked.Path!, o),
            SourceKind.Model => ExcelCommand.ExportFromModel(picked.Path!, o),
            SourceKind.Url => await ExcelCommand.ExportFromEnvAsync(
                picked.Url!, picked.Auth!, o, ctx.GetCancellationToken()).ConfigureAwait(false),
            _ => ExitCodes.UsageError,
        };
    });
    exportCmd
        .WithExamples(
            ("Workbook from a JSON export", "modelmeister excel export --json export.json --out model.xlsx"),
            ("Workbook from a C# model project", "modelmeister excel export --model ./MyModel.csproj --out model.xlsx"),
            ("Workbook from a live env", "modelmeister excel export --url $URL --out model.xlsx"))
        .WithSeeAlso(("scaffold", "use the workbook as a source for a generated project"));

    // ---- excel import
    var importCmd = new Command("import", "Convert an Excel workbook to an inriver JSON export.");
    var excel = new Option<string>("--excel", "Excel workbook path") { IsRequired = true };
    excel.AddAlias("--xlsx");
    var importOut = new Option<string>("--out", "Output JSON path") { IsRequired = true };
    importCmd.AddOption(excel);
    importCmd.AddOption(importOut);
    importCmd.SetHandler((x, o) => Environment.ExitCode = ExcelCommand.ImportToJson(x, o), excel, importOut);
    importCmd
        .WithExamples(("Workbook → JSON", "modelmeister excel import --excel model.xlsx --out model.json"))
        .WithSeeAlso(("scaffold", "skip the JSON detour and scaffold straight from .xlsx"));

    cmd.AddCommand(exportCmd);
    cmd.AddCommand(importCmd);
    return cmd;
}

static Command BuildCvl()
{
    var cmd = new Command("cvl", "CVL value workflows: export, import, sync between environments.");

    // ---- cvl export
    var exportCmd = new Command("export", "Write a per-CVL workbook from a JSON file or a live env.");
    var src = new SourceOptions(SourceKind.Json, SourceKind.Url);
    src.AddTo(exportCmd);
    var outOpt = new Option<string>("--out", "Output .xlsx path") { IsRequired = true };
    exportCmd.AddOption(outOpt);
    exportCmd.SetHandler(async ctx =>
    {
        ResolvedSource picked;
        try { picked = src.Resolve(ctx); }
        catch (SourceResolutionException ex)
        {
            AnsiConsole.MarkupLine($"[red]{ex.Message.EscapeMarkup()}[/]");
            Environment.ExitCode = ExitCodes.UsageError;
            return;
        }
        var o = ctx.ParseResult.GetValueForOption(outOpt)!;
        Environment.ExitCode = picked.Kind == SourceKind.Json
            ? CvlCommand.ExportJson(picked.Path!, o)
            : await CvlCommand.ExportEnvAsync(picked.Url!, picked.Auth!, o, ctx.GetCancellationToken()).ConfigureAwait(false);
    });
    exportCmd
        .WithExamples(
            ("From a JSON snapshot", "modelmeister cvl export --json snap.json --out cvls.xlsx"),
            ("From a live env", "modelmeister cvl export --url $URL --out cvls.xlsx"));

    // ---- cvl import
    var importCmd = new Command("import", "Push CVL values from a workbook to a live env.");
    var excel = new Option<string>("--excel", "Excel workbook path") { IsRequired = true };
    excel.AddAlias("--xlsx");
    var iConn = new ConnectionOptions();
    var allowDeact = new Option<bool>("--allow-deactivate", () => false, "Allow deactivating values that exist in the target but not in the source");
    var dry = new Option<bool>("--dry-run", () => false);
    importCmd.AddOption(excel);
    iConn.AddTo(importCmd);
    importCmd.AddOption(allowDeact);
    importCmd.AddOption(dry);
    importCmd.SetHandler(async ctx =>
    {
        Environment.ExitCode = await CvlCommand.ImportAsync(
            ctx.ParseResult.GetValueForOption(iConn.Url)!,
            iConn.ToAuth(ctx),
            ctx.ParseResult.GetValueForOption(excel)!,
            ctx.ParseResult.GetValueForOption(allowDeact),
            ctx.ParseResult.GetValueForOption(dry),
            ctx.GetCancellationToken()).ConfigureAwait(false);
    });
    importCmd.WithExamples(
        ("Dry-run first", "modelmeister cvl import --excel cvls.xlsx --url $URL --dry-run"),
        ("Apply for real", "modelmeister cvl import --excel cvls.xlsx --url $URL"));

    // ---- cvl sync
    var syncCmd = new Command("sync", "Sync CVL values from a JSON snapshot into a live target env.");
    var srcJson = new Option<string>("--source-json", "Source JSON snapshot (see `env snapshot`)") { IsRequired = true };
    var syConn = new ConnectionOptions();
    var only = new Option<string?>("--cvl", "Sync only this CVL id (otherwise all)");
    var allowDeact2 = new Option<bool>("--allow-deactivate", () => false);
    var dry2 = new Option<bool>("--dry-run", () => false);
    syncCmd.AddOption(srcJson);
    syConn.AddTo(syncCmd);
    syncCmd.AddOption(only);
    syncCmd.AddOption(allowDeact2);
    syncCmd.AddOption(dry2);
    syncCmd.SetHandler(async ctx =>
    {
        Environment.ExitCode = await CvlCommand.SyncAsync(
            ctx.ParseResult.GetValueForOption(srcJson)!,
            ctx.ParseResult.GetValueForOption(syConn.Url)!,
            syConn.ToAuth(ctx),
            ctx.ParseResult.GetValueForOption(only),
            ctx.ParseResult.GetValueForOption(allowDeact2),
            ctx.ParseResult.GetValueForOption(dry2),
            ctx.GetCancellationToken()).ConfigureAwait(false);
    });
    syncCmd.WithExamples(
        ("Promote prod CVL values to staging",
         "modelmeister cvl sync --source-json prod.json --url $STAGING --allow-deactivate"));

    cmd.AddCommand(exportCmd);
    cmd.AddCommand(importCmd);
    cmd.AddCommand(syncCmd);
    return cmd;
}

static Command BuildJson()
{
    var cmd = new Command("json", "JSON-file utilities for inriver model exports.");

    var merge = new Command("merge", "Merge two inriver JSON exports into one.");
    var baseOpt = new Option<string>("--base", "Base (lower-priority) JSON path") { IsRequired = true };
    var overlayOpt = new Option<string>("--overlay", "Overlay (higher-priority) JSON path") { IsRequired = true };
    var outOpt = new Option<string>("--out", "Output JSON path") { IsRequired = true };
    var policy = new Option<string>("--on-conflict", () => "overlay-wins", "Conflict policy: overlay-wins | base-wins | fail");
    foreach (var o in new Option[] { baseOpt, overlayOpt, outOpt, policy })
        merge.AddOption(o);
    merge.SetHandler(
        (b, o, p, pol) => Environment.ExitCode = MergeCommand.Run(b, o, p, pol),
        baseOpt, overlayOpt, outOpt, policy);
    merge.WithExamples(
        ("Apply per-env overlay to a base model",
         "modelmeister json merge --base base.json --overlay env-prod.json --out merged.json"));

    cmd.AddCommand(merge);
    return cmd;
}

static Command BuildUsers()
{
    var cmd = new Command("users", "List, export, and provision inriver users.");

    var listCmd = new Command("list", "List all users.");
    var conn1 = new ConnectionOptions();
    conn1.AddTo(listCmd);
    listCmd.SetHandler(async ctx =>
    {
        Environment.ExitCode = await UsersCommand.ListAsync(
            ctx.ParseResult.GetValueForOption(conn1.Url)!,
            conn1.ToAuth(ctx), ctx.GetCancellationToken()).ConfigureAwait(false);
    });
    listCmd.WithExamples(("Print every user with their roles", "modelmeister users list --url $URL"));

    var templateCmd = new Command("template", "Write a users workbook seeded with the env's users + roles.");
    var conn2 = new ConnectionOptions();
    var tOut = new Option<string>("--out", "Output .xlsx path") { IsRequired = true };
    conn2.AddTo(templateCmd);
    templateCmd.AddOption(tOut);
    templateCmd.SetHandler(async ctx =>
    {
        Environment.ExitCode = await UsersCommand.ExportTemplateAsync(
            ctx.ParseResult.GetValueForOption(conn2.Url)!,
            conn2.ToAuth(ctx),
            ctx.ParseResult.GetValueForOption(tOut)!,
            ctx.GetCancellationToken()).ConfigureAwait(false);
    });
    templateCmd.WithExamples(
        ("Pull current users to edit offline", "modelmeister users template --url $URL --out users.xlsx"));

    var provCmd = new Command("provision", "Provision users from a workbook (REST for create, Remoting for roles).");
    var conn3 = new ConnectionOptions();
    var excel = new Option<string>("--excel", "Excel workbook path") { IsRequired = true };
    excel.AddAlias("--xlsx");
    var restBase = new Option<string?>("--rest-base-url", "Base URL of the inriver REST API (e.g. https://apieuw.productmarketingcloud.com)");
    var restKey = new Option<string?>("--rest-api-key", "REST API key with APIManageUsers permission");
    var dry = new Option<bool>("--dry-run", () => false);
    conn3.AddTo(provCmd);
    foreach (var o in new Option[] { excel, restBase, restKey, dry }) provCmd.AddOption(o);
    provCmd.SetHandler(async ctx =>
    {
        Environment.ExitCode = await UsersCommand.ProvisionAsync(
            ctx.ParseResult.GetValueForOption(conn3.Url)!,
            conn3.ToAuth(ctx),
            ctx.ParseResult.GetValueForOption(restBase),
            ctx.ParseResult.GetValueForOption(restKey),
            ctx.ParseResult.GetValueForOption(excel)!,
            ctx.ParseResult.GetValueForOption(dry),
            ctx.GetCancellationToken()).ConfigureAwait(false);
    });
    provCmd.WithExamples(
        ("Dry-run before pushing",
         "modelmeister users provision --excel users.xlsx --url $URL --rest-base-url $REST --rest-api-key $REST_KEY --dry-run"),
        ("Push for real",
         "modelmeister users provision --excel users.xlsx --url $URL --rest-base-url $REST --rest-api-key $REST_KEY"));

    cmd.AddCommand(listCmd);
    cmd.AddCommand(templateCmd);
    cmd.AddCommand(provCmd);
    return cmd;
}

static Command BuildExtensions()
{
    var cmd = new Command("extensions", "List, start, stop, configure inriver extensions (Connectors).");

    var listCmd = new Command("list", "List all extensions.");
    var conn1 = new ConnectionOptions();
    var rb1 = new Option<string?>("--rest-base-url");
    var rk1 = new Option<string?>("--rest-api-key");
    conn1.AddTo(listCmd);
    listCmd.AddOption(rb1);
    listCmd.AddOption(rk1);
    listCmd.SetHandler(async ctx =>
    {
        Environment.ExitCode = await ExtensionsCommand.ListAsync(
            ctx.ParseResult.GetValueForOption(conn1.Url)!,
            conn1.ToAuth(ctx),
            ctx.ParseResult.GetValueForOption(rb1),
            ctx.ParseResult.GetValueForOption(rk1),
            ctx.GetCancellationToken()).ConfigureAwait(false);
    });
    listCmd.WithExamples(("List extensions and their status", "modelmeister extensions list --url $URL"));

    Command BuildAction(string name, string desc, Func<string, InriverAuth, string?, string?, string, CancellationToken, Task<int>> handler, string example)
    {
        var c = new Command(name, desc);
        var conn = new ConnectionOptions();
        var id = new Option<string>("--id", "Extension id") { IsRequired = true };
        var rb = new Option<string?>("--rest-base-url");
        var rk = new Option<string?>("--rest-api-key");
        conn.AddTo(c);
        c.AddOption(id);
        c.AddOption(rb);
        c.AddOption(rk);
        c.SetHandler(async ctx =>
        {
            Environment.ExitCode = await handler(
                ctx.ParseResult.GetValueForOption(conn.Url)!,
                conn.ToAuth(ctx),
                ctx.ParseResult.GetValueForOption(rb),
                ctx.ParseResult.GetValueForOption(rk),
                ctx.ParseResult.GetValueForOption(id)!,
                ctx.GetCancellationToken()).ConfigureAwait(false);
        });
        c.WithExamples((desc, example));
        return c;
    }

    cmd.AddCommand(listCmd);
    cmd.AddCommand(BuildAction("start", "Start (resume) an extension.", ExtensionsCommand.StartAsync,
        "modelmeister extensions start --url $URL --id MyConnector"));
    cmd.AddCommand(BuildAction("stop", "Stop (pause) an extension.", ExtensionsCommand.StopAsync,
        "modelmeister extensions stop --url $URL --id MyConnector"));

    var logsCmd = new Command("logs", "Show recent events for an extension.");
    var connL = new ConnectionOptions();
    var idL = new Option<string>("--id", "Extension id") { IsRequired = true };
    var count = new Option<int>("--count", () => 50, "Maximum events to print");
    connL.AddTo(logsCmd);
    logsCmd.AddOption(idL);
    logsCmd.AddOption(count);
    logsCmd.SetHandler(async ctx =>
    {
        Environment.ExitCode = await ExtensionsCommand.LogsAsync(
            ctx.ParseResult.GetValueForOption(connL.Url)!,
            connL.ToAuth(ctx),
            ctx.ParseResult.GetValueForOption(idL)!,
            ctx.ParseResult.GetValueForOption(count),
            ctx.GetCancellationToken()).ConfigureAwait(false);
    });
    logsCmd.WithExamples(("Tail recent events", "modelmeister extensions logs --url $URL --id MyConnector --count 200"));
    cmd.AddCommand(logsCmd);

    var setCmd = new Command("set", "Set a single configuration value on an extension.");
    var connS = new ConnectionOptions();
    var idS = new Option<string>("--id", "Extension id") { IsRequired = true };
    var key = new Option<string>("--key", "Setting key") { IsRequired = true };
    var value = new Option<string>("--value", "Setting value") { IsRequired = true };
    connS.AddTo(setCmd);
    setCmd.AddOption(idS);
    setCmd.AddOption(key);
    setCmd.AddOption(value);
    setCmd.SetHandler(async ctx =>
    {
        Environment.ExitCode = await ExtensionsCommand.SetSettingAsync(
            ctx.ParseResult.GetValueForOption(connS.Url)!,
            connS.ToAuth(ctx),
            ctx.ParseResult.GetValueForOption(idS)!,
            ctx.ParseResult.GetValueForOption(key)!,
            ctx.ParseResult.GetValueForOption(value)!,
            ctx.GetCancellationToken()).ConfigureAwait(false);
    });
    setCmd.WithExamples(("Update one setting",
        "modelmeister extensions set --url $URL --id MyConnector --key BatchSize --value 100"));
    cmd.AddCommand(setCmd);

    return cmd;
}

static Command BuildWorkAreas()
{
    var cmd = new Command("workareas", "List, export/import, and promote shared work-area folders + saved queries.");

    var listCmd = new Command("list", "List work-area folders (shared, or a user's personal with --user).");
    var connL = new ConnectionOptions();
    var userL = new Option<string?>("--user", "List this user's personal folders instead of shared.");
    connL.AddTo(listCmd);
    listCmd.AddOption(userL);
    listCmd.SetHandler(async ctx =>
    {
        Environment.ExitCode = await WorkAreasCommand.ListAsync(
            ctx.ParseResult.GetValueForOption(connL.Url)!, connL.ToAuth(ctx),
            ctx.ParseResult.GetValueForOption(userL), ctx.GetCancellationToken()).ConfigureAwait(false);
    });
    listCmd.WithExamples(
        ("List shared folders", "modelmeister workareas list --url $URL"),
        ("List a user's personal folders", "modelmeister workareas list --url $URL --user alice"));

    var exportCmd = new Command("export", "Export work-area folders + queries to an Excel workbook (shared, or --user).");
    var connE = new ConnectionOptions();
    var eOut = new Option<string>("--out", "Output .xlsx path") { IsRequired = true };
    var userE = new Option<string?>("--user", "Export this user's personal folders instead of shared.");
    connE.AddTo(exportCmd);
    exportCmd.AddOption(eOut);
    exportCmd.AddOption(userE);
    exportCmd.SetHandler(async ctx =>
    {
        Environment.ExitCode = await WorkAreasCommand.ExportAsync(
            ctx.ParseResult.GetValueForOption(connE.Url)!, connE.ToAuth(ctx),
            ctx.ParseResult.GetValueForOption(eOut)!,
            ctx.ParseResult.GetValueForOption(userE), ctx.GetCancellationToken()).ConfigureAwait(false);
    });
    exportCmd.WithExamples(("Pull folders to edit/review offline", "modelmeister workareas export --url $URL --out workareas.xlsx"));

    var importCmd = new Command("import", "Apply shared work-area folders from an Excel workbook (matched by path).");
    var connI = new ConnectionOptions();
    var iExcel = new Option<string>("--excel", "Excel workbook path") { IsRequired = true };
    iExcel.AddAlias("--xlsx");
    var iDelete = new Option<bool>("--allow-deletes", () => false, "Delete target folders not present in the workbook");
    var iDry = new Option<bool>("--dry-run", () => false);
    var userI = new Option<string?>("--user", "Apply to this user's personal folders instead of shared.");
    connI.AddTo(importCmd);
    foreach (var o in new Option[] { iExcel, iDelete, iDry, userI }) importCmd.AddOption(o);
    importCmd.SetHandler(async ctx =>
    {
        Environment.ExitCode = await WorkAreasCommand.ImportAsync(
            ctx.ParseResult.GetValueForOption(connI.Url)!, connI.ToAuth(ctx),
            ctx.ParseResult.GetValueForOption(iExcel)!,
            ctx.ParseResult.GetValueForOption(iDelete),
            ctx.ParseResult.GetValueForOption(iDry),
            ctx.ParseResult.GetValueForOption(userI),
            ctx.GetCancellationToken()).ConfigureAwait(false);
    });
    importCmd.WithExamples(("Apply a reviewed workbook", "modelmeister workareas import --url $URL --excel workareas.xlsx"));

    var promoteCmd = new Command("promote", "Copy shared work-area folders (with queries) from one env to another.");
    var connT = new ConnectionOptions(); // target = standard --url/--api-key
    var fromUrl = new Option<string>("--from-url", "Source inriver URL") { IsRequired = true };
    var fromKey = new Option<string?>("--from-api-key", "Source API key (falls back to INRIVER_API_KEY)");
    var pDelete = new Option<bool>("--allow-deletes", () => false, "Delete target folders not present on the source");
    var userP = new Option<string?>("--user", "Promote this user's personal folders instead of shared.");
    connT.AddTo(promoteCmd);
    foreach (var o in new Option[] { fromUrl, fromKey, pDelete, userP }) promoteCmd.AddOption(o);
    promoteCmd.SetHandler(async ctx =>
    {
        Environment.ExitCode = await WorkAreasCommand.PromoteAsync(
            ctx.ParseResult.GetValueForOption(fromUrl)!,
            new InriverAuth(ctx.ParseResult.GetValueForOption(fromKey), null, null, null),
            ctx.ParseResult.GetValueForOption(connT.Url)!,
            connT.ToAuth(ctx),
            ctx.ParseResult.GetValueForOption(pDelete),
            ctx.ParseResult.GetValueForOption(userP),
            ctx.GetCancellationToken()).ConfigureAwait(false);
    });
    promoteCmd.WithExamples(
        ("Promote folders test → prod", "modelmeister workareas promote --from-url $TEST --url $PROD --from-api-key $TEST_KEY --api-key $PROD_KEY"));

    // ---- workareas duplicate
    var duplicateCmd = new Command("duplicate", "Duplicate a folder in place (under its own parent), with a \"(copy)\" name.");
    var connD = new ConnectionOptions();
    var dPath = new Argument<string>("path", "Folder path to duplicate (parent-chain of names, '/'-joined).");
    var dDeep = new Option<bool>("--deep", () => false, "Duplicate the whole subtree (default: just the folder).");
    var userD = new Option<string?>("--user", "Operate on this user's personal folders instead of shared.");
    connD.AddTo(duplicateCmd);
    duplicateCmd.AddArgument(dPath);
    duplicateCmd.AddOption(dDeep);
    duplicateCmd.AddOption(userD);
    duplicateCmd.SetHandler(async ctx =>
    {
        Environment.ExitCode = await WorkAreasCommand.DuplicateAsync(
            ctx.ParseResult.GetValueForOption(connD.Url)!, connD.ToAuth(ctx),
            ctx.ParseResult.GetValueForArgument(dPath),
            ctx.ParseResult.GetValueForOption(dDeep),
            ctx.ParseResult.GetValueForOption(userD),
            ctx.GetCancellationToken()).ConfigureAwait(false);
    });
    duplicateCmd.WithExamples(
        ("Duplicate one folder", "modelmeister workareas duplicate \"Marketing/Launch 2026\" --url $URL"),
        ("Duplicate a whole subtree", "modelmeister workareas duplicate Marketing --deep --url $URL"));

    // ---- workareas copy
    var copyCmd = new Command("copy", "Copy a folder (optionally deep) under a target parent — same scope or cross-scope.");
    var connC = new ConnectionOptions();
    var cSrc = new Argument<string>("sourcePath", "Source folder path.");
    var cDst = new Argument<string>("targetParentPath", "Destination parent path ('' or '/' for the tree root).");
    var cDeep = new Option<bool>("--deep", () => false, "Copy the whole subtree (default: just the folder).");
    var cToShared = new Option<bool>("--to-shared", () => false, "Copy into the shared scope (cross-scope when source is personal).");
    var cToUser = new Option<string?>("--to-user", "Copy into this user's personal scope (cross-scope).");
    var cDry = new Option<bool>("--dry-run", () => false, "Print the plan without writing.");
    var userC = new Option<string?>("--user", "Source scope: this user's personal folders instead of shared.");
    connC.AddTo(copyCmd);
    copyCmd.AddArgument(cSrc);
    copyCmd.AddArgument(cDst);
    foreach (var o in new Option[] { cDeep, cToShared, cToUser, cDry, userC }) copyCmd.AddOption(o);
    copyCmd.SetHandler(async ctx =>
    {
        Environment.ExitCode = await WorkAreasCommand.CopyAsync(
            ctx.ParseResult.GetValueForOption(connC.Url)!, connC.ToAuth(ctx),
            ctx.ParseResult.GetValueForArgument(cSrc),
            ctx.ParseResult.GetValueForArgument(cDst),
            ctx.ParseResult.GetValueForOption(cDeep),
            ctx.ParseResult.GetValueForOption(cToShared),
            ctx.ParseResult.GetValueForOption(cToUser),
            ctx.ParseResult.GetValueForOption(cDry),
            ctx.ParseResult.GetValueForOption(userC),
            ctx.GetCancellationToken()).ConfigureAwait(false);
    });
    copyCmd.WithExamples(
        ("Copy a subtree under another folder", "modelmeister workareas copy Marketing Campaigns --deep --url $URL"),
        ("Copy shared → a user's personal", "modelmeister workareas copy Marketing \"\" --to-user alice --url $URL"),
        ("Preview without writing", "modelmeister workareas copy Marketing Campaigns --deep --dry-run --url $URL"));

    // ---- workareas move
    var moveCmd = new Command("move", "Re-parent a folder under a target parent (same scope).");
    var connM = new ConnectionOptions();
    var mSrc = new Argument<string>("sourcePath", "Source folder path.");
    var mDst = new Argument<string>("targetParentPath", "Destination parent path (root is not supported).");
    var userM = new Option<string?>("--user", "Operate on this user's personal folders instead of shared.");
    connM.AddTo(moveCmd);
    moveCmd.AddArgument(mSrc);
    moveCmd.AddArgument(mDst);
    moveCmd.AddOption(userM);
    moveCmd.SetHandler(async ctx =>
    {
        Environment.ExitCode = await WorkAreasCommand.MoveAsync(
            ctx.ParseResult.GetValueForOption(connM.Url)!, connM.ToAuth(ctx),
            ctx.ParseResult.GetValueForArgument(mSrc),
            ctx.ParseResult.GetValueForArgument(mDst),
            ctx.ParseResult.GetValueForOption(userM),
            ctx.GetCancellationToken()).ConfigureAwait(false);
    });
    moveCmd.WithExamples(
        ("Re-parent a folder", "modelmeister workareas move \"Marketing/Launch 2026\" Campaigns --url $URL"));

    cmd.AddCommand(listCmd);
    cmd.AddCommand(exportCmd);
    cmd.AddCommand(importCmd);
    cmd.AddCommand(promoteCmd);
    cmd.AddCommand(duplicateCmd);
    cmd.AddCommand(copyCmd);
    cmd.AddCommand(moveCmd);
    return cmd;
}

static Command BuildHtmlTemplates()
{
    var cmd = new Command("htmltemplates", "List, export/import, and promote HTML (print / ContentStore) templates.");

    var listCmd = new Command("list", "List HTML templates.");
    var connL = new ConnectionOptions();
    connL.AddTo(listCmd);
    listCmd.SetHandler(async ctx =>
    {
        Environment.ExitCode = await HtmlTemplatesCommand.ListAsync(
            ctx.ParseResult.GetValueForOption(connL.Url)!, connL.ToAuth(ctx), ctx.GetCancellationToken()).ConfigureAwait(false);
    });
    listCmd.WithExamples(("List templates", "modelmeister htmltemplates list --url $URL"));

    var exportCmd = new Command("export", "Export HTML templates to an Excel workbook (oversize bodies spill to a sidecar folder).");
    var connE = new ConnectionOptions();
    var eOut = new Option<string>("--out", "Output .xlsx path") { IsRequired = true };
    connE.AddTo(exportCmd);
    exportCmd.AddOption(eOut);
    exportCmd.SetHandler(async ctx =>
    {
        Environment.ExitCode = await HtmlTemplatesCommand.ExportAsync(
            ctx.ParseResult.GetValueForOption(connE.Url)!, connE.ToAuth(ctx),
            ctx.ParseResult.GetValueForOption(eOut)!, ctx.GetCancellationToken()).ConfigureAwait(false);
    });
    exportCmd.WithExamples(("Pull templates to edit/review offline", "modelmeister htmltemplates export --url $URL --out htmltemplates.xlsx"));

    var importCmd = new Command("import", "Apply HTML templates from an Excel workbook (matched by name + type).");
    var connI = new ConnectionOptions();
    var iExcel = new Option<string>("--excel", "Excel workbook path") { IsRequired = true };
    iExcel.AddAlias("--xlsx");
    var iDelete = new Option<bool>("--allow-deletes", () => false, "Delete target templates not present in the workbook");
    var iDry = new Option<bool>("--dry-run", () => false);
    connI.AddTo(importCmd);
    foreach (var o in new Option[] { iExcel, iDelete, iDry }) importCmd.AddOption(o);
    importCmd.SetHandler(async ctx =>
    {
        Environment.ExitCode = await HtmlTemplatesCommand.ImportAsync(
            ctx.ParseResult.GetValueForOption(connI.Url)!, connI.ToAuth(ctx),
            ctx.ParseResult.GetValueForOption(iExcel)!,
            ctx.ParseResult.GetValueForOption(iDelete),
            ctx.ParseResult.GetValueForOption(iDry),
            ctx.GetCancellationToken()).ConfigureAwait(false);
    });
    importCmd.WithExamples(("Apply a reviewed workbook", "modelmeister htmltemplates import --url $URL --excel htmltemplates.xlsx"));

    var promoteCmd = new Command("promote", "Copy HTML templates from one env to another (matched by name + type).");
    var connT = new ConnectionOptions(); // target = standard --url/--api-key
    var fromUrl = new Option<string>("--from-url", "Source inriver URL") { IsRequired = true };
    var fromKey = new Option<string?>("--from-api-key", "Source API key (falls back to INRIVER_API_KEY)");
    var pDelete = new Option<bool>("--allow-deletes", () => false, "Delete target templates not present on the source");
    connT.AddTo(promoteCmd);
    foreach (var o in new Option[] { fromUrl, fromKey, pDelete }) promoteCmd.AddOption(o);
    promoteCmd.SetHandler(async ctx =>
    {
        Environment.ExitCode = await HtmlTemplatesCommand.PromoteAsync(
            ctx.ParseResult.GetValueForOption(fromUrl)!,
            new InriverAuth(ctx.ParseResult.GetValueForOption(fromKey), null, null, null),
            ctx.ParseResult.GetValueForOption(connT.Url)!,
            connT.ToAuth(ctx),
            ctx.ParseResult.GetValueForOption(pDelete),
            ctx.GetCancellationToken()).ConfigureAwait(false);
    });
    promoteCmd.WithExamples(
        ("Promote templates test → prod", "modelmeister htmltemplates promote --from-url $TEST --url $PROD --from-api-key $TEST_KEY --api-key $PROD_KEY"));

    cmd.AddCommand(listCmd);
    cmd.AddCommand(exportCmd);
    cmd.AddCommand(importCmd);
    cmd.AddCommand(promoteCmd);
    return cmd;
}

static Command BuildWorkflows()
{
    var cmd = new Command("workflows", "Print a cheat-sheet of common command sequences.");
    cmd.SetHandler(() =>
    {
        // Bypass AnsiConsole's wrapping so the cheat-sheet stays column-aligned regardless of terminal width.
        Console.Out.WriteLine("""
            Common workflows

              Daily development
                modelmeister model validate --model ./MyModel.csproj
                modelmeister model diff     --model ./MyModel.csproj --url $URL
                modelmeister model apply    --model ./MyModel.csproj --url $URL --yes --dry-run
                modelmeister model apply    --model ./MyModel.csproj --url $URL --yes

              CI gating
                modelmeister model validate --model ./MyModel.csproj --json --no-color
                modelmeister model diff     --model ./MyModel.csproj --url $URL --format json --out diff.json --fail-on-changes

              Snapshots and comparisons
                modelmeister env snapshot --url $URL --out snap.json
                modelmeister env compare  --left-json snap.json --right-url $URL2
                modelmeister env compare  --left-url $TEST --right-url $PROD

              Data volume and drift
                modelmeister env stats   --url $URL
                modelmeister env changes --url $URL --snapshot approved.json --fail-on-changes

              Whole-model lift-and-shift (inriver-native XML)
                modelmeister model export-xml --url $TEST --out model.xml
                modelmeister model import-xml --url $PROD --in model.xml --yes

              Excel handoff (subject-matter experts edit offline)
                modelmeister excel export --url $URL --out model.xlsx
                # (someone edits model.xlsx)
                modelmeister scaffold --excel model.xlsx --out ./Model

              CVL value workflows
                modelmeister cvl export --url $URL --out cvls.xlsx
                modelmeister cvl import --excel cvls.xlsx --url $URL --dry-run
                modelmeister cvl sync   --source-json prod.json --url $STAGING

              Bootstrapping a new project
                modelmeister scaffold --url $URL --out ./Model --namespace Acme.PimModel

              Run `modelmeister <command> --help` for per-command flags and examples.
            """);
        Environment.ExitCode = ExitCodes.Success;
    });
    return cmd;
}

static Command BuildInteractive()
{
    var cmd = new Command("interactive", "Launch the interactive menu.")
    {
        IsHidden = true, // surfaced via `modelmeister` with no args; not advertised in --help
    };
    cmd.SetHandler(async ctx =>
    {
        Environment.ExitCode = await new InteractiveSession()
            .RunAsync(ctx.GetCancellationToken())
            .ConfigureAwait(false);
    });
    return cmd;
}

static string GetVersion()
{
    var asm = Assembly.GetExecutingAssembly();
    return asm.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion
        ?? asm.GetName().Version?.ToString()
        ?? "0.0.0";
}
