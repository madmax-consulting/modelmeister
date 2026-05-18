using System;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace ModelMeister.Ui.ViewModels;

/// <summary>
/// View-model behind the Add / Edit Server Setting dialog. Used for both create and edit; when
/// <see cref="IsEdit"/> the Key field is read-only (renaming would require a delete + create) and
/// the dialog title shows "Edit".
/// </summary>
public partial class AddServerSettingViewModel : ViewModelBase
{
    public AddServerSettingViewModel(string? initialKey = null, string? initialValue = null, bool isEdit = false)
    {
        _key = initialKey ?? string.Empty;
        _value = initialValue ?? string.Empty;
        IsEdit = isEdit;
    }

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(ConfirmCommand))]
    private string _key = string.Empty;

    [ObservableProperty] private string _value = string.Empty;

    /// <summary>Validation message shown in red under the form.</summary>
    [ObservableProperty] private string _validation = string.Empty;

    /// <summary>True when the dialog was opened to edit an existing row; locks the Key field.</summary>
    public bool IsEdit { get; }

    public string Title => IsEdit ? "Edit server setting" : "New server setting";

    /// <summary>The dialog result, set just before <see cref="Closed"/> fires.</summary>
    public bool? Result { get; private set; }

    /// <summary>Raised when the dialog should close (Confirm or Abort).</summary>
    public event Action? Closed;

    private bool CanConfirm() => !string.IsNullOrWhiteSpace(Key);

    [RelayCommand(CanExecute = nameof(CanConfirm))]
    private void Confirm()
    {
        if (string.IsNullOrWhiteSpace(Key))
        {
            Validation = "Key is required.";
            return;
        }
        Result = true;
        Closed?.Invoke();
    }

    [RelayCommand]
    private void Abort()
    {
        Result = false;
        Closed?.Invoke();
    }
}
