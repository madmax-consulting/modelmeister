using System.Collections.ObjectModel;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using ModelMeister.Ui.Models;
using ModelMeister.Ui.Services;

namespace ModelMeister.Ui.ViewModels;

/// <summary>
/// Management page (Environments → Organizations) for the user-defined + built-in organizations.
/// Lists every organization with Add / Edit / Delete, routed through the modal editor dialog and the
/// <see cref="IOrganizationRegistry"/>. The built-in "Default" is only a starter, so it can be edited or
/// deleted like any other; an organization still referenced by an environment can't be deleted until
/// those environments are reassigned.
/// </summary>
public partial class OrganizationsViewModel : ViewModelBase
{
    private readonly IOrganizationRegistry _registry;
    private readonly IAppLog _log;

    /// <summary>One entry per registered organization, refreshed whenever the registry changes.</summary>
    public ObservableCollection<Organization> Organizations { get; } = new();

    [ObservableProperty] private string _status = "";

    public OrganizationsViewModel(MainWindowViewModel main, IOrganizationRegistry registry, IAppLog log)
    {
        _registry = registry;
        _log = log;
        _registry.Changed += Reload;
        Reload();
    }

    private void Reload()
    {
        Organizations.Clear();
        foreach (var o in _registry.All) Organizations.Add(o);
        Status = $"{Organizations.Count} organization(s) — pick the active organization in the title bar to scope environments and comparisons.";
    }

    [RelayCommand]
    private async Task AddAsync()
    {
        var vm = await DialogHost.OrganizationEditorAsync(null).ConfigureAwait(true);
        if (vm is null) return;
        _registry.Upsert(vm.ToOrganization());
        _log.Success("Organizations", $"Created organization '{vm.Name}'.");
    }

    [RelayCommand]
    private async Task EditAsync(Organization? org)
    {
        if (org is null) return;
        var vm = await DialogHost.OrganizationEditorAsync(org).ConfigureAwait(true);
        if (vm is null) return;
        _registry.Upsert(vm.ToOrganization());
        _log.Success("Organizations", $"Updated organization '{vm.Name}'.");
    }

    [RelayCommand]
    private async Task DeleteAsync(Organization? org)
    {
        if (org is null) return;
        if (_registry.IsInUse(org.Key))
        {
            await DialogHost.ConfirmAsync(
                "Organization in use",
                $"'{org.Name}' has one or more environments assigned to it. Reassign those environments to another organization before deleting it.",
                confirmLabel: "OK", cancelLabel: "Close").ConfigureAwait(true);
            return;
        }
        var ok = await DialogHost.ConfirmBulkAsync(
            "Delete organization", "Delete", "organization",
            new[] { org.Name }, envName: null).ConfigureAwait(true);
        if (!ok) return;
        _registry.Delete(org.Key);
        _log.Success("Organizations", $"Deleted organization '{org.Name}'.");
    }
}
