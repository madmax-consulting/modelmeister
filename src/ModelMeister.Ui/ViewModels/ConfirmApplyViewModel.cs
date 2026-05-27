using System;
using CommunityToolkit.Mvvm.Input;
using ModelMeister.Ui.Services;

namespace ModelMeister.Ui.ViewModels;

/// <summary>
/// View-model behind the apply-confirmation dialog. Clicking Apply confirms — the previous
/// "type APPLY verbatim" friction step was removed at the user's request.
/// </summary>
public partial class ConfirmApplyViewModel : ViewModelBase
{
    /// <summary>Construct a confirmation prompt for an apply that targets <paramref name="envUrl"/>.</summary>
    public ConfirmApplyViewModel(string envUrl, int changeCount, string policySummary = "", string? typeKey = null)
    {
        EnvironmentUrl = envUrl;
        ChangeCount = changeCount;
        PolicySummary = policySummary;
        Stage = typeKey;
        IsProtected = EnvironmentTypeRegistry.Current?.IsProtected(typeKey) ?? false;
    }

    /// <summary>URL of the target environment, displayed prominently in the dialog.</summary>
    public string EnvironmentUrl { get; }

    /// <summary>Number of changes that will be applied if the user confirms.</summary>
    public int ChangeCount { get; }

    /// <summary>Human-readable summary of the merge policy in effect (deletes allowed, etc.).</summary>
    public string PolicySummary { get; }

    /// <summary>The connected environment's type key, resolved to a pill by the dialog's converters.</summary>
    public string? Stage { get; }

    /// <summary>True when the target environment's type is protected; the dialog renders the red safety banner.</summary>
    public bool IsProtected { get; }

    /// <summary>The dialog result, set just before <see cref="Closed"/> fires.</summary>
    public bool? Result { get; private set; }

    /// <summary>Raised once the user has confirmed or cancelled; the host closes the window on this signal.</summary>
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
