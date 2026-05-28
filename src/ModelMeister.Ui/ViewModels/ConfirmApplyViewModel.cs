using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using CommunityToolkit.Mvvm.Input;
using ModelMeister.Ui.Services;

namespace ModelMeister.Ui.ViewModels;

/// <summary>One reviewable change line shown in the apply-confirmation dialog.</summary>
public sealed record ApplyReviewItem(string Operation, string Description, bool IsDangerous);

/// <summary>
/// View-model behind the apply-confirmation dialog. Clicking Apply confirms — the previous
/// "type APPLY verbatim" friction step was removed at the user's request.
/// </summary>
public partial class ConfirmApplyViewModel : ViewModelBase
{
    /// <summary>Construct a confirmation prompt for an apply that targets <paramref name="envUrl"/>.</summary>
    public ConfirmApplyViewModel(
        string envUrl,
        int changeCount,
        string policySummary = "",
        string? typeKey = null,
        IReadOnlyList<ApplyReviewItem>? changes = null)
    {
        EnvironmentUrl = envUrl;
        ChangeCount = changeCount;
        PolicySummary = policySummary;
        Stage = typeKey;
        IsProtected = EnvironmentTypeRegistry.Current?.IsProtected(typeKey) ?? false;
        Changes = new ObservableCollection<ApplyReviewItem>(changes ?? []);
        HasChanges = Changes.Count > 0;
        IsDestructive = Changes.Any(c => c.IsDangerous);
    }

    /// <summary>The individual changes that will be applied, for at-a-glance review before committing.</summary>
    public ObservableCollection<ApplyReviewItem> Changes { get; }

    /// <summary>True when there is at least one change line to show.</summary>
    public bool HasChanges { get; }

    /// <summary>True when any change is destructive (delete or datatype change). Drives the red/amber styling.</summary>
    public bool IsDestructive { get; }

    /// <summary>Headline shown in the dialog: escalated only when the batch is destructive or the env is protected.</summary>
    public string Headline => IsDestructive || IsProtected ? "DESTRUCTIVE ACTION" : "APPLY CHANGES";

    /// <summary>True when the dialog should use the danger (red) accent rather than the neutral accent.</summary>
    public bool UseDangerAccent => IsDestructive || IsProtected;

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
