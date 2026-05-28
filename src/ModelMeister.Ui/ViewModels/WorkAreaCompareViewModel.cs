using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using ModelMeister.Inriver.Users;
using ModelMeister.Inriver.WorkAreas;
using ModelMeister.Inriver.WorkAreas.Query;
using ModelMeister.Ui.Models;
using ModelMeister.Ui.Services;

namespace ModelMeister.Ui.ViewModels;

/// <summary>
/// "Compare work-areas" page. Captures folders from two environments (matched by tree path), shows only the
/// rows that differ — present on one side only, or a folder whose saved query / kind differs — and promotes
/// the source (left) env's folders into the target (right). Identical folders are hidden per the compare-page
/// convention; sides are named environment pills. Promotion is selectable (per-row or checked rows) and the
/// "Promote all" path can optionally prune target-only folders. Saved-query differences are shown field by
/// field, and a source query that references ids missing from the target env is flagged.
/// </summary>
public partial class WorkAreaCompareViewModel : ViewModelBase, ICompareViewModel
{
    private readonly MainWindowViewModel _main;
    private readonly Shell _shell;
    private readonly IEnvironmentVault _vault;
    private readonly IAppLog _log;

    protected MainWindowViewModel Main => _main;
    protected Shell ShellSvc => _shell;
    protected IAppLog Log => _log;

    /// <summary>Personal scope username (null = shared). Overridden by the personal compare page.</summary>
    protected virtual string? PersonalUsername => null;

    /// <summary>Whether the compare scope is ready to run (shared: always; personal: a user is picked).</summary>
    protected virtual bool ScopeReady => true;
    /// <summary>Status shown when the scope isn't ready (personal: prompts to pick a user).</summary>
    protected virtual string ScopeNotReadyMessage => "";

    /// <summary>Whether a "pick a user" dropdown is shown (personal page only).</summary>
    public virtual bool ShowUserPicker => false;
    /// <summary>Candidate users for the personal-scope picker (empty for shared).</summary>
    public ObservableCollection<UserSummary> Users { get; } = [];
    [ObservableProperty] private UserSummary? _selectedUser;

    partial void OnSelectedUserChanged(UserSummary? value)
    {
        _leftCapture = null;
        _rightCapture = null;
        Rows.Clear(); Counts.Clear(); HasRows = false; Summary = "";
        TryAutoCompare();
    }

    public ObservableCollection<EnvironmentEntry> AvailableEnvs { get; } = [];
    public ObservableCollection<WorkAreaDiffRow> Rows { get; } = [];
    public ObservableCollection<ConceptDiffCount> Counts { get; } = [];

    /// <summary>Checkbox-selection model over <see cref="Rows"/>; backs the "Promote selected" command.</summary>
    public RowSelectionModel Selection { get; }

    [ObservableProperty] private EnvironmentEntry? _leftEnv;
    [ObservableProperty] private EnvironmentEntry? _rightEnv;
    [ObservableProperty] private bool _busy;
    [ObservableProperty] private string _status = "Pick two environments to compare work-area folders.";
    [ObservableProperty] private string _summary = "";
    [ObservableProperty] private bool _hasRows;
    [ObservableProperty] private string _leftColumnHeader = "";
    [ObservableProperty] private string _rightColumnHeader = "";
    [ObservableProperty] private string? _leftColumnStage;
    [ObservableProperty] private string? _rightColumnStage;
    /// <summary>When set, "Promote all" also removes folders that exist only in the target (a full mirror).</summary>
    [ObservableProperty] private bool _allowDeletes;

    public IAsyncRelayCommand SaveCsvCommand { get; }
    public IAsyncRelayCommand CopyMarkdownCommand { get; }
    public IReadOnlyList<CompareAction> ExtraActions { get; }
    public BucketToggleState Buckets { get; } = new();
    BucketToggleState? ICompareViewModel.Buckets => Buckets;
    public string BucketPath => nameof(WorkAreaDiffRow.Bucket);

