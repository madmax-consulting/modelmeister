using CommunityToolkit.Mvvm.ComponentModel;

namespace ModelMeister.Ui.Models;

/// <summary>
/// A user-definable grouping that sits above environments: each <see cref="EnvironmentEntry"/> belongs
/// to exactly one organization (referenced by <see cref="EnvironmentEntry.OrgKey"/>). The single built-in
/// "Default" organization ships as a starter so existing environments always resolve somewhere; it is
/// fully editable and may be deleted (the deletion is tombstoned in
/// <see cref="AppSettings.DeletedBuiltInOrgKeys"/> so it survives restarts) as long as no environment
/// still references it. Custom organizations and any edits to the built-in are persisted (non-secret) in
/// <see cref="AppSettings.Organizations"/>.
///
/// Raises <see cref="ObservableObject.PropertyChanged"/> so the organizations-management grid reflects
/// edits in place: <see cref="ModelMeister.Ui.Services.IOrganizationRegistry.Upsert"/> mutates the same
/// instance the grid is already bound to.
/// </summary>
public sealed class Organization : ObservableObject
{
    private string _key = "";
    private string _name = "";
    private string? _description;
    private bool _isBuiltIn;
    private int _sortOrder;

    /// <summary>Stable identity referenced by <see cref="EnvironmentEntry.OrgKey"/>. The built-in key is
    /// "Default"; custom organizations get a generated key.</summary>
    public string Key { get => _key; set => SetProperty(ref _key, value); }

    /// <summary>Display name, e.g. "Acme Corp".</summary>
    public string Name { get => _name; set => SetProperty(ref _name, value); }

    /// <summary>Optional free-form description shown in the organizations list.</summary>
    public string? Description { get => _description; set => SetProperty(ref _description, value); }

    /// <summary>True for the shipped "Default" starter organization. Distinguishes a deletion that must
    /// be tombstoned (built-in, re-seeded on rebuild) from a plain custom-organization removal.</summary>
    public bool IsBuiltIn { get => _isBuiltIn; set => SetProperty(ref _isBuiltIn, value); }

    /// <summary>Display order in lists; the built-in seeds 0, custom organizations append after.</summary>
    public int SortOrder { get => _sortOrder; set => SetProperty(ref _sortOrder, value); }
}
