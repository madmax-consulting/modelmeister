using System.CommandLine;
using System.CommandLine.Invocation;

namespace ModelMeister.Cli.Commands;

/// <summary>The kinds of model source a command can accept.</summary>
public enum SourceKind
{
    /// <summary>An inriver model export as a JSON file.</summary>
    Json,
    /// <summary>An Excel workbook produced by <c>excel export</c>.</summary>
    Excel,
    /// <summary>A C# model project — csproj, pre-built DLL, or directory containing a csproj.</summary>
    Model,
    /// <summary>A live inriver environment reached over the Remoting API.</summary>
    Url,
}

/// <summary>
/// Bundles the source-picker options every multi-source command needs. Centralises the
/// "pick exactly one of N" validation that was previously duplicated across
/// <c>scaffold</c>, <c>excel export</c>, and <c>cvl export</c>.
/// </summary>
/// <example>
/// <code>
/// var src = new SourceOptions(SourceKind.Json, SourceKind.Excel, SourceKind.Url);
/// src.AddTo(cmd);
/// cmd.SetHandler(async ctx =>
/// {
///     var picked = src.Resolve(ctx);
///     // picked.Kind, picked.Path, picked.Url, picked.Auth
/// });
/// </code>
/// </example>
public sealed class SourceOptions
{
    private readonly HashSet<SourceKind> _allowed;

    /// <summary>Source JSON file (when <see cref="SourceKind.Json"/> is allowed).</summary>
    public Option<string?> Json { get; } = new("--json", "Source: inriver model JSON export");

    /// <summary>Source Excel workbook (when <see cref="SourceKind.Excel"/> is allowed).</summary>
    public Option<string?> Excel { get; }

    /// <summary>Source C# model project (when <see cref="SourceKind.Model"/> is allowed).</summary>
    public Option<string?> Model { get; } = new("--model", "Source: C# model project (csproj, dll, or directory)");

    /// <summary>Connection options exposing <c>--url</c> + auth (when <see cref="SourceKind.Url"/> is allowed).</summary>
    public ConnectionOptions Connection { get; } = new();

    public SourceOptions(params SourceKind[] allowed)
    {
        if (allowed.Length < 2)
            throw new ArgumentException("Source picker needs at least two source kinds.", nameof(allowed));
        _allowed = [.. allowed];

        // The Excel flag accepts --xlsx as an alias so existing muscle memory keeps working.
        Excel = new Option<string?>("--excel", "Source: Excel workbook (.xlsx)");
        Excel.AddAlias("--xlsx");

        // When Url is one of several sources, it must not be globally required.
        if (_allowed.Contains(SourceKind.Url))
            Connection.Url.IsRequired = false;
    }

    /// <summary>Register every allowed option on <paramref name="cmd"/>.</summary>
    public void AddTo(Command cmd)
    {
        if (_allowed.Contains(SourceKind.Json)) cmd.AddOption(Json);
        if (_allowed.Contains(SourceKind.Excel)) cmd.AddOption(Excel);
        if (_allowed.Contains(SourceKind.Model)) cmd.AddOption(Model);
        if (_allowed.Contains(SourceKind.Url)) Connection.AddTo(cmd);
    }

    /// <summary>
    /// Resolve the parsed source. Throws <see cref="SourceResolutionException"/> when the user
    /// did not provide exactly one source flag — the caller should catch and exit
    /// <see cref="ExitCodes.UsageError"/>.
    /// </summary>
    public ResolvedSource Resolve(InvocationContext ctx)
    {
        string? j = _allowed.Contains(SourceKind.Json) ? ctx.ParseResult.GetValueForOption(Json) : null;
        string? e = _allowed.Contains(SourceKind.Excel) ? ctx.ParseResult.GetValueForOption(Excel) : null;
        string? m = _allowed.Contains(SourceKind.Model) ? ctx.ParseResult.GetValueForOption(Model) : null;
        string? u = _allowed.Contains(SourceKind.Url) ? ctx.ParseResult.GetValueForOption(Connection.Url) : null;

        var supplied = new[] { j, e, m, u }.Count(s => !string.IsNullOrEmpty(s));
        if (supplied != 1)
            throw new SourceResolutionException(AllowedFlags());

        if (!string.IsNullOrEmpty(j)) return new ResolvedSource(SourceKind.Json, j, null, null);
        if (!string.IsNullOrEmpty(e)) return new ResolvedSource(SourceKind.Excel, e, null, null);
        if (!string.IsNullOrEmpty(m)) return new ResolvedSource(SourceKind.Model, m, null, null);
        return new ResolvedSource(SourceKind.Url, null, u, Connection.ToAuth(ctx));
    }

    private string AllowedFlags()
    {
        var flags = new List<string>();
        if (_allowed.Contains(SourceKind.Json)) flags.Add("--json");
        if (_allowed.Contains(SourceKind.Excel)) flags.Add("--excel");
        if (_allowed.Contains(SourceKind.Model)) flags.Add("--model");
        if (_allowed.Contains(SourceKind.Url)) flags.Add("--url");
        return string.Join(", ", flags);
    }
}

/// <summary>The resolved source picked from a <see cref="SourceOptions"/>.</summary>
public sealed record ResolvedSource(SourceKind Kind, string? Path, string? Url, InriverAuth? Auth);

/// <summary>Thrown by <see cref="SourceOptions.Resolve"/> when 0 or &gt;1 source flags were supplied.</summary>
public sealed class SourceResolutionException(string allowedFlags)
    : Exception($"Specify exactly one of: {allowedFlags}.")
{
    public string AllowedFlags { get; } = allowedFlags;
}
