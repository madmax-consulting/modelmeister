using System;
using CommunityToolkit.Mvvm.Input;

namespace ModelMeister.Ui.ViewModels;

/// <summary>
/// View-model behind the apply-confirmation dialog. Clicking Apply confirms — the previous
/// "type APPLY verbatim" friction step was removed at the user's request.
/// </summary>
public partial class ConfirmApplyViewModel : ViewModelBase
{
    /// <summary>Construct a confirmation prompt for an apply that targets <paramref name="envUrl"/>.</summary>
    public ConfirmApplyViewModel(string envUrl, int changeCount, string policySummary = "", string stage = "Unspecified")
    {
        EnvironmentUrl = envUrl;
        ChangeCount = changeCount;
        PolicySummary = policySummary;
        Stage = stage;
        IsProd = string.Equals(stage, "Prod", StringComparison.Ordinal);
    }

    /// <summary>URL of the target environment, displayed prominently in the dialog.</summary>
    public string EnvironmentUrl { get; }

    /// <summary>Number of changes that will be applied if the user confirms.</summary>
    public int ChangeCount { get; }

    /// <summary>Human-readable summary of the merge policy in effect (deletes allowed, etc.).</summary>
    public string PolicySummary { get; }

    /// <summary>The connected environment's stage label (e.g. "Prod", "Test").</summary>
    public string Stage { get; }

    /// <summary>True when <see cref="Stage"/> is "Prod"; the dialog renders an extra-red banner in that case.</summary>
    public bool IsProd { get; }

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
