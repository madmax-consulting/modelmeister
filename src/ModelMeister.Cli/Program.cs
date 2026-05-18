using System.CommandLine;
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
// (`--no-color` on its own counts as no-args.)
if (args is [] or ["--no-color"])
    return await new InteractiveSession().RunAsync(CancellationToken.None).ConfigureAwait(false);

// --- Root + per-command wiring ----------------------------------------------
var root = new RootCommand(
    "ModelMeister — inriver model management CLI. " +
    "Exit codes: 0=success, 1=changes pending, 2=usage error, 3=validation failed, 4=operation failed.");

root.AddGlobalOption(new Option<bool>("--no-color", "Disable ANSI colour output (also: NO_COLOR env var)"));

root.AddCommand(BuildScaffold());
root.AddCommand(BuildValidate());
root.AddCommand(BuildDescribe());
root.AddCommand(BuildStatus());
root.AddCommand(BuildDiff());
root.AddCommand(BuildApply());
root.AddCommand(BuildExport());
root.AddCommand(BuildMerge());
root.AddCommand(BuildExcel());
root.AddCommand(BuildCvl());
root.AddCommand(BuildCompare());
root.AddCommand(BuildUsers());
root.AddCommand(BuildExtensions());
root.AddCommand(BuildInteractive());

return await root.InvokeAsync(args).ConfigureAwait(false);

// --- Command builders --------------------------------------------------------
// Each builder is a local function so wiring stays declarative; the handlers
// store their exit code in Environment.ExitCode (the System.CommandLine idiom).

static Command BuildScaffold()
{
    var cmd = new Command("scaffold", "Generate a starter C# model project from an inriver JSON export, Excel workbook, or live environment.");

    var json = new Option<string?>("--json", "Path to inriver export JSON");
    var excel = new Option<string?>("--excel", "Path to an Excel workbook produced by `excel export`");
    var url = new Option<string?>("--url", "Inriver URL (fetches the live model via the Remoting API)");
    var apiKey = new Option<string?>("--api-key", "Inriver API key (falls back to INRIVER_API_KEY env var)");
    var user = new Option<string?>("--username", "Inriver username");
    var pw = new Option<string?>("--password", "Inriver password");
    var env = new Option<string?>("--environment", "Inriver environment name");
    var outOpt = new Option<string>("--out", () => "./GeneratedModel", "Output directory");
    var ns = new Option<string>("--namespace", () => "Generated.Model", "Root namespace");
    var detect = new Option<bool>("--detect-base-classes", () => true, "Detect shared field sets and emit abstract base classes");
    var noCvlValues = new Option<bool>("--no-cvl-values", "Skip emitting CVL values (the generated Cvl classes inherit the empty default)");

    foreach (var opt in new Option[] { json, excel, url, apiKey, user, pw, env, outOpt, ns, detect, noCvlValues })
        cmd.AddOption(opt);

    cmd.SetHandler(async ctx =>
    {
        var jsonVal = ctx.ParseResult.GetValueForOption(json);
        var excelVal = ctx.ParseResult.GetValueForOption(excel);
        var urlVal = ctx.ParseResult.GetValueForOption(url);
        var outVal = ctx.ParseResult.GetValueForOption(outOpt) ?? "./GeneratedModel";
        var nsVal = ctx.ParseResult.GetValueForOption(ns) ?? "Generated.Model";
        var detectVal = ctx.ParseResult.GetValueForOption(detect);
        var emitCvlValues = !ctx.ParseResult.GetValueForOption(noCvlValues);

        var sources = new[] { jsonVal, excelVal, urlVal }.Count(s => !string.IsNullOrEmpty(s));
        if (sources != 1)
        {
            AnsiConsole.MarkupLine("[red]Specify exactly one of --json, --excel, or --url.[/]");
            Environment.ExitCode = ExitCodes.UsageError;
            return;
        }

        if (!string.IsNullOrEmpty(jsonVal))
        {
            Environment.ExitCode = ScaffoldCommand.Run(jsonVal, outVal, nsVal, detectVal, emitCvlValues);
            return;
        }
        if (!string.IsNullOrEmpty(excelVal))
        {
            Environment.ExitCode = ScaffoldCommand.RunFromExcel(excelVal, outVal, nsVal, detectVal, emitCvlValues);
            return;
        }

        var auth = new InriverAuth(
            ctx.ParseResult.GetValueForOption(apiKey),
            ctx.ParseResult.GetValueForOption(user),
            ctx.ParseResult.GetValueForOption(pw),
            ctx.ParseResult.GetValueForOption(env));
        Environment.ExitCode = await ScaffoldCommand
            .RunFromEnvAsync(urlVal!, auth, outVal, nsVal, detectVal, ctx.GetCancellationToken(), emitCvlValues)
            .ConfigureAwait(false);
    });

    return cmd;
}

