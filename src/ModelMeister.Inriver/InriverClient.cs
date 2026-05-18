using System.ServiceModel;
using inRiver.Remoting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Polly;
using Polly.Retry;

namespace ModelMeister.Inriver;

/// <summary>
/// Thin wrapper around <see cref="RemoteManager"/>. Owns the connection, applies Polly retry
/// to transient transport failures, and serialises writes via a semaphore so two apply runs
/// cannot collide against the same environment.
/// </summary>
public sealed class InriverClient : IDisposable
{
    // Process-wide write serialisation. RemoteManager is a process-wide singleton, so two
    // concurrent writers in the same process would step on each other.
    private static readonly SemaphoreSlim WriteLock = new(1, 1);
    private static readonly object ConnectGate = new();
    private static string? s_activeUrl;

    private readonly ILogger _log;
    private readonly ResiliencePipeline _retry;
    private RemoteManager? _manager;

    /// <summary>Build a client. The connection itself is established by one of the <c>ConnectXxx</c> methods.</summary>
    public InriverClient(string url, ILogger<InriverClient>? log = null)
    {
        Url = url;
        _log = (ILogger?)log ?? NullLogger.Instance;
        _retry = new ResiliencePipelineBuilder()
            .AddRetry(new RetryStrategyOptions
            {
                // Retry policy: transport-level faults ONLY.
                // CommunicationException is the WCF base, but a CommunicationException carrying a
                // FaultException inner is a server-deliberate response (validation, auth, not-found)
                // — deterministic, retrying just delays the inevitable.
                ShouldHandle = new PredicateBuilder()
                    .Handle<TimeoutException>()
                    .Handle<HttpRequestException>()
                    .Handle<CommunicationException>(IsTransientCommunicationException),
                MaxRetryAttempts = 4,
                BackoffType = DelayBackoffType.Exponential,
                Delay = TimeSpan.FromMilliseconds(500),
                OnRetry = args =>
                {
                    _log.LogWarning(args.Outcome.Exception,
                        "Remoting call retrying (attempt {Attempt}, {ExceptionType})",
                        args.AttemptNumber + 1,
                        args.Outcome.Exception?.GetType().Name);
                    return ValueTask.CompletedTask;
                },
            })
            .Build();
    }

    /// <summary>The configured remoting endpoint URL.</summary>
    public string Url { get; }

    /// <summary>The underlying <see cref="RemoteManager"/>. Throws if not yet connected.</summary>
    public RemoteManager Manager =>
        _manager ?? throw new InvalidOperationException("Not connected. Call ConnectAsync first.");

    /// <summary>Establish a connection using an inriver REST API key.</summary>
    public Task ConnectWithApiKeyAsync(string apiKey)
    {
        _log.LogInformation("Connecting to {Url} via API key", Url);
        EnsureNoConflictingProcessWideConnection();
        _manager = RemoteManager.CreateInstance(Url, apiKey);
        return Task.CompletedTask;
    }

    /// <summary>Establish a connection using username/password credentials against a named environment.</summary>
    public Task ConnectWithCredentialsAsync(string username, string password, string environment)
    {
        _log.LogInformation("Connecting to {Url} via username for environment {Env}", Url, environment);
        EnsureNoConflictingProcessWideConnection();
        _manager = RemoteManager.CreateInstance(Url, username, password, environment);
        return Task.CompletedTask;
    }

    /// <summary>
    /// <see cref="RemoteManager.CreateInstance"/> mutates a process-wide singleton
    /// (<see cref="RemoteManager.Instance"/>). Two <see cref="InriverClient"/> instances against
    /// different URLs in the same process would silently step on each other; guard with a clear error.
    /// TODO: long-term, replace with <c>AuthenticateUsingApiKey</c> + per-call <c>AuthenticationTicket</c>
    /// so multiple environments can coexist in one process — invasive (every Read/Write rewires).
    /// </summary>
    private void EnsureNoConflictingProcessWideConnection()
    {
        lock (ConnectGate)
        {
            if (s_activeUrl is { } existing && !string.Equals(existing, Url, StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException(
                    $"RemoteManager is a process-wide singleton already bound to '{existing}'. " +
                    $"Cannot connect a second InriverClient to '{Url}' from the same process. " +
                    "Reuse the existing client, run the second connection in a separate process, " +
                    $"or call {nameof(InriverClient)}.{nameof(ReleaseActiveUrl)} first to rebind.");
            }
            s_activeUrl = Url;
        }
    }

    /// <summary>
    /// Releases the process-wide URL binding so a subsequent <see cref="InriverClient"/> against a
    /// different URL can connect. Use only after disposing the prior client. This is the explicit
    /// switch-env hatch used by features that talk to two environments sequentially (server-settings
    /// promote, cross-env extension compare).
    /// </summary>
    public static void ReleaseActiveUrl()
    {
        lock (ConnectGate)
        {
            s_activeUrl = null;
        }
    }

    /// <summary>Execute a read against the live environment with retry on transient faults.</summary>
    public T Read<T>(Func<RemoteManager, T> call) =>
        _retry.Execute(static state => state.call(state.mgr), (mgr: Manager, call));

    /// <summary>Execute a write against the live environment, serialised against other writers in this process.</summary>
    public async Task<T> WriteAsync<T>(Func<RemoteManager, T> call, CancellationToken ct = default)
    {
        await WriteLock.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            return _retry.Execute(static state => state.call(state.mgr), (mgr: Manager, call));
        }
        finally
        {
            WriteLock.Release();
        }
    }

    /// <summary>
    /// True iff the exception represents a network-level fault (no inner <see cref="FaultException"/>).
    /// Application errors travel as <c>FaultException</c> derivatives and must surface, not be retried.
    /// </summary>
    private static bool IsTransientCommunicationException(CommunicationException ex)
    {
        for (Exception? current = ex; current is not null; current = current.InnerException)
        {
            if (current is FaultException) return false;
        }
        return true;
    }

    /// <summary><see cref="RemoteManager"/> has no explicit dispose hook; nothing to release here.</summary>
    public void Dispose()
    {
    }
}
