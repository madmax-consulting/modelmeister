using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;

namespace ModelMeister.Ui.Services.Import;

/// <summary>
/// Static description of an Excel import feature, shown in the workflow header / step rail and used
/// to label the per-row grid. Supplied by each <see cref="IImportPlan"/>.
/// </summary>
public sealed record ImportPlanMetadata(
    string Eyebrow,            // "USERS IMPORT"
    string Title,              // "Import users from workbook"
    string Subtitle,           // one-line explanation shown on the Choose-file step
    string ItemNoun,           // "users" — pluralised count noun
    string KeyColumnHeader,    // "Username" / "Role" / "Key" / "Path" / "CVL"
    string SuggestedFileName,  // "users.xlsx"
    BackupScope BackupScope);  // which scoped backup the auto-backup captures

/// <summary>How the Verify step categorised a workbook row against the live environment.</summary>
public enum RowPlanKind
{
    /// <summary>Row creates a new item in the env.</summary>
    WillCreate,
    /// <summary>Row updates an existing item in the env.</summary>
    WillUpdate,
    /// <summary>Row is a no-op (already present / unchanged) and is never applied.</summary>
    WillSkip,
    /// <summary>Row failed pre-flight validation and is never applied (see <see cref="ImportRowViewModel.Reason"/>).</summary>
    Invalid,
}

/// <summary>Live state of a row as the import loop runs. Drives the status pill in the grid.</summary>
public enum RowRunState
{
    /// <summary>Not yet processed.</summary>
    Pending,
    /// <summary>Currently being applied.</summary>
    Running,
    /// <summary>Applied as a create.</summary>
    Created,
    /// <summary>Applied as an update.</summary>
    Updated,
    /// <summary>Not applied (will-skip / invalid), or left untouched after an abort/cancel.</summary>
    Skipped,
    /// <summary>Apply failed (see <see cref="ImportRowViewModel.Error"/>).</summary>
    Failed,
    /// <summary>Pending row that the run was cancelled before reaching.</summary>
    Cancelled,
}

/// <summary>
/// One workbook row inside the import workflow. The SAME instance is shown in the Verify preview and
/// mutated live during Import — its <see cref="State"/> flips Pending → Running → outcome in place,
/// which is how the user sees every row update in real time.
/// </summary>
public sealed partial class ImportRowViewModel : ObservableObject
{
    /// <summary>Value shown in the key column (username, role name, setting key, folder path, …).</summary>
    public required string Key { get; init; }

    /// <summary>Short human description of what the row carries ("roles: a, b", "+3 ~1 -2", …).</summary>
    public required string Preview { get; init; }

    /// <summary>How Verify categorised the row. Only <see cref="RowPlanKind.WillCreate"/> /
    /// <see cref="RowPlanKind.WillUpdate"/> rows are passed to <see cref="IImportPlan.ApplyRowAsync"/>.</summary>
    public required RowPlanKind PlanKind { get; init; }

    /// <summary>Why the row is <see cref="RowPlanKind.Invalid"/> or <see cref="RowPlanKind.WillSkip"/>.</summary>
    public string? Reason { get; init; }

    /// <summary>Feature-specific payload the plan needs to apply this row (a spec, an action, a KVP…).</summary>
    public required object Payload { get; init; }

    /// <summary>Live run state — bound to the status pill. Mutated by the workflow engine on the UI thread.</summary>
    [ObservableProperty] private RowRunState _state = RowRunState.Pending;

    /// <summary>Per-row result detail after apply (e.g. "roles: a, b" or "(cleared)").</summary>
    [ObservableProperty] private string? _resultDetail;

    /// <summary>Per-row error text after a failed apply.</summary>
    [ObservableProperty] private string? _error;
}

/// <summary>
/// Result of <see cref="IImportPlan.LoadAndVerifyAsync"/>: the categorised rows plus headline counts.
/// When the import would remove items, the <c>Destructive*</c> fields drive an explicit confirmation
/// gate (a <see cref="DialogHost.ConfirmBulkAsync"/> prompt) between Verify and Import.
/// </summary>
public sealed record VerifyResult(
    IReadOnlyList<ImportRowViewModel> Rows,
    int WillCreate,
    int WillUpdate,
    int WillSkip,
    int Invalid,
    string? DestructiveConfirmTitle = null,
    string? DestructiveVerb = null,
    string? DestructiveNoun = null,
    IReadOnlyList<string>? DestructiveItems = null);

/// <summary>Outcome of applying a single row. Plans return <see cref="RowRunState.Failed"/> with an
/// <see cref="Error"/> rather than throwing for a row-level failure.</summary>
public sealed record RowOutcome(RowRunState State, string Detail, string? Error = null);

/// <summary>
/// Feature-specific half of the unified Excel-import workflow. The shared
/// <c>ImportWorkflowViewModel</c> owns the step flow, progress, cancellation,
/// abort-on-error, counts, and the backup; a plan supplies only load/verify, the backup capture,
/// and how to apply one row.
/// </summary>
public interface IImportPlan
{
    /// <summary>Static labels for the header / step rail / grid.</summary>
    ImportPlanMetadata Metadata { get; }

    /// <summary>The workbook path used by the last <see cref="LoadAndVerifyAsync"/> call — the caller
    /// pushes it onto the recents MRU after a successful run.</summary>
    string? LastWorkbookPath { get; }

    /// <summary>Gate the run before the window opens. Returns <c>null</c> when ready, else a
    /// user-facing reason (e.g. Users needs a REST base URL + API key on the connected env).</summary>
    string? CheckPreconditions();

    /// <summary>Load the workbook + live state and categorise every row. Run off the UI thread by the
    /// engine; may compute a real diff against the connected environment.</summary>
    Task<VerifyResult> LoadAndVerifyAsync(string workbookPath, CancellationToken ct);

    /// <summary>Capture the automatic pre-run backup and return its saved path (<c>null</c> when the
    /// feature has no scoped backup). Throwing aborts the import before any row is written.</summary>
    Task<string?> BackupAsync(CancellationToken ct);

    /// <summary>Apply one row. Cancellation-aware. Must NOT throw for a row-level failure — return a
    /// <see cref="RowOutcome"/> with <see cref="RowRunState.Failed"/> and an error instead.</summary>
    Task<RowOutcome> ApplyRowAsync(ImportRowViewModel row, CancellationToken ct);
}
