using CommunityToolkit.Mvvm.ComponentModel;

namespace ModelMeister.Ui.Models;

/// <summary>
/// Base for any grid row that participates in the uniform checkbox-selection system. Exposes the
/// single observable <see cref="IsSelected"/> flag the left-hand checkbox column binds to. The
/// header select-all checkbox (<see cref="ViewModels.RowSelectionModel"/>), shift-click range
/// selection (<see cref="Services.GridSelectionBehavior"/>), and the bulk Delete / Promote commands
/// all read and write this flag — so selection is one consistent concept across every feature grid.
/// </summary>
public abstract partial class SelectableRow : ObservableObject
{
    [ObservableProperty] private bool _isSelected;
}
