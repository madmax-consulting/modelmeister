using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using ModelMeister.Ui.Models;
using ModelMeister.Ui.Services;
using ModelMeister.Ui.Services.Import;

namespace ModelMeister.Ui.Tests;

/// <summary>Scriptable <see cref="IImportPlan"/> for the workflow-engine tests. Records the order of
/// applied rows and lets a test dictate each row's outcome, the backup behaviour, and the verify result.</summary>
internal sealed class FakeImportPlan : IImportPlan
{
    public List<ImportRowViewModel> Rows { get; set; } = new();
    public VerifyResult? VerifyOverride { get; set; }
    public Func<ImportRowViewModel, RowOutcome>? OutcomeFor { get; set; }
    public Exception? BackupThrows { get; set; }
    public string? BackupReturns { get; set; } = "fake-backup.json";
    public bool PreconditionFails { get; set; }

    public List<string> AppliedKeys { get; } = new();
    /// <summary>Invoked after each row applies, with the running applied-count (1-based). Lets a test
    /// trigger cancellation mid-run.</summary>
    public Action<int>? AfterRowApplied { get; set; }

    public ImportPlanMetadata Metadata { get; } =
        new("TEST IMPORT", "Test import", "subtitle", "items", "Key", "test.xlsx", BackupScope.None);

    public string? LastWorkbookPath { get; private set; }

    public string? CheckPreconditions() => PreconditionFails ? "blocked" : null;

    public Task<VerifyResult> LoadAndVerifyAsync(string workbookPath, CancellationToken ct)
    {
        LastWorkbookPath = workbookPath;
        if (VerifyOverride is not null) return Task.FromResult(VerifyOverride);
        var r = Rows;
        return Task.FromResult(new VerifyResult(
            r,
            r.Count(x => x.PlanKind == RowPlanKind.WillCreate),
            r.Count(x => x.PlanKind == RowPlanKind.WillUpdate),
            r.Count(x => x.PlanKind == RowPlanKind.WillSkip),
            r.Count(x => x.PlanKind == RowPlanKind.Invalid)));
    }

    public Task<string?> BackupAsync(CancellationToken ct)
    {
        if (BackupThrows is not null) throw BackupThrows;
        return Task.FromResult(BackupReturns);
    }

    public Task<RowOutcome> ApplyRowAsync(ImportRowViewModel row, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        AppliedKeys.Add(row.Key);
        var outcome = OutcomeFor?.Invoke(row) ?? new RowOutcome(RowRunState.Created, "ok");
        AfterRowApplied?.Invoke(AppliedKeys.Count);
        return Task.FromResult(outcome);
    }
}

/// <summary>Fake destructive-removal gate: records calls and returns a fixed answer.</summary>
internal sealed class FakeConfirmGate : IImportConfirmGate
{
    public bool Answer { get; set; } = true;
    public int Calls { get; private set; }

    public Task<bool> ConfirmDestructiveAsync(string title, string verb, string noun, IReadOnlyList<string> items)
    {
        Calls++;
        return Task.FromResult(Answer);
    }
}

/// <summary>No-op log so the engine tests don't depend on the toast/log bus.</summary>
internal sealed class NullAppLog : IAppLog
{
    public ObservableCollection<LogEntry> Entries { get; } = new();
    public ObservableCollection<ToastEntry> Toasts { get; } = new();
    public void Info(string source, string message) { }
    public void Success(string source, string message) { }
    public void Warn(string source, string message, Exception? exception = null) { }
    public void Error(string source, string message, Exception? exception = null) { }
    public void Toast(LogLevel level, string title, string? detail = null, Action? onClick = null) { }
    public void DismissToast(ToastEntry entry) { }
    public void Clear() { }
}

/// <summary>No-op file opener.</summary>
internal sealed class NullFileOpener : IFileOpener
{
    public void Open(string path) { }
    public void OpenAt(string filePath, int line) { }
    public void RevealInExplorer(string path) { }
}

/// <summary>In-memory <see cref="ISettingsStore"/> for registry tests — <see cref="Save"/> just bumps a counter.</summary>
internal sealed class FakeSettingsStore : ISettingsStore
{
    public AppSettings Current { get; } = new();
    public int SaveCount { get; private set; }
    public void Save() => SaveCount++;
}

/// <summary>Minimal in-memory <see cref="IEnvironmentVault"/> exposing a fixed entry list (for IsInUse tests).</summary>
internal sealed class FakeEnvironmentVault : IEnvironmentVault
{
    private readonly List<EnvironmentEntry> _entries;
    public FakeEnvironmentVault(params EnvironmentEntry[] entries) => _entries = entries.ToList();

    public IReadOnlyList<EnvironmentEntry> List() => _entries;
    public EnvironmentEntry? Get(Guid id) => _entries.FirstOrDefault(e => e.Id == id);
    public EnvironmentSecret? GetSecret(Guid id) => null;
    public bool SecretMissing(Guid id) => false;
    public void Upsert(EnvironmentEntry entry, EnvironmentSecret secret) { }
    public void Delete(Guid id) { }
    public void Touch(Guid id) { }
    // No-op accessors keep the interface contract without an unused backing field (avoids CS0067).
    public event Action? Changed { add { } remove { } }
}
