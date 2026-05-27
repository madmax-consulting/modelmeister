namespace ModelMeister.Ui.Models;

/// <summary>
/// A user-definable environment classification: a colored shorthand badge plus a name and
/// description, with a <see cref="IsProtected"/> flag that drives the destructive-operation safety
/// banner. The seven built-in types — keyed by the legacy <see cref="EnvironmentStage"/> names —
/// ship out of the box; they are fully editable but can never be deleted. Custom types and any edits
/// to built-ins are persisted (non-secret) in <see cref="AppSettings.EnvironmentTypes"/>.
/// </summary>
public sealed class EnvironmentType
{
    /// <summary>Stable identity referenced by <see cref="EnvironmentEntry.TypeKey"/>. Built-in keys are
    /// the legacy enum names ("Prod", "Test", …); custom types get a generated key.</summary>
    public string Key { get; set; } = "";

    /// <summary>Full display name, e.g. "Production".</summary>
    public string Name { get; set; } = "";

    /// <summary>Short tag shown in the pill, e.g. "PROD".</summary>
    public string Shorthand { get; set; } = "";

    /// <summary>Optional free-form description shown in the types list.</summary>
    public string? Description { get; set; }

    /// <summary>Pill color as a hex string, e.g. "#C0392B". The pill text/border use this color and the
    /// background a translucent variant, so a single color renders correctly over either theme.</summary>
    public string ColorHex { get; set; } = "#6B7280";

    /// <summary>When true, destructive operations against an environment of this type surface the red
    /// safety banner ("PROTECTED ENVIRONMENT"). Built-in <c>Prod</c> ships with this on.</summary>
    public bool IsProtected { get; set; }

    /// <summary>True for the seven shipped types — editable but never deletable.</summary>
    public bool IsBuiltIn { get; set; }

    /// <summary>Display order in lists; built-ins seed 0..6, custom types append after.</summary>
    public int SortOrder { get; set; }
}
