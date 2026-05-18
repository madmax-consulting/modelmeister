using Spectre.Console;
using ModelMeister.Inriver;

namespace ModelMeister.Cli.Commands;

/// <summary>
/// Captures the credentials supplied on the command line for an inriver connection.
/// Either an API key or a username/password pair is required; the API key falls back
/// to the <c>INRIVER_API_KEY</c> environment variable so CI pipelines can avoid leaking
/// secrets through command lines.
/// </summary>
/// <param name="ApiKey">Explicit API key, or <c>null</c> to fall back to env var.</param>
/// <param name="Username">Username (used together with <paramref name="Password"/>).</param>
/// <param name="Password">Password.</param>
/// <param name="Environment">Optional inriver environment name (defaults to empty).</param>
public sealed record InriverAuth(string? ApiKey, string? Username, string? Password, string? Environment)
{
    private const string ApiKeyEnvVar = "INRIVER_API_KEY";

    /// <summary>Builds an <see cref="InriverAuth"/> that only resolves <c>INRIVER_API_KEY</c>.</summary>
    public static InriverAuth FromEnv() => new(System.Environment.GetEnvironmentVariable(ApiKeyEnvVar), null, null, null);

    /// <summary>
    /// Connects <paramref name="client"/> using whichever credential set was supplied.
    /// Returns <see cref="ExitCodes.Success"/> on success, <see cref="ExitCodes.UsageError"/> when
    /// nothing usable was provided.
    /// </summary>
    public async Task<int> ConnectAsync(InriverClient client)
    {
        var apiKey = ApiKey ?? System.Environment.GetEnvironmentVariable(ApiKeyEnvVar);

        if (!string.IsNullOrEmpty(apiKey))
        {
            await client.ConnectWithApiKeyAsync(apiKey).ConfigureAwait(false);
            return ExitCodes.Success;
        }

        if (!string.IsNullOrEmpty(Username) && Password is not null)
        {
            await client.ConnectWithCredentialsAsync(Username, Password, Environment ?? string.Empty).ConfigureAwait(false);
            return ExitCodes.Success;
        }

        AnsiConsole.MarkupLine("[red]Need either --api-key (or INRIVER_API_KEY env var) or --username/--password.[/]");
        return ExitCodes.UsageError;
    }
}
