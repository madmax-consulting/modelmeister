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

    /// <summary>Legacy maturity stage. Superseded by <see cref="TypeKey"/>; retained only so older vault
    /// files still deserialize and migrate. New writes leave it at its default.</summary>
    public EnvironmentStage Stage { get; set; } = EnvironmentStage.Unspecified;

    /// <summary>Key of the <see cref="EnvironmentType"/> assigned to this environment (drives the pill
    /// color/shorthand and the protected-environment guard). Null on legacy entries until migrated from
    /// <see cref="Stage"/>; resolves to the built-in "Unspecified" type when unset.</summary>
    public string? TypeKey { get; set; }

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