    private IReadOnlyList<WorkAreaFolderDto>? _leftCapture;
    private IReadOnlyList<WorkAreaFolderDto>? _rightCapture;
    private QueryMetadata _rightMeta = QueryMetadata.Empty;

    public WorkAreaCompareViewModel(MainWindowViewModel main, Shell shell, IEnvironmentVault vault, IAppLog log)
    {
        _main = main;
        _shell = shell;
        _vault = vault;
        _log = log;
        _vault.Changed += RefreshEnvList;
        _main.ScopeChanged += RefreshEnvList;
        Selection = new RowSelectionModel(Rows);
        RefreshEnvList();

        SaveCsvCommand = CompareCommands.MakeSaveCsv(() => Rows, BuildExportColumns, "workareas-compare.csv", _log, "CompareWorkAreas");
        CopyMarkdownCommand = CompareCommands.MakeCopyMarkdown(() => Rows, BuildExportColumns, _log, "CompareWorkAreas");

        ExtraActions = new[]
        {
            new CompareAction("Promote selected →", Primary: true, PromoteSelectedCommand),
            new CompareAction("Promote all →", Primary: false, PromoteAllCommand),
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
        foreach (var e in _main.EnvironmentsInScope())
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
        if (Busy || !ScopeReady || LeftEnv is null || RightEnv is null) return;
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
        if (!ScopeReady) { Status = ScopeNotReadyMessage; return; }
        if (LeftEnv is null || RightEnv is null) { Status = "Pick both environments first."; return; }
        if (LeftEnv.Id == RightEnv.Id) { Status = "Pick two different environments."; return; }

        var leftSecret = _vault.GetSecret(LeftEnv.Id);
        var rightSecret = _vault.GetSecret(RightEnv.Id);
        if (leftSecret is null || string.IsNullOrEmpty(leftSecret.ApiKey)) { Status = $"No API key on file for '{LeftEnv.Name}'."; return; }
        if (rightSecret is null || string.IsNullOrEmpty(rightSecret.ApiKey)) { Status = $"No API key on file for '{RightEnv.Name}'."; return; }

        Busy = true;
        _main.SuspendConnectionIndicator = true;
        Rows.Clear(); Counts.Clear(); HasRows = false; Summary = "";
        try
        {
            Status = $"Capturing work-areas from '{LeftEnv.Name}'…";
            _leftCapture = await _shell.CaptureWorkAreasFromEnvAsync(LeftEnv, leftSecret, PersonalUsername).ConfigureAwait(true);

            Status = $"Capturing work-areas from '{RightEnv.Name}'…";
            _rightCapture = await _shell.CaptureWorkAreasFromEnvAsync(RightEnv, rightSecret, PersonalUsername).ConfigureAwait(true);

            // Connection is now on the right (target) env. Only pay for its model-id catalog when there are
            // query folders to validate — folder-only trees need no validity check. Best-effort + advisory.
            _rightMeta = QueryMetadata.Empty;
            if (_leftCapture.Any(f => f.IsQuery))
            {
                try { _rightMeta = await _shell.CaptureWorkAreaQueryMetadataAsync().ConfigureAwait(true); }
                catch { _rightMeta = QueryMetadata.Empty; }
            }

            RecomputeDelta();
            _log.Success("CompareWorkAreas", $"Compared '{LeftEnv.Name}' vs '{RightEnv.Name}': {Rows.Count} difference(s).");
        }
        catch (Exception ex)
        {
            Status = "Compare failed: " + ex.Message;
            _log.Error("CompareWorkAreas", ex.Message, ex);
        }
        finally { Busy = false; _main.SuspendConnectionIndicator = false; }
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
                Rows.Add(new WorkAreaDiffRow(path, KindOf(l!), differentLabel, l, r, DescribeDiff(l!, r!), Validity(l!)));
            }
            else if (inLeft)
            {
                Rows.Add(new WorkAreaDiffRow(path, KindOf(l!), onlyLeftLabel, l, null, $"only in {LeftColumnHeader}", Validity(l!)));
            }
            else
            {
                Rows.Add(new WorkAreaDiffRow(path, KindOf(r!), onlyRightLabel, null, r, $"only in {RightColumnHeader}", null));
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

    /// <summary>Human, field-level description of how the source (left) folder differs from the target (right).</summary>
    private static string DescribeDiff(WorkAreaFolderDto l, WorkAreaFolderDto r)
    {
        var parts = new List<string>();
        if (l.IsQuery != r.IsQuery)
            parts.Add(l.IsQuery ? "query here, plain folder on the other side" : "plain folder here, query on the other side");
        if (l.IsSyndication != r.IsSyndication)
            parts.Add("syndication flag differs");
        if (!string.Equals(l.QueryJson ?? "", r.QueryJson ?? "", StringComparison.Ordinal))
        {
            var lines = QueryDiff.Describe(l.QueryJson, r.QueryJson);
            parts.Add(lines.Count == 0 ? "saved query differs" : "query · " + string.Join(" · ", lines));
        }
        return parts.Count == 0 ? "differs" : string.Join(" · ", parts);
    }

    /// <summary>Warn when the source (left) folder's saved query references ids missing from the target env.</summary>
    private string? Validity(WorkAreaFolderDto l)
    {
        if (!l.IsQuery || string.IsNullOrWhiteSpace(l.QueryJson) || _rightMeta.IsEmpty) return null;
        var model = QueryMapper.ToModel(WorkAreaService.DeserializeQuery(l.QueryJson));
        var warnings = QueryValidator.Validate(model, _rightMeta);
        return warnings.Count == 0 ? null : string.Join(" · ", warnings);
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

    /// <summary>Promote a single folder (and its ancestor folders) from the source (left) env into the target.</summary>
    [RelayCommand]
    public Task ApplyAsync(WorkAreaDiffRow? row)
        => row is null || !row.CanPromoteLeftToRight ? Task.CompletedTask : PromoteAsync(new[] { row.Path }, prune: false, label: $"folder '{row.Path}'");

    /// <summary>Promote the checked folders (plus their ancestors) from source into target.</summary>
    [RelayCommand(CanExecute = nameof(CanPromote))]
    public Task PromoteSelectedAsync()
    {
        var paths = Selection.SelectedOf<WorkAreaDiffRow>().Where(r => r.CanPromoteLeftToRight).Select(r => r.Path).ToList();
        if (paths.Count == 0) { Status = "Select at least one folder present in the source."; return Task.CompletedTask; }
        return PromoteAsync(paths, prune: false, label: $"{paths.Count} selected folder(s)");
    }

    /// <summary>Promote every source folder into the target. When <see cref="AllowDeletes"/> is on, also prune
    /// folders that exist only in the target (a full mirror) — confirmed destructively first.</summary>
    [RelayCommand(CanExecute = nameof(CanPromote))]
    public Task PromoteAllAsync() => PromoteAsync(onlyPaths: null, prune: AllowDeletes, label: "all source folders");

    private async Task PromoteAsync(IReadOnlyList<string>? onlyPaths, bool prune, string label)
    {
        if (LeftEnv is null || RightEnv is null) return;
        var sourceSecret = _vault.GetSecret(LeftEnv.Id);
        var targetSecret = _vault.GetSecret(RightEnv.Id);
        if (sourceSecret is null || string.IsNullOrEmpty(sourceSecret.ApiKey)) { Status = $"No API key on file for '{LeftEnv.Name}'."; return; }
        if (targetSecret is null || string.IsNullOrEmpty(targetSecret.ApiKey)) { Status = $"No API key on file for '{RightEnv.Name}'."; return; }

        // When pruning would actually remove folders, require a destructive confirm that lists exactly what
        // gets deleted. With nothing to prune it's a plain promote, so use the normal promote confirmation.
        var targetOnly = prune ? TargetOnlyPaths() : [];
        if (prune && targetOnly.Count > 0)
        {
            var ok = await DialogHost.ConfirmBulkAsync(
                "Promote and prune work-area folders", "Promote + delete", "target-only folder",
                targetOnly, RightEnv.Name, RightEnv.TypeKey, destructive: true).ConfigureAwait(true);
            if (!ok) { Status = "Promote cancelled."; return; }
        }
        else
        {
            var ok = await DialogHost.ConfirmPromoteAsync(
                "work-area folders", label, LeftEnv.Name, RightEnv.Name, RightEnv.TypeKey).ConfigureAwait(true);
            if (!ok) { Status = "Promote cancelled."; return; }
        }

        Busy = true;
        _main.SuspendConnectionIndicator = true;
        Status = $"Promoting {label} '{LeftEnv.Name}' → '{RightEnv.Name}'…";
        try
        {
            var result = await _shell.PromoteWorkAreasAsync(
                LeftEnv, sourceSecret, RightEnv, targetSecret, allowDeletes: prune, onlyPaths: onlyPaths, personalUsername: PersonalUsername).ConfigureAwait(true);
            Status = $"Promoted · created {result.Created}, updated {result.Updated}"
                + (result.Deleted > 0 ? $", deleted {result.Deleted}" : "")
                + (result.Failed > 0 ? $", {result.Failed} failed" : "");
            _log.Success("CompareWorkAreas", Status);
            if (result.Deleted > 0)
                _log.Toast(LogLevel.Warn, "Pruned target-only folders", $"Deleted {result.Deleted} folder(s) from {RightEnv.Name}.");
            if (result.Failed > 0)
                _log.Toast(LogLevel.Error, "Promote had failures", $"{result.Failed} folder(s) failed — see log.");
        }
        catch (Exception ex)
        {
            Status = "Promote failed: " + ex.Message;
            _log.Error("CompareWorkAreas", ex.Message, ex);
        }
        finally { Busy = false; _main.SuspendConnectionIndicator = false; }

        await CompareAsync().ConfigureAwait(true);
    }

    /// <summary>Paths that exist only in the target (right) — what a prune would delete.</summary>
    private List<string> TargetOnlyPaths()
    {
        if (_leftCapture is null || _rightCapture is null) return [];
        var left = _leftCapture.Select(f => f.Path).ToHashSet(StringComparer.OrdinalIgnoreCase);
        return _rightCapture.Select(f => f.Path).Where(p => !left.Contains(p))
            .OrderByDescending(p => p.Count(c => c == '/')).ToList();
    }
}

/// <summary>One differing row in the work-area compare grid.</summary>
public sealed partial class WorkAreaDiffRow : SelectableRow
{
    public WorkAreaDiffRow(string path, string kind, string bucket, WorkAreaFolderDto? left, WorkAreaFolderDto? right, string detail, string? validityWarning)
    {
        Path = path;
        Kind = kind;
        Bucket = bucket;
        Left = left;
        Right = right;
        Detail = detail;
        ValidityWarning = validityWarning;
    }

    public string Path { get; }
    public string Kind { get; }
    /// <summary>Bucket title used by the bottom bar chart + toggle filter (env-named).</summary>
    public string Bucket { get; }
    public WorkAreaFolderDto? Left { get; }
    public WorkAreaFolderDto? Right { get; }
    public string Detail { get; }

    /// <summary>Non-null when the source query references ids missing from the target env (advisory).</summary>
    public string? ValidityWarning { get; }
    public bool HasValidityWarning => !string.IsNullOrEmpty(ValidityWarning);

    public bool LeftPresent => Left is not null;
    public bool RightPresent => Right is not null;
    /// <summary>Promotable left→right only when the folder exists in the source (left) env.</summary>
    public bool CanPromoteLeftToRight => LeftPresent;
    public string LeftCell => Left is null ? "—" : (Left.IsSyndication ? "syndication" : Left.IsQuery ? "query" : "folder");
    public string RightCell => Right is null ? "—" : (Right.IsSyndication ? "syndication" : Right.IsQuery ? "query" : "folder");
}
