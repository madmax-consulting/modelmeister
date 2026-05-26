using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.Input;

namespace ModelMeister.Ui.ViewModels;

/// <summary>One line in the per-user results table inside <see cref="ProvisionResultViewModel"/>.</summary>
public sealed record ProvisionResultRow(string Username, string Outcome, string Detail)
{
    /// <summary>"created", "updated", "skipped", "error", "would-create", "would-update".</summary>
    public string Outcome { get; init; } = Outcome;
}

/// <summary>
/// Summary view of a Users-workbook import (real or dry-run). Shows headline counts (created /
/// updated / errors / warnings) plus a row-by-row table so the user can see exactly what happened
/// to each username. Replaces the previous "result baked into a Status string" pattern.
/// </summary>
public partial class ProvisionResultViewModel : ViewModelBase
{
    public ProvisionResultViewModel(
        bool dryRun, int created, int updated, int errors, int warnings, IEnumerable<ProvisionResultRow> rows,
        string importEyebrow = "USERS IMPORT", string keyColumnHeader = "Username", string itemNoun = "users")
    {
        DryRun = dryRun;
        Created = created;
        Updated = updated;
        Errors = errors;
        Warnings = warnings;
        Rows = new ObservableCollection<ProvisionResultRow>(rows);
        _importEyebrow = importEyebrow;
        KeyColumnHeader = keyColumnHeader;
        ItemNoun = itemNoun;
    }

    private readonly string _importEyebrow;

    public bool DryRun { get; }
    public int Created { get; }
    public int Updated { get; }
    public int Errors { get; }
    public int Warnings { get; }
    public ObservableCollection<ProvisionResultRow> Rows { get; }

    /// <summary>Header for the first (key) column — "Username" for users, "Restriction" for restricted
    /// fields, etc. Parameterised so the shared dialog never mislabels a non-user import as users.</summary>
    public string KeyColumnHeader { get; }

    /// <summary>Plural noun for the count chips ("users", "restrictions", "roles").</summary>
    public string ItemNoun { get; }

    public string Title => DryRun ? "Dry-run result" : "Import result";
    /// <summary>On a dry-run preview the dismiss button reads "Cancel" (since "Continue with import" is
    /// the primary action); on a real result it reads "Close".</summary>
    public string CloseLabel => DryRun ? "Cancel" : "Close";
    public string Eyebrow => DryRun ? "DRY RUN — NO CHANGES APPLIED" : _importEyebrow;
    public string HeadlineCreated => DryRun ? $"would create {Created}" : $"created {Created}";
    public string HeadlineUpdated => DryRun ? $"would update {Updated}" : $"updated {Updated}";

    /// <summary>True on a dry-run preview — surfaces the "Continue with import" button so the user
    /// explicitly approves the real import as the next step.</summary>
    public bool CanProceed => DryRun;

    /// <summary>Set when the user clicks "Continue with import" — tells the caller to run the real import.</summary>
    public bool Proceed { get; private set; }

    /// <summary>Raised when the dialog should close.</summary>
    public event Action? Closed;

    /// <summary>The dialog result. Always <c>true</c> — this dialog is informational, no destructive action.</summary>
    public bool? Result { get; private set; } = true;

    [RelayCommand]
    private void Close()
    {
        Result = true;
        Closed?.Invoke();
    }

    [RelayCommand]
    private void ProceedWithImport()
    {
        Proceed = true;
        Result = true;
        Closed?.Invoke();
    }
}
