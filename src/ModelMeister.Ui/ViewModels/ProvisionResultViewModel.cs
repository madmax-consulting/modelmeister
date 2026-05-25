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
    public ProvisionResultViewModel(bool dryRun, int created, int updated, int errors, int warnings, IEnumerable<ProvisionResultRow> rows, string importEyebrow = "USERS IMPORT")
    {
        DryRun = dryRun;
        Created = created;
        Updated = updated;
        Errors = errors;
        Warnings = warnings;
        Rows = new ObservableCollection<ProvisionResultRow>(rows);
        _importEyebrow = importEyebrow;
    }

    private readonly string _importEyebrow;

    public bool DryRun { get; }
    public int Created { get; }
    public int Updated { get; }
    public int Errors { get; }
    public int Warnings { get; }
    public ObservableCollection<ProvisionResultRow> Rows { get; }

    public string Title => DryRun ? "Dry-run result" : "Import result";
    public string Eyebrow => DryRun ? "DRY RUN — NO CHANGES APPLIED" : _importEyebrow;
    public string HeadlineCreated => DryRun ? $"would create {Created}" : $"created {Created}";
    public string HeadlineUpdated => DryRun ? $"would update {Updated}" : $"updated {Updated}";

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
}
