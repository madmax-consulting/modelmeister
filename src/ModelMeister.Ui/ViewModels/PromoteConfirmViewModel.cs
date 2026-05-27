using System;
using CommunityToolkit.Mvvm.Input;
using ModelMeister.Ui.Services;

namespace ModelMeister.Ui.ViewModels;

/// <summary>
/// Confirmation dialog for per-row promote actions on compare pages. Generic over the concept
/// being promoted — the host VM picks the wording. Production targets get an extra-prominent
/// banner.
/// </summary>
public partial class PromoteConfirmViewModel : ViewModelBase
{
    public PromoteConfirmViewModel(string conceptLabel, string itemLabel, string sourceEnv, string targetEnv, string? targetTypeKey)
    {
        ConceptLabel = conceptLabel;
        ItemLabel = itemLabel;
        SourceEnv = sourceEnv;
        TargetEnv = targetEnv;
        TargetStage = targetTypeKey;
        IsProtected = EnvironmentTypeRegistry.Current?.IsProtected(targetTypeKey) ?? false;
    }

    /// <summary>Concept being promoted (e.g. "User", "Setting", "CVL value").</summary>
    public string ConceptLabel { get; }
    /// <summary>Identifier of the row being promoted (e.g. "alice", "cache.ttl").</summary>
    public string ItemLabel { get; }
    public string SourceEnv { get; }
    public string TargetEnv { get; }
    /// <summary>Target environment's type key (used to resolve the protected guard).</summary>
    public string? TargetStage { get; }
    public bool IsProtected { get; }

    public string Headline => $"Promote {ConceptLabel.ToLowerInvariant()} '{ItemLabel}'";
    public string Detail =>
        $"This will overwrite '{ItemLabel}' in '{TargetEnv}' with the value currently in '{SourceEnv}'. " +
        "The change is applied immediately and cannot be rolled back automatically.";

    public bool? Result { get; private set; }
    public event Action? Closed;

    [RelayCommand]
    private void Continue()
    {
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
