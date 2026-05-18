using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using ModelMeister.Ui.Models;

namespace ModelMeister.Ui.ViewModels;

/// <summary>One button on the Model → Manage workflow strip. Owns the bindable state
/// (<see cref="IsDone"/>, <see cref="IsCurrent"/>, <see cref="IsClickable"/>) for a single step;
/// <see cref="MainWindowViewModel"/> updates these whenever the underlying workflow state moves.</summary>
public sealed partial class WorkflowStepItem : ObservableObject
{
    /// <summary>The workflow step this item represents.</summary>
    public WorkflowStep Step { get; }

    /// <summary>The label shown on the chip (e.g. "ENVIRONMENT", "MODEL", …).</summary>
    public string Title { get; }

    /// <summary>True for every item except the last — toggles the trailing chevron separator.</summary>
    public bool HasSeparator { get; }

    /// <summary>Click handler that navigates the host to this step.</summary>
    public IRelayCommand ActivateCommand { get; }

    /// <summary>True when the step's completion criterion is met (checkmark glyph shown).</summary>
    [ObservableProperty] private bool _isDone;

    /// <summary>True when this step is the active body inside Model → Manage (accent highlight).</summary>
    [ObservableProperty] private bool _isCurrent;

    /// <summary>True when the button accepts clicks: every done step + the first pending step.</summary>
    [ObservableProperty] private bool _isClickable;

    public WorkflowStepItem(WorkflowStep step, string title, bool hasSeparator, IRelayCommand activate)
    {
        Step = step;
        Title = title;
        HasSeparator = hasSeparator;
        ActivateCommand = activate;
    }
}
