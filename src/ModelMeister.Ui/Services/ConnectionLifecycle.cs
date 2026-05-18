using System;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Threading;
using ModelMeister.Inriver;
using ModelMeister.Ui.Models;

namespace ModelMeister.Ui.Services;

/// <summary>Coarse state machine for the single live <see cref="InriverClient"/> the process can hold.</summary>
public enum ConnectionState { Disconnected, Connecting, Connected, Faulted }

/// <summary>
/// Abstraction over the connection lifecycle so view-models can be unit-tested against a stub.
/// </summary>
public interface IConnectionLifecycle
{
    ConnectionState State { get; }
    EnvironmentEntry? Connected { get; }
    InriverClient? Client { get; }
    string? LastError { get; }

    /// <summary>Raised after any transition; subscribers re-read <see cref="State"/>.</summary>
    event Action? Changed;

    /// <summary>Connect to <paramref name="env"/> using <paramref name="secret"/>. Replaces any existing connection.</summary>
    Task ConnectAsync(EnvironmentEntry env, EnvironmentSecret secret, CancellationToken ct = default);

    /// <summary>Tear down the current connection and reset to <see cref="ConnectionState.Disconnected"/>.</summary>
    Task DisconnectAsync();
}

/// <summary>
/// Owns the single live <see cref="InriverClient"/> the process can hold. RemoteManager is a
/// process-wide singleton — connecting to a second URL would throw. This class enforces "at most one"
/// and disposes the prior client before reconnecting.
/// </summary>
public sealed class ConnectionLifecycle : IConnectionLifecycle
{
    private readonly IEnvironmentVault? _vault;

    /// <inheritdoc/>
    public ConnectionState State { get; private set; } = ConnectionState.Disconnected;
    /// <inheritdoc/>
    public EnvironmentEntry? Connected { get; private set; }
    /// <inheritdoc/>
    public InriverClient? Client { get; private set; }
    /// <inheritdoc/>
    public string? LastError { get; private set; }
    /// <inheritdoc/>
    public event Action? Changed;

    public ConnectionLifecycle(IEnvironmentVault? vault = null)
    {
        _vault = vault;
        if (vault is not null) vault.Changed += OnVaultChanged;
    }

    /// <summary>When the vault is mutated (eg. a rename), pick up the new entry instance for the
    /// connected env so all bindings that read <see cref="Connected"/>.Name refresh.</summary>
    private void OnVaultChanged()
    {
        if (Connected is null || _vault is null) return;
        var fresh = _vault.Get(Connected.Id);
        if (fresh is null || ReferenceEquals(fresh, Connected)) return;
        Connected = fresh;
        RaiseChanged();
    }

    /// <summary>
    /// Marshal <see cref="Changed"/> onto the UI thread. Subscribers update Avalonia-bound properties
    /// (and now, post-state-management pass, propagate <c>NotifyCanExecuteChanged</c> into bound
    /// buttons), which require the UI thread. <c>ConnectAsync</c> resumes on the thread-pool after
    /// <c>Task.Run().ConfigureAwait(false)</c>, so a direct invoke would throw "Call from invalid thread".
    /// </summary>
    private void RaiseChanged()
    {
        var handler = Changed;
        if (handler is null) return;
        if (Dispatcher.UIThread.CheckAccess()) handler();
        else Dispatcher.UIThread.Post(handler);
    }

    /// <inheritdoc/>
    public async Task ConnectAsync(EnvironmentEntry env, EnvironmentSecret secret, CancellationToken ct = default)
    {
        await DisconnectAsync().ConfigureAwait(false);
        State = ConnectionState.Connecting;
        Connected = env;
        LastError = null;
        RaiseChanged();

        try
        {
            var client = new InriverClient(env.Url);
            await Task.Run(async () =>
            {
                if (string.IsNullOrWhiteSpace(secret.ApiKey))
                    throw new InvalidOperationException("No API key stored for this environment.");
                await client.ConnectWithApiKeyAsync(secret.ApiKey).ConfigureAwait(false);
                // Touch the service once so we surface auth/transport faults immediately rather than on first use.
                _ = client.Read(m => m.UtilityService.GetAllLanguages());
            }, ct).ConfigureAwait(false);

            Client = client;
            State = ConnectionState.Connected;
        }
        catch (Exception ex)
        {
            LastError = ex.Message;
            State = ConnectionState.Faulted;
            Client = null;
        }

        RaiseChanged();
    }

    /// <inheritdoc/>
    public Task DisconnectAsync()
    {
        Client?.Dispose();
        Client = null;
        Connected = null;
        State = ConnectionState.Disconnected;
        LastError = null;
        // Release the process-wide singleton URL binding so the next ConnectAsync can target a
        // different environment. The RemoteManager itself does not need an explicit teardown;
        // CreateInstance replaces it transparently.
        InriverClient.ReleaseActiveUrl();
        RaiseChanged();
        return Task.CompletedTask;
    }

}
