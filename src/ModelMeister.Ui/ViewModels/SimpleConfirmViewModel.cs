using System;
using CommunityToolkit.Mvvm.Input;

namespace ModelMeister.Ui.ViewModels;

/// <summary>Generic two-button confirmation. Use via <c>DialogHost.ConfirmAsync(title, message)</c>.</summary>
public partial class SimpleConfirmViewModel : ViewModelBase
{
    public SimpleConfirmViewModel(string title, string message, string confirmLabel, string cancelLabel)
    {
        Title = title;
        Message = message;
        ConfirmLabel = confirmLabel;
        CancelLabel = cancelLabel;
    }

    public string Title { get; }
    public string Message { get; }
    public string ConfirmLabel { get; }
    public string CancelLabel { get; }

    public bool? Result { get; private set; }
    public event Action? Closed;

    [RelayCommand]
    private void Confirm()
    {
        Result = true;
        Closed?.Invoke();
    }

    [RelayCommand]
    private void Cancel()
    {
        Result = false;
        Closed?.Invoke();
    }
}