static Command BuildValidate()
{
    var cmd = new Command("validate", "Statically validate a code-defined model.");
    var model = new Option<string>("--model", "Path to model DLL or csproj") { IsRequired = true };
    var json = new Option<bool>("--json", "Emit machine-readable JSON output");
    cmd.AddOption(model);
    cmd.AddOption(json);
    cmd.SetHandler((m, j) => Environment.ExitCode = ValidateCommand.Run(m, j), model, json);
    return cmd;
}

static Command BuildDescribe()
{
    var cmd = new Command("describe", "Print a summary of a code-defined model.");
    var model = new Option<string>("--model", "Path to model DLL or csproj") { IsRequired = true };
    var json = new Option<bool>("--json", "Emit machine-readable JSON output");
    cmd.AddOption(model);
    cmd.AddOption(json);
    cmd.SetHandler((m, j) => Environment.ExitCode = DescribeCommand.Run(m, j), model, json);
    return cmd;
}

static Command BuildStatus()
{
    var cmd = new Command("status", "Ping an inriver environment and show concept counts.");
    var conn = new ConnectionOptions();
    conn.AddTo(cmd);
    cmd.SetHandler(async ctx =>
    {
        var url = ctx.ParseResult.GetValueForOption(conn.Url)!;
        Environment.ExitCode = await StatusCommand
            .RunAsync(url, conn.ToAuth(ctx), ctx.GetCancellationToken())
            .ConfigureAwait(false);
    });
    return cmd;
}

static Command BuildDiff()
{
    var cmd = new Command("diff", "Diff a code-defined model against a live inriver environment.");
    var model = new Option<string>("--model", "Path to model DLL or csproj") { IsRequired = true };
    var conn = new ConnectionOptions();
    var format = new Option<string>("--format", () => "tree", "Output format: tree | text | json");
    var outOpt = new Option<string?>("--out", "Also write the diff to this file (text or json based on --format)");
    var fail = new Option<bool>("--fail-on-changes", () => false, "Exit with code 1 if any changes are detected (for CI gating)");
    var allowDeletes = new Option<bool>("--allow-deletes", () => false);
    var allowDt = new Option<bool>("--allow-datatype-change", () => false);

    cmd.AddOption(model);
    conn.AddTo(cmd);
    foreach (var opt in new Option[] { format, outOpt, fail, allowDeletes, allowDt })
        cmd.AddOption(opt);

    cmd.SetHandler(async ctx =>
    {
        var policy = MergePolicy.Default with
        {
            AllowDeletes = ctx.ParseResult.GetValueForOption(allowDeletes),
            AllowDatatypeChange = ctx.ParseResult.GetValueForOption(allowDt),
        };
        Environment.ExitCode = await DiffCommand.RunAsync(
            ctx.ParseResult.GetValueForOption(model)!,
            ctx.ParseResult.GetValueForOption(conn.Url)!,
            conn.ToAuth(ctx),
            ctx.ParseResult.GetValueForOption(format) ?? "tree",
            ctx.ParseResult.GetValueForOption(outOpt),
            ctx.ParseResult.GetValueForOption(fail),
            policy,
            ctx.GetCancellationToken()).ConfigureAwait(false);
    });

    return cmd;
}

static Command BuildApply()
{
    var cmd = new Command("apply", "Apply a code-defined model to an inriver environment.");
    var model = new Option<string>("--model", "Path to model DLL or csproj") { IsRequired = true };
    var conn = new ConnectionOptions();
    var yes = new Option<bool>("--yes", () => false, "Skip the confirmation prompt");
    var dry = new Option<bool>("--dry-run", () => false, "Report what would change but don't write");
    var allowDeletes = new Option<bool>("--allow-deletes", () => false);
    var allowDt = new Option<bool>("--allow-datatype-change", () => false);
    var allowRename = new Option<bool>("--allow-cvl-value-rename", () => false);

    cmd.AddOption(model);
    conn.AddTo(cmd);
    foreach (var opt in new Option[] { yes, dry, allowDeletes, allowDt, allowRename })
        cmd.AddOption(opt);

    cmd.SetHandler(async ctx =>
    {
        var policy = MergePolicy.Default with
        {
            AllowDeletes = ctx.ParseResult.GetValueForOption(allowDeletes),
            AllowDatatypeChange = ctx.ParseResult.GetValueForOption(allowDt),
            AllowCvlValueRename = ctx.ParseResult.GetValueForOption(allowRename),
        };
        Environment.ExitCode = await ApplyCommand.RunAsync(
            ctx.ParseResult.GetValueForOption(model)!,
            ctx.ParseResult.GetValueForOption(conn.Url)!,
            conn.ToAuth(ctx),
            ctx.ParseResult.GetValueForOption(yes),
            ctx.ParseResult.GetValueForOption(dry),
            policy,
            ctx.GetCancellationToken()).ConfigureAwait(false);
    });

    return cmd;
}

