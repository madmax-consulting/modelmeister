using System;
using System.Collections;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using ModelMeister.Inriver.Diff;
using ModelMeister.Inriver.Snapshot;
using ModelMeister.Ui.Models;
using ModelMeister.Ui.Services;

namespace ModelMeister.Ui.ViewModels;

/// <summary>
/// Compare CVLs across two environments. Captures a <see cref="LiveModel"/> from each side
/// sequentially (via <see cref="Shell.SwitchEnvAsync"/>) and renders the CVL slice of the
/// resulting <see cref="EnvironmentDiff"/>: existence deltas, definition changes, and per-CVL
/// value deltas — each as a one-property-per-row entry with the actual left/right values.
/// </summary>
public partial class CvlCompareViewModel : ViewModelBase, ICompareViewModel
{
    readonly MainWindowViewModel _main;
    readonly Shell _shell;
    readonly IAppLog _log;
    readonly IEnvironmentVault _vault;

    public ObservableCollection<EnvironmentEntry> AvailableEnvs { get; } = [];
    public ObservableCollection<CvlCompareRow> Rows { get; } = [];
    /// <summary>Per-bucket counts shown as small bars at the bottom (presence vs value vs definition).</summary>
    public ObservableCollection<ConceptDiffCount> Counts { get; } = [];

    [ObservableProperty] private EnvironmentEntry? _leftEnv;
    [ObservableProperty] private EnvironmentEntry? _rightEnv;
    [ObservableProperty] private bool _busy;
    [ObservableProperty] private string _status = "Pick two environments to compare CVLs.";
    [ObservableProperty] private string _summary = "";
    [ObservableProperty] private bool _hasRows;
    /// <summary>Header label for the left-value column = left env name.</summary>
    [ObservableProperty] private string _leftColumnHeader = "";
    /// <summary>Header label for the right-value column = right env name.</summary>
    [ObservableProperty] private string _rightColumnHeader = "";
    /// <summary>Stage of the left env (for the pill in the value-column header).</summary>
    [ObservableProperty] private EnvironmentStage _leftColumnStage;
    /// <summary>Stage of the right env (for the pill in the value-column header).</summary>
    [ObservableProperty] private EnvironmentStage _rightColumnStage;

    public IAsyncRelayCommand SaveCsvCommand { get; }
    public IAsyncRelayCommand CopyMarkdownCommand { get; }
    public IReadOnlyList<CompareAction> ExtraActions { get; } = Array.Empty<CompareAction>();
    /// <summary>Bucket-bar toggle state: clicking a bar in the bottom chart hides that Bucket's rows.</summary>
    public BucketToggleState Buckets { get; } = new();
    public string BucketPath => nameof(CvlCompareRow.Bucket);

    // Cached LiveModel snapshots so per-row promote can look up source CVLs / values and run the
    // parent-CVL-existence pre-check without going back to the wire.
    private LiveModel? _leftSnapshot;
    private LiveModel? _rightSnapshot;

    public CvlCompareViewModel(MainWindowViewModel main, Shell shell, IAppLog log)
    {
        _main = main;
        _shell = shell;
        _log = log;
        _vault = main.Vault;
        _vault.Changed += RefreshEnvList;
        RefreshEnvList();

        SaveCsvCommand = CompareCommands.MakeSaveCsv(
            () => Rows,
            BuildExportColumns,
            suggestedFileName: "cvl-compare.csv",
            log: _log,
            logSource: "CvlCompare");

        CopyMarkdownCommand = CompareCommands.MakeCopyMarkdown(
            () => Rows,
            BuildExportColumns,
            log: _log,
            logSource: "CvlCompare");
    }

