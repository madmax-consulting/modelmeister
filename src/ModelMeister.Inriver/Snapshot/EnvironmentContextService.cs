using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace ModelMeister.Inriver.Snapshot;

/// <summary>Identifying context for a connected environment — which customer, which env, which stack.</summary>
public sealed record EnvironmentContextInfo(string Customer, string EnvironmentName, string Stack)
{
    /// <summary>True when nothing identifying came back.</summary>
    public bool IsEmpty => string.IsNullOrEmpty(Customer) && string.IsNullOrEmpty(EnvironmentName) && string.IsNullOrEmpty(Stack);

    /// <summary>Compact label, e.g. "acme · prod · euw". Empty parts are dropped.</summary>
    public string Label()
    {
        var parts = new[] { Customer, EnvironmentName, Stack }.Where(p => !string.IsNullOrWhiteSpace(p));
        return string.Join(" · ", parts);
    }
}

/// <summary>
/// Reads inriver's <c>GetEnvironmentContextAsync</c> — the customer / environment / stack identity of
/// the connection. Surfaced at the apply gate so the operator can confirm they're about to mutate the
/// environment they think they are (the "wrong env" apply is the costliest mistake in model mgmt).
/// </summary>
public sealed class EnvironmentContextService
{
    private readonly InriverClient _remoting;
    private readonly ILogger _log;

    public EnvironmentContextService(InriverClient remoting, ILogger<EnvironmentContextService>? log = null)
    {
        _remoting = remoting;
        _log = (ILogger?)log ?? NullLogger.Instance;
    }

    /// <summary>Capture the connected environment's identifying context.</summary>
    public EnvironmentContextInfo Capture()
    {
        var ctx = _remoting.Read(m => m.UtilityService.GetEnvironmentContextAsync().GetAwaiter().GetResult());
        if (ctx is null) return new EnvironmentContextInfo("", "", "");
        return new EnvironmentContextInfo(
            ctx.CustomerSafeName ?? "",
            ctx.EnvironmentSafeName ?? "",
            ctx.Stack ?? "");
    }
}