static Command BuildExport()
{
    var cmd = new Command("export", "Snapshot a live inriver model to JSON.");
    var conn = new ConnectionOptions();
    conn.AddTo(cmd);
    var outOpt = new Option<string>("--out", "Output JSON path") { IsRequired = true };
    cmd.AddOption(outOpt);
    cmd.SetHandler(async ctx =>
    {
        Environment.ExitCode = await ExportCommand.RunAsync(
            ctx.ParseResult.GetValueForOption(conn.Url)!,
            conn.ToAuth(ctx),
            ctx.ParseResult.GetValueForOption(outOpt)!,
            ctx.GetCancellationToken()).ConfigureAwait(false);
    });
    return cmd;
}

static Command BuildMerge()
{
    var cmd = new Command("merge", "Merge two inriver JSON exports into one.");
    var baseOpt = new Option<string>("--base", "Base (lower-priority) JSON path") { IsRequired = true };
    var overlayOpt = new Option<string>("--overlay", "Overlay (higher-priority) JSON path") { IsRequired = true };
    var outOpt = new Option<string>("--out", "Output JSON path") { IsRequired = true };
    var policy = new Option<string>("--on-conflict", () => "overlay-wins", "Conflict policy: overlay-wins | base-wins | fail");

    foreach (var opt in new Option[] { baseOpt, overlayOpt, outOpt, policy })
        cmd.AddOption(opt);

    cmd.SetHandler(
        (b, o, p, pol) => Environment.ExitCode = MergeCommand.Run(b, o, p, pol),
        baseOpt, overlayOpt, outOpt, policy);

    return cmd;
}

static Command BuildInteractive()
{
    var cmd = new Command("interactive", "Launch the interactive menu.");
    cmd.SetHandler(async ctx =>
    {
        Environment.ExitCode = await new InteractiveSession()
            .RunAsync(ctx.GetCancellationToken())
            .ConfigureAwait(false);
    });
    return cmd;
}

// --- Excel ------------------------------------------------------------------
static Command BuildExcel()
{
    var cmd = new Command("excel", "Excel workbook export/import for the inriver model.");

    var exportCmd = new Command("export", "Write a workbook from a JSON export or a live environment.");
    var jsonOpt = new Option<string?>("--json", "Source JSON file");
    var outOpt = new Option<string>("--out", "Output .xlsx path") { IsRequired = true };
    var conn = new ConnectionOptions();
    exportCmd.AddOption(jsonOpt);
    exportCmd.AddOption(outOpt);
    conn.AddTo(exportCmd);
    exportCmd.SetHandler(async ctx =>
    {
        var j = ctx.ParseResult.GetValueForOption(jsonOpt);
        var o = ctx.ParseResult.GetValueForOption(outOpt)!;
        var url = ctx.ParseResult.GetValueForOption(conn.Url);
        if (!string.IsNullOrEmpty(j) && string.IsNullOrEmpty(url))
            Environment.ExitCode = ExcelCommand.ExportFromJson(j, o);
        else if (!string.IsNullOrEmpty(url))
            Environment.ExitCode = await ExcelCommand.ExportFromEnvAsync(url, conn.ToAuth(ctx), o, ctx.GetCancellationToken()).ConfigureAwait(false);
        else
        {
            AnsiConsole.MarkupLine("[red]Specify --json or --url.[/]");
            Environment.ExitCode = ExitCodes.UsageError;
        }
    });

    var importCmd = new Command("import", "Convert an Excel workbook to an inriver JSON export.");
    var xlsxOpt = new Option<string>("--xlsx", "Excel workbook path") { IsRequired = true };
    var importOut = new Option<string>("--out", "Output JSON path") { IsRequired = true };
    importCmd.AddOption(xlsxOpt);
    importCmd.AddOption(importOut);
    importCmd.SetHandler((x, o) => Environment.ExitCode = ExcelCommand.ImportToJson(x, o), xlsxOpt, importOut);

    cmd.AddCommand(exportCmd);
    cmd.AddCommand(importCmd);
    return cmd;
}

