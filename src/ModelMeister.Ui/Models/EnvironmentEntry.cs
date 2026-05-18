using System;

namespace ModelMeister.Ui.Models;

/// <summary>Maturity stage for an environment; used to surface a prod-guard banner before destructive operations.</summary>
public enum EnvironmentStage { Unspecified, Dev, Test, QA, UAT, Stage, Prod }

/// <summary>
/// Persisted definition of an inriver environment the user has configured. Secrets are stored
/// separately in <see cref="EnvironmentSecret"/> so they can be encrypted independently of the
/// (non-sensitive) metadata.
/// </summary>
public sealed class EnvironmentEntry
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Name { get; set; } = "";
    public string Url { get; set; } = "";
    /// <summary>Base URL of the inriver REST API for this env (e.g. https://apieuw.productmarketingcloud.com). Optional.</summary>
    public string? RestBaseUrl { get; set; }
    public EnvironmentStage Stage { get; set; } = EnvironmentStage.Unspecified;
    public string? Notes { get; set; }
    public DateTime LastUsedUtc { get; set; }
}

/// <summary>
/// Credential bag paired with an <see cref="EnvironmentEntry"/>. DPAPI-encrypted at rest.
/// <see cref="ApiKey"/> is the Remoting key (also accepted by REST in some envs); <see cref="RestApiKey"/>
/// is reserved for the dedicated REST admin key that authorises features Remoting can't do, like
/// user creation and the modern Extensions endpoints.
/// </summary>
public sealed class EnvironmentSecret
{
    public string? ApiKey { get; set; }
    public string? RestApiKey { get; set; }
}
