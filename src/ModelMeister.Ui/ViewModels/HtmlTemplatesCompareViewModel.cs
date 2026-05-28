using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using ModelMeister.Inriver.HtmlTemplates;
using ModelMeister.Ui.Models;
using ModelMeister.Ui.Services;

namespace ModelMeister.Ui.ViewModels;

/// <summary>
/// "Compare HTML templates" page. Captures templates from two environments (matched by name + type),
/// shows only the rows that differ — present on one side only, or whose body/properties/localized name
/// differs — and promotes the source env's templates into the target. Identical templates are filtered
/// out; sides are named environment pills rather than left/right.
/// </summary>
public partial class HtmlTemplatesCompareViewModel : ViewModelBase, ICompareViewModel
{
    private readonly MainWindowViewModel _main;
    private readonly Shell _shell;
    private readonly IEnvironmentVault _vault;
    private readonly IAppLog _log;

    public ObservableCollection<EnvironmentEntry> AvailableEnvs { get; } = [];
    public ObservableCollection<HtmlTemplateDiffRow> Rows { get; } = [];
    public ObservableCollection<ConceptDiffCount> Counts { get; } = [];

    [ObservableProperty] private EnvironmentEntry? _leftEnv;
    [ObservableProperty] private EnvironmentEntry? _rightEnv;
    [ObservableProperty] private bool _busy;
    [ObservableProperty] private string _status = "Pick two environments to compare HTML templates.";
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
    public string BucketPath => nameof(HtmlTemplateDiffRow.Bucket);

    private IReadOnlyList<HtmlTemplateDto>? _leftCapture;
    private IReadOnlyList<HtmlTemplateDto>? _rightCapture;

    public HtmlTemplatesCompareViewModel(MainWindowViewModel main, Shell shell, IEnvironmentVault vault, IAppLog log)
    {
        _main = main;
        _shell = shell;
        _vault = vault;
        _log = log;
        _vault.Changed += RefreshEnvList;
        _main.ScopeChanged += RefreshEnvList;
        RefreshEnvList();

        SaveCsvCommand = CompareCommands.MakeSaveCsv(() => Rows, BuildExportColumns, "htmltemplates-compare.csv", _log, "CompareHtmlTemplates");
        CopyMarkdownCommand = CompareCommands.MakeCopyMarkdown(() => Rows, BuildExportColumns, _log, "CompareHtmlTemplates");

        ExtraActions = new[]
        {
            new CompareAction("Promote templates →", Primary: true, PromoteCommand),
        };
    }

