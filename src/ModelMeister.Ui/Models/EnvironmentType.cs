using CommunityToolkit.Mvvm.ComponentModel;

namespace ModelMeister.Ui.Models;

/// <summary>
/// A user-definable environment classification: a colored shorthand badge plus a name and
/// description, with a <see cref="IsProtected"/> flag that drives the destructive-operation safety
/// banner. The seven built-in types — keyed by the legacy <see cref="EnvironmentStage"/> names —
/// ship out of the box; they are fully editable but can never be deleted. Custom types and any edits
/// to built-ins are persisted (non-secret) in <see cref="AppSettings.EnvironmentTypes"/>.
///
/// Raises <see cref="ObservableObject.PropertyChanged"/> so the types-management grid reflects edits
/// in place: <see cref="ModelMeister.Ui.Services.IEnvironmentTypeRegistry.Upsert"/> mutates the same
/// instance the grid is already bound to, and (unlike fresh-per-reload rows) the recycled row
/// containers only repaint when the model notifies.
/// </summary>
public sealed class EnvironmentType : ObservableObject
{
    private string _key = "";
    private string _name = "";
    private string _shorthand = "";
    private string? _description;
    private string _colorHex = "#6B7280";
    private bool _isProtected;
    private bool _isBuiltIn;
    private int _sortOrder;

    /// <summary>Stable identity referenced by <see cref="EnvironmentEntry.TypeKey"/>. Built-in keys are
    /// the legacy enum names ("Prod", "Test", …); custom types get a generated key.</summary>
    public string Key { get => _key; set => SetProperty(ref _key, value); }

    /// <summary>Full display name, e.g. "Production".</summary>
    public string Name { get => _name; set => SetProperty(ref _name, value); }

    /// <summary>Short tag shown in the pill, e.g. "PROD".</summary>
    public string Shorthand { get => _shorthand; set => SetProperty(ref _shorthand, value); }

    /// <summary>Optional free-form description shown in the types list.</summary>
    public string? Description { get => _description; set => SetProperty(ref _description, value); }

    /// <summary>Pill color as a hex string, e.g. "#C0392B". The pill text/border use this color and the
    /// background a translucent variant, so a single color renders correctly over either theme.</summary>
    public string ColorHex { get => _colorHex; set => SetProperty(ref _colorHex, value); }

    /// <summary>When true, destructive operations against an environment of this type surface the red
    /// safety banner ("PROTECTED ENVIRONMENT"). Built-in <c>Prod</c> ships with this on.</summary>
    public bool IsProtected { get => _isProtected; set => SetProperty(ref _isProtected, value); }

    /// <summary>True for the seven shipped types — editable but never deletable.</summary>
    public bool IsBuiltIn { get => _isBuiltIn; set => SetProperty(ref _isBuiltIn, value); }

    /// <summary>Display order in lists; built-ins seed 0..6, custom types append after.</summary>
    public int SortOrder { get => _sortOrder; set => SetProperty(ref _sortOrder, value); }
}
