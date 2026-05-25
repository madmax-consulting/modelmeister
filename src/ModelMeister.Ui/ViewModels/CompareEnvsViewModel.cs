using System;
using System.Collections;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using ModelMeister.Inriver.Diff;
using ModelMeister.Inriver.Snapshot;
using ModelMeister.Model.Loading;
using ModelMeister.Ui.Models;
using ModelMeister.Ui.Services;

namespace ModelMeister.Ui.ViewModels;

/// <summary>
/// Compare-environments view-model. Two env dropdowns sourced from the vault; selecting both
/// captures a live snapshot from each (sequentially, via <see cref="Shell.SwitchEnvAsync"/>) and
/// computes the diff. The active connection ends up bound to the right-side env afterwards.
/// </summary>
public partial class CompareEnvsViewModel : ViewModelBase, ICompareViewModel
{
    readonly MainWindowViewModel _main;
    readonly Shell _shell;
    readonly IAppLog _log;
    readonly IEnvironmentVault _vault;

    public ObservableCollection<EnvironmentEntry> AvailableEnvs { get; } = [];
    public ObservableCollection<DiffLineRow> Rows { get; } = [];
    /// <summary>Per-concept diff counts shown as a small bar chart at the bottom.</summary>
    public ObservableCollection<ConceptDiffCount> Counts { get; } = [];

    [ObservableProperty] private EnvironmentEntry? _leftEnv;
    [ObservableProperty] private EnvironmentEntry? _rightEnv;
    [ObservableProperty] private bool _busy;
    [ObservableProperty] private string _status = "Pick two environments to compare.";
    [ObservableProperty] private string _summary = "";
    [ObservableProperty] private EnvironmentDiff? _diff;
    /// <summary>True when at least one diff row exists.</summary>
    [ObservableProperty] private bool _hasRows;
    /// <summary>Header label for the left-value column = left env name (e.g. "Acme-Test").</summary>
    [ObservableProperty] private string _leftColumnHeader = "";
    /// <summary>Header label for the right-value column = right env name.</summary>
    [ObservableProperty] private string _rightColumnHeader = "";
    /// <summary>Stage of the left env (for the pill in the value-column header).</summary>
    [ObservableProperty] private EnvironmentStage _leftColumnStage;
    /// <summary>Stage of the right env (for the pill in the value-column header).</summary>
    [ObservableProperty] private EnvironmentStage _rightColumnStage;

    public IAsyncRelayCommand SaveCsvCommand { get; }
    public IAsyncRelayCommand CopyMarkdownCommand { get; }
    public IReadOnlyList<CompareAction> ExtraActions { get; }
    /// <summary>Bucket-bar toggle state: clicking a bar in the bottom chart hides that Concept's rows.</summary>
    public BucketToggleState Buckets { get; } = new();
    public string BucketPath => nameof(DiffLineRow.Concept);

    /// <summary>When true, promoting may also delete concepts that exist only on the target. Off by default.</summary>
    [ObservableProperty] private bool _allowDeletesOnPromote;

    /// <summary>Current multi-selection, fed by the grid via <see cref="MultiSelectBehavior"/> for bulk promote.</summary>
    public IList SelectedRows { get; } = new List<object>();

    private LiveModel? _leftSnapshot;
    private LiveModel? _rightSnapshot;
    // Promotion is one-way (left = source); the left snapshot is scaffolded+built into a model and
    // cached, rebuilt on each Compare run and on env change (see CompareAsync / OnLeftEnvChanged).
    private LoadedModel? _leftLoaded;

