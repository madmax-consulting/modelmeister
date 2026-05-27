using System;
using System.Collections;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using ModelMeister.Inriver.WorkAreas;
using ModelMeister.Ui.Models;
using ModelMeister.Ui.Services;

namespace ModelMeister.Ui.ViewModels;

/// <summary>
/// "Compare work-areas" page. Captures shared folders from two environments (matched by tree path),
/// shows only the rows that differ — present on one side only, or a folder whose saved query / kind
/// differs — and promotes the source env's folders (with their queries, faithfully) into the target.
/// Identical folders are filtered out per the compare-page convention; sides are named environment
/// pills rather than left/right.
/// </summary>
public partial class WorkAreaCompareViewModel : ViewModelBase, ICompareViewModel
{
    private readonly MainWindowViewModel _main;
    private readonly Shell _shell;
    private readonly IEnvironmentVault _vault;
    private readonly IAppLog _log;

    public ObservableCollection<EnvironmentEntry> AvailableEnvs { get; } = [];
    public ObservableCollection<WorkAreaDiffRow> Rows { get; } = [];
    public ObservableCollection<ConceptDiffCount> Counts { get; } = [];

    [ObservableProperty] private EnvironmentEntry? _leftEnv;
    [ObservableProperty] private EnvironmentEntry? _rightEnv;
    [ObservableProperty] private bool _busy;
    [ObservableProperty] private string _status = "Pick two environments to compare shared work-area folders.";
    [ObservableProperty] private string _summary = "";
    [ObservableProperty] private bool _hasRows;
    [ObservableProperty] private string _leftColumnHeader = "";
    [ObservableProperty] private string _rightColumnHeader = "";
    [ObservableProperty] private string? _leftColumnStage;
    [ObservableProperty] private string? _rightColumnStage;

    public IAsyncRelayCommand SaveCsvCommand { get; }
    public IAsyncRelayCommand CopyMarkdownCommand { get; }
    public IReadOnlyList<CompareAction> ExtraActions { get; }
    public BucketToggleState Buckets { get; } = new();
    BucketToggleState? ICompareViewModel.Buckets => Buckets;
    public string BucketPath => nameof(WorkAreaDiffRow.Bucket);

    private IReadOnlyList<WorkAreaFolderDto>? _leftCapture;
    private IReadOnlyList<WorkAreaFolderDto>? _rightCapture;

    public WorkAreaCompareViewModel(MainWindowViewModel main, Shell shell, IEnvironmentVault vault, IAppLog log)
    {
        _main = main;
        _shell = shell;
        _vault = vault;
        _log = log;
        _vault.Changed += RefreshEnvList;
        RefreshEnvList();

        SaveCsvCommand = CompareCommands.MakeSaveCsv(() => Rows, BuildExportColumns, "workareas-compare.csv", _log, "CompareWorkAreas");
        CopyMarkdownCommand = CompareCommands.MakeCopyMarkdown(() => Rows, BuildExportColumns, _log, "CompareWorkAreas");

        ExtraActions = new[]
        {
            new CompareAction("Promote folders →", Primary: true, PromoteCommand),
        };
    }

    private IReadOnlyList<CompareExport.Column> BuildExportColumns() =>
        new CompareExport.Column[]
        {
            new("Path", r => ((WorkAreaDiffRow)r).Path),
            new("Kind", r => ((WorkAreaDiffRow)r).Kind),
            new(string.IsNullOrEmpty(LeftColumnHeader) ? "Left" : LeftColumnHeader, r => ((WorkAreaDiffRow)r).LeftCell),
            new(string.IsNullOrEmpty(RightColumnHeader) ? "Right" : RightColumnHeader, r => ((WorkAreaDiffRow)r).RightCell),
            new("Detail", r => ((WorkAreaDiffRow)r).Detail),
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
    }

    partial void OnLeftEnvChanged(EnvironmentEntry? value)
    {
        _leftCapture = null;
        LeftColumnHeader = value?.Name ?? "";
        LeftColumnStage = value?.TypeKey;
        TryAutoCompare();
    }

    partial void OnRightEnvChanged(EnvironmentEntry? value)
    {
        _rightCapture = null;
        RightColumnHeader = value?.Name ?? "";
        RightColumnStage = value?.TypeKey;
        TryAutoCompare();
    }

