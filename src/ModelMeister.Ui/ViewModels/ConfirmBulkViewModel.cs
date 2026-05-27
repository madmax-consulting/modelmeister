using System;
using System.Collections.Generic;
using System.Linq;
using CommunityToolkit.Mvvm.Input;
using ModelMeister.Ui.Services;

namespace ModelMeister.Ui.ViewModels;

/// <summary>
/// View-model behind the itemized, stage-aware bulk-confirm dialog. Unlike the old count-only
/// prompts ("Delete 12 role(s)?") this always shows <em>what</em> is about to change: the dialog
/// lists every item by name (capped at <see cref="RenderCap"/> with a "+N more" note) so the user
/// sees exactly the targets. When the connected env is Prod and the action is destructive it renders
/// a red production banner. There is no type-to-confirm friction (per product decision) — the
/// itemized list + stage banner are the guard.
/// </summary>
public sealed partial class ConfirmBulkViewModel : ViewModelBase
{
    /// <summary>Cap on how many item names are rendered; the rest collapse into "+N more".</summary>
    private const int RenderCap = 200;

    public ConfirmBulkViewModel(
        string title, string verb, string noun,
        IReadOnlyList<string> itemNames,
        string? envName, string? typeKey, bool destructive)
    {
        Title = title;
        Verb = verb;
        Destructive = destructive;
        ConfirmLabel = verb;

        var count = itemNames.Count;
        Headline = $"{verb} {count} {Pluralize(noun, count)}?";

        Items = itemNames.Take(RenderCap).ToList();
        Overflow = Math.Max(0, count - Items.Count);
        HasOverflow = Overflow > 0;

        EnvName = envName;
        HasEnv = !string.IsNullOrEmpty(envName);
        Stage = typeKey;
        IsProtected = (EnvironmentTypeRegistry.Current?.IsProtected(typeKey) ?? false) && destructive;
        Eyebrow = destructive ? "DESTRUCTIVE ACTION" : "CONFIRM ACTION";
    }

    /// <summary>Window title (e.g. "Delete roles").</summary>
    public string Title { get; }
    /// <summary>Small all-caps eyebrow above the headline.</summary>
    public string Eyebrow { get; }
    /// <summary>The bold question, e.g. "Delete 12 roles?".</summary>
    public string Headline { get; }
    /// <summary>The action verb, reused as the confirm-button label.</summary>
    public string Verb { get; }
    /// <summary>Confirm button text (= <see cref="Verb"/>).</summary>
    public string ConfirmLabel { get; }
    /// <summary>True for delete-style actions: red styling + Prod banner eligibility.</summary>
    public bool Destructive { get; }

    /// <summary>Names rendered in the scrollable list (capped at <see cref="RenderCap"/>).</summary>
    public IReadOnlyList<string> Items { get; }
    /// <summary>How many items were elided past the render cap.</summary>
    public int Overflow { get; }
    public bool HasOverflow { get; }
    public string OverflowText => $"+{Overflow} more…";

    /// <summary>Connected environment name (null for local-file targets like snapshots).</summary>
    public string? EnvName { get; }
    public bool HasEnv { get; }
    /// <summary>Environment-type key the StageTo* converters resolve into the colored pill.</summary>
    public string? Stage { get; }
    /// <summary>True when the target environment's type is protected and the action is destructive.</summary>
    public bool IsProtected { get; }

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

    private static string Pluralize(string noun, int count)
        => count == 1 || noun.EndsWith('s') ? noun : noun + "s";
}