    private IReadOnlyList<CompareExport.Column> BuildExportColumns() =>
        new CompareExport.Column[]
        {
            new("Scope",    r => ((CvlCompareRow)r).Bucket),
            new("CVL",      r => ((CvlCompareRow)r).CvlId),
            new("Key",      r => ((CvlCompareRow)r).Key),
            new("Property", r => ((CvlCompareRow)r).Property),
            new(string.IsNullOrEmpty(LeftColumnHeader)  ? "Left"  : LeftColumnHeader,  r => ((CvlCompareRow)r).LeftValue),
            new(string.IsNullOrEmpty(RightColumnHeader) ? "Right" : RightColumnHeader, r => ((CvlCompareRow)r).RightValue),
        };

    public void RefreshEnvList()
    {
        var lid = LeftEnv?.Id;
        var rid = RightEnv?.Id;
        AvailableEnvs.Clear();
        foreach (var e in _vault.List().OrderBy(e => e.Name, StringComparer.OrdinalIgnoreCase))
            AvailableEnvs.Add(e);
        if (lid is { } li) LeftEnv = AvailableEnvs.FirstOrDefault(e => e.Id == li);
        if (rid is { } ri) RightEnv = AvailableEnvs.FirstOrDefault(e => e.Id == ri);

        if (LeftEnv is not null) { LeftColumnHeader = LeftEnv.Name; LeftColumnStage = LeftEnv.Stage; }
        if (RightEnv is not null) { RightColumnHeader = RightEnv.Name; RightColumnStage = RightEnv.Stage; }
    }

    partial void OnLeftEnvChanged(EnvironmentEntry? value)
    {
        LeftColumnHeader = value?.Name ?? "";
        LeftColumnStage = value?.Stage ?? EnvironmentStage.Unspecified;
        TryAutoCompare();
    }

    partial void OnRightEnvChanged(EnvironmentEntry? value)
    {
        RightColumnHeader = value?.Name ?? "";
        RightColumnStage = value?.Stage ?? EnvironmentStage.Unspecified;
        TryAutoCompare();
    }

    private void TryAutoCompare()
    {
        if (Busy) return;
        if (LeftEnv is null || RightEnv is null) return;
        if (LeftEnv.Id == RightEnv.Id)
        {
            Status = "Pick two different environments.";
            Rows.Clear();
            Counts.Clear();
            HasRows = false;
            Summary = "";
            return;
        }
        _ = CompareAsync();
    }

    [RelayCommand]
    public async Task CompareAsync()
    {
        if (LeftEnv is null || RightEnv is null) { Status = "Pick both environments first."; return; }
        if (LeftEnv.Id == RightEnv.Id) { Status = "Pick two different environments."; return; }

        var leftSecret = _vault.GetSecret(LeftEnv.Id);
        var rightSecret = _vault.GetSecret(RightEnv.Id);
        if (leftSecret is null || string.IsNullOrEmpty(leftSecret.ApiKey))
        { Status = $"No API key on file for '{LeftEnv.Name}'."; return; }
        if (rightSecret is null || string.IsNullOrEmpty(rightSecret.ApiKey))
        { Status = $"No API key on file for '{RightEnv.Name}'."; return; }

        Busy = true;
        Rows.Clear();
        Counts.Clear();
        HasRows = false;
        Summary = "";
        try
        {
            Status = $"Connecting to '{LeftEnv.Name}'…";
            await _shell.SwitchEnvAsync(LeftEnv, leftSecret).ConfigureAwait(true);
            Status = $"Capturing CVLs from '{LeftEnv.Name}'…";
            var left = await _shell.CaptureSnapshotAsync().ConfigureAwait(true);

            Status = $"Connecting to '{RightEnv.Name}'…";
            await _shell.SwitchEnvAsync(RightEnv, rightSecret).ConfigureAwait(true);
            Status = $"Capturing CVLs from '{RightEnv.Name}'…";
            var right = await _shell.CaptureSnapshotAsync().ConfigureAwait(true);

            _leftSnapshot = left;
            _rightSnapshot = right;
            Status = "Computing differences…";
            var diff = await _shell.CompareSnapshotsAsync(left, right).ConfigureAwait(true);
            PopulateRows(diff);
            HasRows = Rows.Count > 0;
            Summary = HasRows
                ? $"{Rows.Count} CVL diff row(s) across {Counts.Count} scope(s)"
                : "No differences.";

            Status = "";
            _log.Success("CvlCompare", $"Compared '{LeftEnv.Name}' vs '{RightEnv.Name}': {Rows.Count} CVL diff rows.");
        }
        catch (Exception ex)
        {
            Status = "Compare failed: " + ex.Message;
            _log.Error("CvlCompare", ex.Message, ex);
        }
        finally { Busy = false; }
    }