    private void TryAutoCompare()
    {
        if (Busy || LeftEnv is null || RightEnv is null) return;
        if (LeftEnv.Id == RightEnv.Id)
        {
            Status = "Pick two different environments.";
            Rows.Clear(); Counts.Clear(); HasRows = false; Summary = "";
            return;
        }
        _ = CompareAsync();
    }

    [RelayCommand(AllowConcurrentExecutions = true)]
    public async Task CompareAsync()
    {
        if (Busy) return;
        if (LeftEnv is null || RightEnv is null) { Status = "Pick both environments first."; return; }
        if (LeftEnv.Id == RightEnv.Id) { Status = "Pick two different environments."; return; }

        var leftSecret = _vault.GetSecret(LeftEnv.Id);
        var rightSecret = _vault.GetSecret(RightEnv.Id);
        if (leftSecret is null || string.IsNullOrEmpty(leftSecret.ApiKey)) { Status = $"No API key on file for '{LeftEnv.Name}'."; return; }
        if (rightSecret is null || string.IsNullOrEmpty(rightSecret.ApiKey)) { Status = $"No API key on file for '{RightEnv.Name}'."; return; }

        Busy = true;
        Rows.Clear(); Counts.Clear(); HasRows = false; Summary = "";
        try
        {
            Status = $"Capturing work-areas from '{LeftEnv.Name}'…";
            _leftCapture = await _shell.CaptureWorkAreasFromEnvAsync(LeftEnv, leftSecret).ConfigureAwait(true);

            Status = $"Capturing work-areas from '{RightEnv.Name}'…";
            _rightCapture = await _shell.CaptureWorkAreasFromEnvAsync(RightEnv, rightSecret).ConfigureAwait(true);

            RecomputeDelta();
            _log.Success("CompareWorkAreas", $"Compared '{LeftEnv.Name}' vs '{RightEnv.Name}': {Rows.Count} difference(s).");
        }
        catch (Exception ex)
        {
            Status = "Compare failed: " + ex.Message;
            _log.Error("CompareWorkAreas", ex.Message, ex);
        }
        finally { Busy = false; }
    }

    private void RecomputeDelta()
    {
        Rows.Clear();
        Counts.Clear();
        Buckets.Reset(Counts);

        if (_leftCapture is null || _rightCapture is null) { Summary = ""; HasRows = false; return; }

        var left = _leftCapture.ToDictionary(f => f.Path, StringComparer.OrdinalIgnoreCase);
        var right = _rightCapture.ToDictionary(f => f.Path, StringComparer.OrdinalIgnoreCase);
        var allPaths = left.Keys.Union(right.Keys, StringComparer.OrdinalIgnoreCase).OrderBy(p => p, StringComparer.OrdinalIgnoreCase);

        var onlyLeftLabel = $"Only in {LeftColumnHeader}";
        var onlyRightLabel = $"Only in {RightColumnHeader}";
        const string differentLabel = "Different";
        int identical = 0;

        foreach (var path in allPaths)
        {
            var inLeft = left.TryGetValue(path, out var l);
            var inRight = right.TryGetValue(path, out var r);

            if (inLeft && inRight)
            {
                if (SameFolder(l!, r!)) { identical++; continue; }
                Rows.Add(new WorkAreaDiffRow(path, KindOf(l!), differentLabel, l, r, DescribeDiff(l!, r!)));
            }
            else if (inLeft)
            {
                Rows.Add(new WorkAreaDiffRow(path, KindOf(l!), onlyLeftLabel, l, null, $"only in {LeftColumnHeader}"));
            }
            else
            {
                Rows.Add(new WorkAreaDiffRow(path, KindOf(r!), onlyRightLabel, null, r, $"only in {RightColumnHeader}"));
            }
        }

        HasRows = Rows.Count > 0;
        RebuildCounts();
        Summary = Rows.Count == 0
            ? $"No differences. ({identical} folders identical.)"
            : $"{Rows.Count} difference(s) · {identical} identical";
        Status = "Comparison complete.";
    }

    private static bool SameFolder(WorkAreaFolderDto a, WorkAreaFolderDto b) =>
        a.IsQuery == b.IsQuery
        && a.IsSyndication == b.IsSyndication
        && string.Equals(a.QueryJson ?? "", b.QueryJson ?? "", StringComparison.Ordinal);