// --- CVL --------------------------------------------------------------------
static Command BuildCvl()
{
    var cmd = new Command("cvl", "CVL value workflows: export, import, sync between environments.");

    var exportCmd = new Command("export", "Write a per-CVL workbook from a JSON export or a live env.");
    var jsonOpt = new Option<string?>("--json", "Source JSON file");
    var outOpt = new Option<string>("--out", "Output .xlsx path") { IsRequired = true };
    var conn1 = new ConnectionOptions();
    exportCmd.AddOption(jsonOpt);
    exportCmd.AddOption(outOpt);
    conn1.AddTo(exportCmd);
    exportCmd.SetHandler(async ctx =>
    {
        var j = ctx.ParseResult.GetValueForOption(jsonOpt);
        var o = ctx.ParseResult.GetValueForOption(outOpt)!;
        var url = ctx.ParseResult.GetValueForOption(conn1.Url);
        if (!string.IsNullOrEmpty(j) && string.IsNullOrEmpty(url))
            Environment.ExitCode = CvlCommand.ExportJson(j, o);
        else if (!string.IsNullOrEmpty(url))
            Environment.ExitCode = await CvlCommand.ExportEnvAsync(url, conn1.ToAuth(ctx), o, ctx.GetCancellationToken()).ConfigureAwait(false);
        else
        {
            AnsiConsole.MarkupLine("[red]Specify --json or --url.[/]");
            Environment.ExitCode = ExitCodes.UsageError;
        }
    });

    var importCmd = new Command("import", "Push CVL values from a workbook to a live env.");
    var xlsxOpt = new Option<string>("--xlsx", "Workbook path") { IsRequired = true };
    var conn2 = new ConnectionOptions();
    var allowDeactivate = new Option<bool>("--allow-deactivate", () => false, "Allow deactivating values that exist in target but not in source.");
    var dry = new Option<bool>("--dry-run", () => false);
    importCmd.AddOption(xlsxOpt);
    conn2.AddTo(importCmd);
    importCmd.AddOption(allowDeactivate);
    importCmd.AddOption(dry);
    importCmd.SetHandler(async ctx =>
    {
        Environment.ExitCode = await CvlCommand.ImportAsync(
            ctx.ParseResult.GetValueForOption(conn2.Url)!,
            conn2.ToAuth(ctx),
            ctx.ParseResult.GetValueForOption(xlsxOpt)!,
            ctx.ParseResult.GetValueForOption(allowDeactivate),
            ctx.ParseResult.GetValueForOption(dry),
            ctx.GetCancellationToken()).ConfigureAwait(false);
    });

    var syncCmd = new Command("sync", "Sync CVL values from a JSON snapshot to a live target env.");
    var srcJson = new Option<string>("--source-json", "Source JSON snapshot (use 'modelmeister export' to capture)") { IsRequired = true };
    var conn3 = new ConnectionOptions();
    var only = new Option<string?>("--cvl", "Sync only this CVL id (otherwise all)");
    var allow2 = new Option<bool>("--allow-deactivate", () => false);
    var dry2 = new Option<bool>("--dry-run", () => false);
    syncCmd.AddOption(srcJson);
    conn3.AddTo(syncCmd);
    syncCmd.AddOption(only);
    syncCmd.AddOption(allow2);
    syncCmd.AddOption(dry2);
    syncCmd.SetHandler(async ctx =>
    {
        Environment.ExitCode = await CvlCommand.SyncAsync(
            ctx.ParseResult.GetValueForOption(srcJson)!,
            ctx.ParseResult.GetValueForOption(conn3.Url)!,
            conn3.ToAuth(ctx),
            ctx.ParseResult.GetValueForOption(only),
            ctx.ParseResult.GetValueForOption(allow2),
            ctx.ParseResult.GetValueForOption(dry2),
            ctx.GetCancellationToken()).ConfigureAwait(false);
    });

    cmd.AddCommand(exportCmd);
    cmd.AddCommand(importCmd);
    cmd.AddCommand(syncCmd);
    return cmd;
}