    public CompareEnvsViewModel(MainWindowViewModel main, Shell shell, IAppLog log)
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
            suggestedFileName: "model-compare.csv",
            log: _log,
            logSource: "Compare");

        CopyMarkdownCommand = CompareCommands.MakeCopyMarkdown(
            () => Rows,
            BuildExportColumns,
            log: _log,
            logSource: "Compare");

        ExtraActions = new[]
        {
            new CompareAction("Promote selected →", Primary: true, PromoteSelectedCommand),
        };
    }

    private IReadOnlyList<CompareExport.Column> BuildExportColumns() =>
        new CompareExport.Column[]
        {
            new("Concept",  r => ((DiffLineRow)r).Concept),
            new("CVL",      r => ((DiffLineRow)r).Cvl),
            new("Id",       r => ((DiffLineRow)r).Id),
            new("Property", r => ((DiffLineRow)r).Property),
            new(string.IsNullOrEmpty(LeftColumnHeader)  ? "Left"  : LeftColumnHeader,  r => ((DiffLineRow)r).LeftValue),
            new(string.IsNullOrEmpty(RightColumnHeader) ? "Right" : RightColumnHeader, r => ((DiffLineRow)r).RightValue),
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

        // Refresh value-column headers in case an env was just renamed.
        if (LeftEnv is not null) { LeftColumnHeader = LeftEnv.Name; LeftColumnStage = LeftEnv.Stage; }
        if (RightEnv is not null) { RightColumnHeader = RightEnv.Name; RightColumnStage = RightEnv.Stage; }
    }

    partial void OnLeftEnvChanged(EnvironmentEntry? value)
    {
        _leftSnapshot = null;
        _leftLoaded = null;
        LeftColumnHeader = value?.Name ?? "";
        LeftColumnStage = value?.Stage ?? EnvironmentStage.Unspecified;
        TryAutoCompare();
    }

    partial void OnRightEnvChanged(EnvironmentEntry? value)
    {
        _rightSnapshot = null;
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
        {
            Status = $"No API key on file for '{LeftEnv.Name}'."; return;
        }
        if (rightSecret is null || string.IsNullOrEmpty(rightSecret.ApiKey))
        {
            Status = $"No API key on file for '{RightEnv.Name}'."; return;
        }

        Busy = true;
        Rows.Clear();
        Counts.Clear();
        HasRows = false;
        Summary = "";
        // The source-side built model is tied to a specific snapshot; a fresh compare invalidates it.
        _leftLoaded = null;
        try
        {
            Status = $"Connecting to '{LeftEnv.Name}'…";
            await _shell.SwitchEnvAsync(LeftEnv, leftSecret).ConfigureAwait(true);
            Status = $"Capturing snapshot from '{LeftEnv.Name}'…";
            _leftSnapshot = await _shell.CaptureSnapshotAsync().ConfigureAwait(true);

            Status = $"Connecting to '{RightEnv.Name}'…";
            await _shell.SwitchEnvAsync(RightEnv, rightSecret).ConfigureAwait(true);
            Status = $"Capturing snapshot from '{RightEnv.Name}'…";
            _rightSnapshot = await _shell.CaptureSnapshotAsync().ConfigureAwait(true);

            Status = "Computing differences…";
            var diff = await _shell.CompareSnapshotsAsync(_leftSnapshot, _rightSnapshot).ConfigureAwait(true);
            Diff = diff;
            PopulateRows(diff);
            HasRows = Rows.Count > 0;

            Summary = HasRows
                ? $"{Rows.Count} differences across {Counts.Count} concept(s)"
                : "No differences.";
            Status = "";
            _log.Success("Compare", $"Compared '{LeftEnv.Name}' vs '{RightEnv.Name}': {diff.TotalDifferences} differences.");
        }
        catch (Exception ex)
        {
            Status = "Compare failed: " + ex.Message;
            _log.Error("Compare", ex.Message, ex);
        }
        finally { Busy = false; }
    }

    private void PopulateRows(EnvironmentDiff diff)
    {
        Rows.Clear();
        Counts.Clear();
        Buckets.Reset(Counts);

        AddSetSection("Languages", diff.Languages);
        AddSetSection("Entity types", diff.EntityTypes);
        AddSetSection("CVLs", diff.Cvls);
        AddSetSection("Categories", diff.Categories);
        AddSetSection("Fieldsets", diff.Fieldsets);
        AddSetSection("Link types", diff.LinkTypes);
        AddSetSection("Roles", diff.Roles);
        AddSetSection("Field types", diff.FieldTypes);

        foreach (var c in diff.ChangedEntityTypes)
            foreach (var d in c.Differences)
                Rows.Add(new DiffLineRow("Entity type", "", c.EntityTypeId, d.Property, d.Left, d.Right));
        foreach (var c in diff.ChangedFields)
            foreach (var d in c.Differences)
                Rows.Add(new DiffLineRow("Field", "", $"{c.EntityTypeId}.{c.FieldId}", d.Property, d.Left, d.Right));
        foreach (var c in diff.ChangedCvls)
            foreach (var d in c.Differences)
                Rows.Add(new DiffLineRow("CVL", c.CvlId, c.CvlId, d.Property, d.Left, d.Right));
        foreach (var c in diff.ChangedLinkTypes)
            foreach (var d in c.Differences)
                Rows.Add(new DiffLineRow("Link type", "", c.LinkTypeId, d.Property, d.Left, d.Right));

        foreach (var d in diff.CvlValueChanges)
        {
            foreach (var key in d.OnlyInLeft) Rows.Add(new DiffLineRow("CVL value", d.CvlId, key, "presence", key, ""));
            foreach (var key in d.OnlyInRight) Rows.Add(new DiffLineRow("CVL value", d.CvlId, key, "presence", "", key));
            foreach (var ch in d.Changed) Rows.Add(new DiffLineRow("CVL value", d.CvlId, ch.Key, "value/parent/deactivated", "changed", "changed"));
        }

        RebuildConceptDiffCounts();
    }

    private void AddSetSection(string concept, ConceptDelta<string> delta)
    {
        if (delta.Total == 0) return;
        foreach (var id in delta.OnlyInLeft) Rows.Add(new DiffLineRow(concept, "", id, "presence", id, ""));
        foreach (var id in delta.OnlyInRight) Rows.Add(new DiffLineRow(concept, "", id, "presence", "", id));
    }

    // ---------------- Promotion (left → right only; swap envs to go the other way) ----------------

    /// <summary>Promote the row's whole concept from the left env into the right env
    /// (entity type, field, CVL, …). To promote the other direction, swap the environments.</summary>
    [RelayCommand]
    public Task ApplyAsync(DiffLineRow? row) =>
        row is null ? Task.CompletedTask : PromoteAsync(new[] { row });

    /// <summary>Promote every selected row left→right in a single diff/apply pass.</summary>
    [RelayCommand]
    public Task PromoteSelectedAsync() =>
        PromoteAsync(SelectedRows.OfType<DiffLineRow>().ToList());

    /// <summary>Map a diff row to the concept it promotes. Returns null for non-promotable rows (Languages, Roles).</summary>
    private static PromoteScope? ScopeFor(DiffLineRow row) => row.Concept switch
    {
        "Entity type" or "Entity types" => new PromoteScope(PromoteConcept.EntityType, row.Id),
        "Field" or "Field types" => FieldScope(row),
        "CVL" or "CVLs" => new PromoteScope(PromoteConcept.Cvl, row.Id),
        "CVL value" => new PromoteScope(PromoteConcept.CvlValue, row.Cvl, CvlKey: row.Id),
        "Category" or "Categories" => new PromoteScope(PromoteConcept.Category, row.Id),
        "Fieldset" or "Fieldsets" => new PromoteScope(PromoteConcept.Fieldset, row.Id),
        "Link type" or "Link types" => new PromoteScope(PromoteConcept.LinkType, row.Id),
        _ => null,
    };

    private static PromoteScope FieldScope(DiffLineRow row)
    {
        // Changed-field rows carry "Entity.Field"; presence rows carry the bare field id.
        var dot = row.Id.IndexOf('.');
        return dot < 0
            ? new PromoteScope(PromoteConcept.Field, row.Id)
            : new PromoteScope(PromoteConcept.Field, row.Id[(dot + 1)..], EntityTypeId: row.Id[..dot]);
    }

    private async Task PromoteAsync(IReadOnlyList<DiffLineRow> rows)
    {
        if (LeftEnv is null || RightEnv is null) { Status = "Pick both environments first."; return; }
        if (_leftSnapshot is null || _rightSnapshot is null) { Status = "Run a compare first."; return; }
        if (rows.Count == 0) { Status = "Select at least one row to promote."; return; }

        var scopes = new List<PromoteScope>();
        var skipped = 0;
        foreach (var r in rows)
        {
            if (ScopeFor(r) is { } s) scopes.Add(s);
            else skipped++;
        }
        if (scopes.Count == 0) { Status = "None of the selected rows can be promoted here (Languages/Roles use their own pages)."; return; }

        // Promotion is one-way: left (source) → right (target). Swap the environments to go the other way.
        var sourceSnapshot = _leftSnapshot;
        var targetSnapshot = _rightSnapshot;
        var targetEnv = RightEnv;
        var label = "left→right";

        var targetSecret = _vault.GetSecret(targetEnv.Id);
        if (targetSecret is null || string.IsNullOrEmpty(targetSecret.ApiKey))
        { Status = $"No API key on file for target '{targetEnv.Name}'."; return; }

        // Pre-flight: a field can't land on a target that lacks its owning entity type.
        foreach (var s in scopes)
        {
            if (s.Concept == PromoteConcept.Field && s.EntityTypeId is { } et &&
                !targetSnapshot.EntityTypes.Any(e => string.Equals(e.Id, et, StringComparison.OrdinalIgnoreCase)))
            {
                Status = $"Entity type '{et}' is missing on '{targetEnv.Name}'. Promote the entity type first.";
                _log.Warn("Compare", Status);
                return;
            }
        }

        Busy = true;
        try
        {
            var sourceLoaded = _leftLoaded;
            if (sourceLoaded is null)
            {
                Status = "Preparing source model… (scaffolding + building)";
                sourceLoaded = await _shell.LoadModelFromLiveAsync(sourceSnapshot).ConfigureAwait(true);
                _leftLoaded = sourceLoaded;
            }

            if (_main.ConnectedEnv?.Id != targetEnv.Id)
            {
                Status = $"Connecting to '{targetEnv.Name}'…";
                await _shell.SwitchEnvAsync(targetEnv, targetSecret).ConfigureAwait(true);
            }

            var policy = new MergePolicy
            {
                OverwriteNamesAndDescriptions = true,
                OverwriteCvlValues = true,
                AllowDeletes = AllowDeletesOnPromote,
            };

            var stamp = DateTime.UtcNow.ToString("yyyyMMdd-HHmmss");
            var backupPath = Path.Combine(
                _shell.GetBackupsDir(targetSnapshot.EnvironmentUrl, Paths.AppDataDir),
                $"{stamp}.promote.model.json");

            Status = $"Promoting {scopes.Count} concept(s) {label} → '{targetEnv.Name}'…";
            var receipt = await _shell.PromoteConceptsAsync(sourceLoaded, scopes, policy, backupPath).ConfigureAwait(true);

            var note = skipped > 0 ? $" ({skipped} row(s) skipped)" : "";
            if (receipt.Failed == 0)
            {
                Status = receipt.Succeeded == 0
                    ? $"Nothing to promote — target already matches the selected concept(s).{note}"
                    : $"Promoted {receipt.Succeeded} change(s) {label} → '{targetEnv.Name}'.{note}";
                _log.Success("Compare", Status);
            }
            else
            {
                Status = $"Promote finished: {receipt.Succeeded} applied, {receipt.Failed} failed — see log.{note}";
                _log.Error("Compare", Status);
            }
        }
        catch (Exception ex)
        {
            Status = "Promote failed: " + ex.Message;
            _log.Error("Compare", ex.Message, ex);
        }
        finally { Busy = false; }

        // Re-run the compare so rows reflect the new state (also rebuilds the source models).
        await CompareAsync().ConfigureAwait(true);
    }

    private void RebuildConceptDiffCounts()
    {
        var max = 0;
        var groups = Rows.GroupBy(r => r.Concept)
                         .Select(g => (Title: g.Key, Count: g.Count()))
                         .OrderByDescending(t => t.Count)
                         .ToList();
        foreach (var g in groups) if (g.Count > max) max = g.Count;
        foreach (var g in groups)
            Counts.Add(new ConceptDiffCount(g.Title, g.Count, max == 0 ? 0 : (double)g.Count / max));
    }
}

/// <summary>One row in the compare-envs grid. <see cref="Cvl"/> is the owning CVL id for CVL-value
/// rows (and the CVL itself for CVL-definition rows), empty for non-CVL concepts. <see cref="Id"/> is
/// the bare key (no <c>Cvl/key</c> concatenation), so its filter only matches the value key.</summary>
public sealed record DiffLineRow(string Concept, string Cvl, string Id, string Property, string LeftValue, string RightValue);
