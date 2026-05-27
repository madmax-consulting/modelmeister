using System.Collections.ObjectModel;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using ModelMeister.Ui.Models;
using ModelMeister.Ui.Services;

namespace ModelMeister.Ui.ViewModels;

/// <summary>
/// Management page (Setup → Environment types) for the user-defined + built-in environment types.
/// Lists every type with a live pill preview and Add / Edit / Delete, routed through the modal editor
/// dialog and the <see cref="IEnvironmentTypeRegistry"/>. Built-in types can be edited but not deleted;
/// a type in use by an environment can't be deleted until it's reassigned.
/// </summary>
public partial class EnvironmentTypesViewModel : ViewModelBase
{
    private readonly IEnvironmentTypeRegistry _registry;
    private readonly IAppLog _log;

    /// <summary>One entry per registered type, refreshed whenever the registry changes.</summary>
    public ObservableCollection<EnvironmentType> Types { get; } = new();

    [ObservableProperty] private string _status = "";

    public EnvironmentTypesViewModel(MainWindowViewModel main, IEnvironmentTypeRegistry registry, IEnvironmentVault vault, IAppLog log)
    {
        _registry = registry;
        _log = log;
        _registry.Changed += Reload;
        Reload();
    }

    private void Reload()
    {
        Types.Clear();
        foreach (var t in _registry.All) Types.Add(t);
        Status = $"{Types.Count} environment types — used for the colored pills on every environment.";
    }

    [RelayCommand]
    private async Task AddAsync()
    {
        var vm = await DialogHost.EnvironmentTypeEditorAsync(null).ConfigureAwait(true);
        if (vm is null) return;
        _registry.Upsert(vm.ToType());
        _log.Success("Environment types", $"Created type '{vm.Name}'.");
    }

    [RelayCommand]
    private async Task EditAsync(EnvironmentType? type)
    {
        if (type is null) return;
        var vm = await DialogHost.EnvironmentTypeEditorAsync(type).ConfigureAwait(true);
        if (vm is null) return;
        _registry.Upsert(vm.ToType());
        _log.Success("Environment types", $"Updated type '{vm.Name}'.");
    }

    [RelayCommand]
    private async Task DeleteAsync(EnvironmentType? type)
    {
        if (type is null) return;
        if (type.IsBuiltIn)
        {
            Status = "Built-in types can't be deleted (they can be recolored / renamed instead).";
            return;
        }
        if (_registry.IsInUse(type.Key))
        {
            await DialogHost.ConfirmAsync(
                "Type in use",
                $"'{type.Name}' is assigned to one or more environments. Reassign those environments to another type before deleting it.",
                confirmLabel: "OK", cancelLabel: "Close").ConfigureAwait(true);
            return;
        }
        var ok = await DialogHost.ConfirmBulkAsync(
            "Delete environment type", "Delete", "type",
            new[] { type.Name }, envName: null).ConfigureAwait(true);
        if (!ok) return;
        _registry.Delete(type.Key);
        _log.Success("Environment types", $"Deleted type '{type.Name}'.");
    }
}