    private void PopulateRows(EnvironmentDiff diff)
    {
        Buckets.Reset(Counts);
        // CVL existence diffs — one row per CVL that exists on only one side.
        foreach (var id in diff.Cvls.OnlyInLeft)
            Rows.Add(new CvlCompareRow("CVL", id, "", "presence", id, ""));
        foreach (var id in diff.Cvls.OnlyInRight)
            Rows.Add(new CvlCompareRow("CVL", id, "", "presence", "", id));

        // CVL definition changes (DataType / ParentId / CustomValueList).
        foreach (var c in diff.ChangedCvls)
            foreach (var d in c.Differences)
                Rows.Add(new CvlCompareRow("CVL", c.CvlId, "", d.Property, d.Left, d.Right));

        // Per-CVL value deltas: presence + structured per-property changes per key.
        foreach (var d in diff.CvlValueChanges)
        {
            foreach (var key in d.OnlyInLeft)
                Rows.Add(new CvlCompareRow("Value", d.CvlId, key, "presence", key, ""));
            foreach (var key in d.OnlyInRight)
                Rows.Add(new CvlCompareRow("Value", d.CvlId, key, "presence", "", key));
            foreach (var ch in d.Changed)
                foreach (var p in ch.Differences)
                    Rows.Add(new CvlCompareRow("Value", d.CvlId, ch.Key, p.Property, p.Left, p.Right));
        }

        RebuildBuckets();
    }

    /// <summary>Promote <paramref name="row"/> left→right. CVL-bucket rows sync the whole CVL;
    /// Value-bucket rows touch a single value (pre-checked against parent CVL existence).</summary>
    [RelayCommand]
    public Task ApplyLeftToRightAsync(CvlCompareRow? row) =>
        ApplyRowAsync(row, sourceFromLeft: true);

    [RelayCommand]
    public Task ApplyRightToLeftAsync(CvlCompareRow? row) =>
        ApplyRowAsync(row, sourceFromLeft: false);