// --- Compare envs -----------------------------------------------------------
static Command BuildCompare()
{
    var cmd = new Command("compare-envs", "Compare two inriver environments (or a JSON snapshot vs a live env).");

    var leftJson = new Option<string?>("--left-json", "JSON snapshot for the left side");
    var leftUrl = new Option<string?>("--left-url", "Live URL for the left side (omit if using --left-json)");
    var leftKey = new Option<string?>("--left-api-key", "API key for the left URL");
    var rightUrl = new Option<string>("--right-url", "Live URL for the right side") { IsRequired = true };
    var rightKey = new Option<string?>("--right-api-key", "API key for the right URL (falls back to INRIVER_API_KEY)");
    var outOpt = new Option<string?>("--out", "Write the report to this file");
    var format = new Option<string>("--format", () => "text", "Output format: text | json");

    foreach (var opt in new Option[] { leftJson, leftUrl, leftKey, rightUrl, rightKey, outOpt, format })
        cmd.AddOption(opt);

    cmd.SetHandler(async ctx =>
    {
        var lj = ctx.ParseResult.GetValueForOption(leftJson);
        var lu = ctx.ParseResult.GetValueForOption(leftUrl);
        var lk = ctx.ParseResult.GetValueForOption(leftKey);
        var ru = ctx.ParseResult.GetValueForOption(rightUrl)!;
        var rk = ctx.ParseResult.GetValueForOption(rightKey) ?? Environment.GetEnvironmentVariable("INRIVER_API_KEY");

        InriverAuth? lAuth = lu is not null ? new InriverAuth(lk, null, null, null) : null;
        var rAuth = new InriverAuth(rk, null, null, null);

        Environment.ExitCode = await CompareEnvsCommand.RunAsync(
            lj, lu, lAuth, ru, rAuth,
            ctx.ParseResult.GetValueForOption(outOpt) ?? "",
            ctx.ParseResult.GetValueForOption(format) ?? "text",
            ctx.GetCancellationToken()).ConfigureAwait(false);
    });
    return cmd;
}

// --- Users ------------------------------------------------------------------
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

    var exportCmd = new Command("export-template", "Write a users workbook seeded with the env's users + roles.");
    var conn2 = new ConnectionOptions();
    var outOpt = new Option<string>("--out", "Output .xlsx path") { IsRequired = true };
    conn2.AddTo(exportCmd);
    exportCmd.AddOption(outOpt);
    exportCmd.SetHandler(async ctx =>
    {
        Environment.ExitCode = await UsersCommand.ExportTemplateAsync(
            ctx.ParseResult.GetValueForOption(conn2.Url)!,
            conn2.ToAuth(ctx),
            ctx.ParseResult.GetValueForOption(outOpt)!,
            ctx.GetCancellationToken()).ConfigureAwait(false);
    });

    var provCmd = new Command("provision", "Provision users from an Excel workbook (REST for create, Remoting for role assignment).");
    var conn3 = new ConnectionOptions();
    var xlsx = new Option<string>("--excel", "Excel workbook path") { IsRequired = true };
    var restBase = new Option<string?>("--rest-base-url", "Base URL of the inriver REST API (e.g. https://apieuw.productmarketingcloud.com)");
    var restKey = new Option<string?>("--rest-api-key", "REST API key with APIManageUsers permission");
    var dry = new Option<bool>("--dry-run", () => false);
    conn3.AddTo(provCmd);
    foreach (var opt in new Option[] { xlsx, restBase, restKey, dry }) provCmd.AddOption(opt);
    provCmd.SetHandler(async ctx =>
    {
        Environment.ExitCode = await UsersCommand.ProvisionAsync(
            ctx.ParseResult.GetValueForOption(conn3.Url)!,
            conn3.ToAuth(ctx),
            ctx.ParseResult.GetValueForOption(restBase),
            ctx.ParseResult.GetValueForOption(restKey),
            ctx.ParseResult.GetValueForOption(xlsx)!,
            ctx.ParseResult.GetValueForOption(dry),
            ctx.GetCancellationToken()).ConfigureAwait(false);
    });

    cmd.AddCommand(listCmd);
    cmd.AddCommand(exportCmd);
    cmd.AddCommand(provCmd);
    return cmd;
}

// --- Extensions -------------------------------------------------------------
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

    Command BuildAction(string name, string desc, Func<string, InriverAuth, string?, string?, string, CancellationToken, Task<int>> handler)
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
        return c;
    }

    cmd.AddCommand(listCmd);
    cmd.AddCommand(BuildAction("start", "Start (resume) an extension.", ExtensionsCommand.StartAsync));
    cmd.AddCommand(BuildAction("stop",  "Stop (pause) an extension.",  ExtensionsCommand.StopAsync));

    var logsCmd = new Command("logs", "Show recent events for an extension.");
    var connL = new ConnectionOptions();
    var idL = new Option<string>("--id", "Extension id") { IsRequired = true };
    var count = new Option<int>("--count", () => 50);
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
    cmd.AddCommand(setCmd);

    return cmd;
}