    private IReadOnlyList<CompareExport.Column> BuildExportColumns() =>
        new CompareExport.Column[]
        {
            new("Name", r => ((HtmlTemplateDiffRow)r).Name),
            new("Type", r => ((HtmlTemplateDiffRow)r).TemplateType),
            new(string.IsNullOrEmpty(LeftColumnHeader) ? "Left" : LeftColumnHeader, r => ((HtmlTemplateDiffRow)r).LeftCell),
            new(string.IsNullOrEmpty(RightColumnHeader) ? "Right" : RightColumnHeader, r => ((HtmlTemplateDiffRow)r).RightCell),
            new("Detail", r => ((HtmlTemplateDiffRow)r).Detail),
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
            Status = $"Capturing templates from '{LeftEnv.Name}'…";
            _leftCapture = await _shell.CaptureHtmlTemplatesFromEnvAsync(LeftEnv, leftSecret).ConfigureAwait(true);

            Status = $"Capturing templates from '{RightEnv.Name}'…";
            _rightCapture = await _shell.CaptureHtmlTemplatesFromEnvAsync(RightEnv, rightSecret).ConfigureAwait(true);

            RecomputeDelta();
            _log.Success("CompareHtmlTemplates", $"Compared '{LeftEnv.Name}' vs '{RightEnv.Name}': {Rows.Count} difference(s).");
        }
        catch (Exception ex)
        {
            Status = "Compare failed: " + ex.Message;
            _log.Error("CompareHtmlTemplates", ex.Message, ex);
        }
        finally { Busy = false; }
    }

    private static string Key(HtmlTemplateDto t) => $"{t.Name}{t.TemplateType}";

    private void RecomputeDelta()
    {
        Rows.Clear();
        Counts.Clear();
        Buckets.Reset(Counts);

        if (_leftCapture is null || _rightCapture is null) { Summary = ""; HasRows = false; return; }

        var left = _leftCapture.ToDictionary(Key, t => t, StringComparer.Ordinal);
        var right = _rightCapture.ToDictionary(Key, t => t, StringComparer.Ordinal);
        var allKeys = left.Keys.Union(right.Keys, StringComparer.Ordinal).OrderBy(k => k, StringComparer.OrdinalIgnoreCase);

        var onlyLeftLabel = $"Only in {LeftColumnHeader}";
        var onlyRightLabel = $"Only in {RightColumnHeader}";
        const string differentLabel = "Different";
        int identical = 0;

        foreach (var key in allKeys)
        {
            var inLeft = left.TryGetValue(key, out var l);
            var inRight = right.TryGetValue(key, out var r);

            if (inLeft && inRight)
            {
                if (SameBody(l!, r!)) { identical++; continue; }
                Rows.Add(new HtmlTemplateDiffRow(l!.Name, l.TemplateType, differentLabel, l, r, DescribeDiff(l!, r!)));
            }
            else if (inLeft)
            {
                Rows.Add(new HtmlTemplateDiffRow(l!.Name, l.TemplateType, onlyLeftLabel, l, null, $"only in {LeftColumnHeader}"));
            }
            else
            {
                Rows.Add(new HtmlTemplateDiffRow(r!.Name, r.TemplateType, onlyRightLabel, null, r, $"only in {RightColumnHeader}"));
            }
        }

        HasRows = Rows.Count > 0;
        RebuildCounts();
        Summary = Rows.Count == 0
            ? $"No differences. ({identical} templates identical.)"
            : $"{Rows.Count} difference(s) · {identical} identical";
        Status = "Comparison complete.";
    }

    private static bool SameBody(HtmlTemplateDto a, HtmlTemplateDto b) =>
        string.Equals(a.Content, b.Content, StringComparison.Ordinal)
        && string.Equals(a.Properties, b.Properties, StringComparison.Ordinal);

    private static string DescribeDiff(HtmlTemplateDto l, HtmlTemplateDto r)
    {
        var parts = new List<string>();
        if (!string.Equals(l.Content, r.Content, StringComparison.Ordinal))
            parts.Add($"body differs ({l.Content.Length:n0} vs {r.Content.Length:n0} chars)");
        if (!string.Equals(l.Properties, r.Properties, StringComparison.Ordinal))
            parts.Add("properties differ");
        return parts.Count == 0 ? "differs" : string.Join(" · ", parts);
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

    /// <summary>Copy the source (left) env's HTML templates into the target (right) env (matched by name + type).</summary>
    [RelayCommand(CanExecute = nameof(CanPromote))]
    public async Task PromoteAsync()
    {
        if (LeftEnv is null || RightEnv is null) return;
        var sourceSecret = _vault.GetSecret(LeftEnv.Id);
        var targetSecret = _vault.GetSecret(RightEnv.Id);
        if (sourceSecret is null || string.IsNullOrEmpty(sourceSecret.ApiKey)) { Status = $"No API key on file for '{LeftEnv.Name}'."; return; }
        if (targetSecret is null || string.IsNullOrEmpty(targetSecret.ApiKey)) { Status = $"No API key on file for '{RightEnv.Name}'."; return; }

        var ok = await DialogHost.ConfirmPromoteAsync(
            "HTML templates", $"all templates from {LeftEnv.Name}",
            LeftEnv.Name, RightEnv.Name, RightEnv.TypeKey).ConfigureAwait(true);
        if (!ok) return;

        Busy = true;
        Status = $"Promoting HTML templates '{LeftEnv.Name}' → '{RightEnv.Name}'…";
        try
        {
            var result = await _shell.PromoteHtmlTemplatesAsync(LeftEnv, sourceSecret, RightEnv, targetSecret, allowDeletes: false).ConfigureAwait(true);
            Status = $"Promoted · created {result.Created}, updated {result.Updated}, unchanged {result.Unchanged}" + (result.Failed > 0 ? $", {result.Failed} failed" : "");
            _log.Success("CompareHtmlTemplates", Status);
        }
        catch (Exception ex)
        {
            Status = "Promote failed: " + ex.Message;
            _log.Error("CompareHtmlTemplates", ex.Message, ex);
        }
        finally { Busy = false; }

        await CompareAsync().ConfigureAwait(true);
    }
}

/// <summary>One differing row in the HTML-template compare grid.</summary>
public sealed class HtmlTemplateDiffRow
{
    public HtmlTemplateDiffRow(string name, string templateType, string bucket, HtmlTemplateDto? left, HtmlTemplateDto? right, string detail)
    {
        Name = name;
        TemplateType = templateType;
        Bucket = bucket;
        Left = left;
        Right = right;
        Detail = detail;
    }

    public string Name { get; }
    public string TemplateType { get; }
    public string Bucket { get; }
    public HtmlTemplateDto? Left { get; }
    public HtmlTemplateDto? Right { get; }
    public string Detail { get; }

    public bool LeftPresent => Left is not null;
    public bool RightPresent => Right is not null;
    public string LeftCell => Left is null ? "—" : "present";
    public string RightCell => Right is null ? "—" : "present";
}