    private async Task ApplyRowAsync(CvlCompareRow? row, bool sourceFromLeft)
    {
        if (row is null) return;
        if (LeftEnv is null || RightEnv is null) { Status = "Pick both environments first."; return; }
        if (_leftSnapshot is null || _rightSnapshot is null) { Status = "Run a compare first."; return; }

        var sourceSnapshot = sourceFromLeft ? _leftSnapshot : _rightSnapshot;
        var targetSnapshot = sourceFromLeft ? _rightSnapshot : _leftSnapshot;
        var targetEnv = sourceFromLeft ? RightEnv : LeftEnv;
        var label = sourceFromLeft ? "left→right" : "right→left";

        var targetSecret = _vault.GetSecret(targetEnv.Id);
        if (targetSecret is null || string.IsNullOrEmpty(targetSecret.ApiKey))
        { Status = $"No API key on file for target '{targetEnv.Name}'."; return; }

        Busy = true;
        try
        {
            if (_main.ConnectedEnv?.Id != targetEnv.Id)
            {
                Status = $"Connecting to '{targetEnv.Name}'…";
                await _shell.SwitchEnvAsync(targetEnv, targetSecret).ConfigureAwait(true);
            }

            if (row.Bucket == "CVL")
            {
                // Whole-CVL sync via CvlSync — handles definition + values with correct ordering.
                Status = $"Promoting CVL '{row.CvlId}' {label} → '{targetEnv.Name}'…";
                var results = await _shell.SyncCvlsAsync(
                    source: sourceSnapshot,
                    cvlIds: new[] { row.CvlId },
                    allowDeactivate: false,
                    dryRun: false).ConfigureAwait(true);

                var errors = results.SelectMany(r => r.Errors).ToList();
                if (errors.Count == 0)
                {
                    var added = results.Sum(r => r.Added);
                    var updated = results.Sum(r => r.Updated);
                    _log.Success("CvlCompare", $"Promoted CVL '{row.CvlId}' {label}: +{added}, ~{updated}.");
                    Status = $"Promoted CVL '{row.CvlId}' to '{targetEnv.Name}' (+{added}, ~{updated}).";
                }
                else
                {
                    Status = $"Promote CVL '{row.CvlId}' had errors: {string.Join("; ", errors)}";
                    _log.Error("CvlCompare", Status);
                }
            }
            else // "Value"
            {
                // Single-value promote. Parent CVL must exist on target — abort with a clear hint
                // rather than silently auto-create.
                var parentOnTarget = targetSnapshot.Cvls.Any(c => string.Equals(c.Id, row.CvlId, StringComparison.OrdinalIgnoreCase));
                if (!parentOnTarget)
                {
                    Status = $"Parent CVL '{row.CvlId}' does not exist on '{targetEnv.Name}'. Promote the parent CVL row first.";
                    _log.Warn("CvlCompare", Status);
                    return;
                }

                var sourceCvl = sourceSnapshot.Cvls.FirstOrDefault(c => string.Equals(c.Id, row.CvlId, StringComparison.OrdinalIgnoreCase));
                var sourceValue = sourceCvl?.Values.FirstOrDefault(v => string.Equals(v.Key, row.Key, StringComparison.OrdinalIgnoreCase));

                if (sourceValue is null)
                {
                    // Key absent on source → promoting means "delete on target".
                    Status = $"Deleting CVL value '{row.CvlId}/{row.Key}' on '{targetEnv.Name}'…";
                    await _shell.DeleteCvlValueAsync(row.CvlId, row.Key).ConfigureAwait(true);
                    _log.Success("CvlCompare", $"Deleted CVL value '{row.CvlId}/{row.Key}' on '{targetEnv.Name}'.");
                    Status = $"Deleted CVL value '{row.CvlId}/{row.Key}' on '{targetEnv.Name}'.";
                }
                else
                {
                    Status = $"Promoting CVL value '{row.CvlId}/{row.Key}' {label} → '{targetEnv.Name}'…";
                    await _shell.ApplyCvlValueAsync(row.CvlId, sourceValue).ConfigureAwait(true);
                    _log.Success("CvlCompare", $"Promoted CVL value '{row.CvlId}/{row.Key}' {label}.");
                    Status = $"Promoted CVL value '{row.CvlId}/{row.Key}' to '{targetEnv.Name}'.";
                }
            }
        }
        catch (Exception ex)
        {
            Status = "Promote failed: " + ex.Message;
            _log.Error("CvlCompare", ex.Message, ex);
        }
        finally { Busy = false; }

        // Re-run the compare so rows reflect the new state.
        await CompareAsync().ConfigureAwait(true);
    }

    private void RebuildBuckets()
    {
        var max = 0;
        var groups = Rows.GroupBy(r => r.Bucket)
                         .Select(g => (Title: g.Key, Count: g.Count()))
                         .OrderByDescending(t => t.Count)
                         .ToList();
        foreach (var g in groups) if (g.Count > max) max = g.Count;
        foreach (var g in groups)
            Counts.Add(new ConceptDiffCount(g.Title, g.Count, max == 0 ? 0 : (double)g.Count / max));
    }
}

/// <summary>One row in the CVL-compare grid. <see cref="Bucket"/> is "CVL" for CVL-level diffs
/// (existence or definition) and "Value" for per-key value diffs.</summary>
public sealed record CvlCompareRow(string Bucket, string CvlId, string Key, string Property, string LeftValue, string RightValue);