    private static string KindOf(WorkAreaFolderDto f) => f.IsSyndication ? "syndication" : f.IsQuery ? "query" : "folder";

    private static string DescribeDiff(WorkAreaFolderDto l, WorkAreaFolderDto r)
    {
        if (l.IsQuery != r.IsQuery) return l.IsQuery ? "query here, plain folder on the other side" : "plain folder here, query on the other side";
        if (l.IsSyndication != r.IsSyndication) return "syndication flag differs";
        if (!string.Equals(l.QueryJson ?? "", r.QueryJson ?? "", StringComparison.Ordinal)) return "saved query differs";
        return "differs";
    }

    private void RebuildCounts()
    {
        var max = 0;
        var groups = Rows.GroupBy(r => r.Bucket)
                         .Select(g => (Title: g.Key, Count: g.Count()))
                         .OrderByDescending(t => t.Count)
                         .ToList();
        foreach (var g in groups) if (g.Count > max) max = g.Count;
        foreach (var g in groups)
            Counts.Add(new ConceptDiffCount(g.Title, g.Count, max == 0 ? 0 : (double)g.Count / max));
        Buckets.Reset(Counts);
    }

    private bool CanPromote() => !Busy && LeftEnv is not null && RightEnv is not null && LeftEnv.Id != RightEnv.Id;

    /// <summary>Faithfully copy the source (left) env's shared folders + queries into the target (right) env.</summary>
    [RelayCommand(CanExecute = nameof(CanPromote))]
    public async Task PromoteAsync()
    {
        if (LeftEnv is null || RightEnv is null) return;
        var sourceSecret = _vault.GetSecret(LeftEnv.Id);
        var targetSecret = _vault.GetSecret(RightEnv.Id);
        if (sourceSecret is null || string.IsNullOrEmpty(sourceSecret.ApiKey)) { Status = $"No API key on file for '{LeftEnv.Name}'."; return; }
        if (targetSecret is null || string.IsNullOrEmpty(targetSecret.ApiKey)) { Status = $"No API key on file for '{RightEnv.Name}'."; return; }

        var ok = await DialogHost.ConfirmPromoteAsync(
            "work-area folders", $"all shared folders from {LeftEnv.Name}",
            LeftEnv.Name, RightEnv.Name, RightEnv.TypeKey).ConfigureAwait(true);
        if (!ok) return;

        Busy = true;
        Status = $"Promoting work-area folders '{LeftEnv.Name}' → '{RightEnv.Name}'…";
        try
        {
            var result = await _shell.PromoteWorkAreasAsync(LeftEnv, sourceSecret, RightEnv, targetSecret, allowDeletes: false).ConfigureAwait(true);
            Status = $"Promoted · created {result.Created}, updated {result.Updated}" + (result.Failed > 0 ? $", {result.Failed} failed" : "");
            _log.Success("CompareWorkAreas", Status);
        }
        catch (Exception ex)
        {
            Status = "Promote failed: " + ex.Message;
            _log.Error("CompareWorkAreas", ex.Message, ex);
        }
        finally { Busy = false; }

        await CompareAsync().ConfigureAwait(true);
    }
}

/// <summary>One differing row in the work-area compare grid.</summary>
public sealed class WorkAreaDiffRow
{
    public WorkAreaDiffRow(string path, string kind, string bucket, WorkAreaFolderDto? left, WorkAreaFolderDto? right, string detail)
    {
        Path = path;
        Kind = kind;
        Bucket = bucket;
        Left = left;
        Right = right;
        Detail = detail;
    }

    public string Path { get; }
    public string Kind { get; }
    /// <summary>Bucket title used by the bottom bar chart + toggle filter (env-named).</summary>
    public string Bucket { get; }
    public WorkAreaFolderDto? Left { get; }
    public WorkAreaFolderDto? Right { get; }
    public string Detail { get; }

    public bool LeftPresent => Left is not null;
    public bool RightPresent => Right is not null;
    public string LeftCell => Left is null ? "—" : (Left.IsSyndication ? "syndication" : Left.IsQuery ? "query" : "folder");
    public string RightCell => Right is null ? "—" : (Right.IsSyndication ? "syndication" : Right.IsQuery ? "query" : "folder");
}
