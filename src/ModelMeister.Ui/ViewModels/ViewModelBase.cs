using CommunityToolkit.Mvvm.ComponentModel;

namespace ModelMeister.Ui.ViewModels;

/// <summary>
/// Base class for every UI view-model. Inherits <see cref="ObservableObject"/> from the MVVM
/// Toolkit so derived classes can use <c>[ObservableProperty]</c> and <c>[RelayCommand]</c>.
/// </summary>
public abstract class ViewModelBase : ObservableObject { }
