using System.CommandLine;
using System.CommandLine.Invocation;

namespace ModelMeister.Cli.Commands;

/// <summary>
/// Bundles the inriver connection-related CLI options so command wiring stays terse
/// and the same set of flags is consistently exposed by every command that talks to a live env.
/// </summary>
public sealed class ConnectionOptions
{
    /// <summary>The inriver URL (required).</summary>
    public Option<string> Url { get; } = new("--url", "Inriver URL") { IsRequired = true };

    /// <summary>API key; falls back to the <c>INRIVER_API_KEY</c> environment variable.</summary>
    public Option<string?> ApiKey { get; } = new("--api-key", "Inriver API key (falls back to INRIVER_API_KEY env var)");

    /// <summary>Username for legacy credential-based auth.</summary>
    public Option<string?> Username { get; } = new("--username", "Inriver username");

    /// <summary>Password for legacy credential-based auth.</summary>
    public Option<string?> Password { get; } = new("--password", "Inriver password");

    /// <summary>Optional inriver environment name.</summary>
    public Option<string?> Environment { get; } = new("--environment", "Inriver environment name");

    /// <summary>Registers every option on <paramref name="cmd"/>.</summary>
    public void AddTo(Command cmd)
    {
        foreach (var opt in AllOptions())
            cmd.AddOption(opt);
    }

    /// <summary>Builds an <see cref="InriverAuth"/> from the parsed values on <paramref name="ctx"/>.</summary>
    public InriverAuth ToAuth(InvocationContext ctx) => new(
        ctx.ParseResult.GetValueForOption(ApiKey),
        ctx.ParseResult.GetValueForOption(Username),
        ctx.ParseResult.GetValueForOption(Password),
        ctx.ParseResult.GetValueForOption(Environment));

    private IEnumerable<Option> AllOptions() =>
        [Url, ApiKey, Username, Password, Environment];
}
