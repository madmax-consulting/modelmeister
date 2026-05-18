using System;
using System.Collections;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using ModelMeister.Inriver.Extensions;
using ModelMeister.Ui.Models;
using ModelMeister.Ui.Services;

namespace ModelMeister.Ui.ViewModels;

/// <summary>
/// "Compare extensions" page. When both env dropdowns are populated, sequentially captures the
/// extension list (including settings) from each environment and reports per-extension
/// differences: presence, run state, and per-setting value diff. The user can promote settings
/// between sides on a per-row basis via the Push L→R / R→L actions.
/// </summary>
public partial class CompareExtensionsViewModel : ViewModelBase, ICompareViewModel
{
    private readonly MainWindowViewModel _main;
    private readonly Shell _shell;
    private readonly IEnvironmentVault _vault;
    private readonly IAppLog _log;

    public ObservableCollection<EnvironmentEntry> AvailableEnvs { get; } = [];
    public ObservableCollection<ExtensionDiffRow> Rows { get; } = [];
    public ObservableCollection<ConceptDiffCount> Counts { get; } = [];

    [ObservableProperty] private EnvironmentEntry? _leftEnv;
    [ObservableProperty] private EnvironmentEntry? _rightEnv;
    [ObservableProperty] private bool _busy;
    [ObservableProperty] private string _status = "Pick two environments to compare extensions.";
    [ObservableProperty] private string _summary = "";
    [ObservableProperty] private bool _hasRows;
    [ObservableProperty] private string _leftColumnHeader = "";
    [ObservableProperty] private string _rightColumnHeader = "";
    [ObservableProperty] private EnvironmentStage _leftColumnStage;
    [ObservableProperty] private EnvironmentStage _rightColumnStage;
    [ObservableProperty] private ExtensionDiffRow? _selected;

    public IAsyncRelayCommand SaveCsvCommand { get; }
    public IAsyncRelayCommand CopyMarkdownCommand { get; }
    public IReadOnlyList<CompareAction> ExtraActions { get; }

    private IReadOnlyList<ExtensionsService.ExtensionInfo>? _leftCapture;
    private IReadOnlyList<ExtensionsService.ExtensionInfo>? _rightCapture;
    private ExtensionsDelta? _delta;

    public CompareExtensionsViewModel(MainWindowViewModel main, Shell shell, IEnvironmentVault vault, IAppLog log)
    {
        _main = main;
        _shell = shell;
        _vault = vault;
        _log = log;
        _vault.Changed += RefreshEnvList;
        RefreshEnvList();

        SaveCsvCommand = CompareCommands.MakeSaveCsv(
            () => Rows,
            BuildExportColumns,
            suggestedFileName: "extensions-compare.csv",
            log: _log,
            logSource: "CompareExtensions");

        CopyMarkdownCommand = CompareCommands.MakeCopyMarkdown(
            () => Rows,
            BuildExportColumns,
            log: _log,
            logSource: "CompareExtensions");

        ExtraActions = new[]
        {
            new CompareAction("Push settings L→R", Primary: true,  PromoteSelectedSettingsLeftToRightCommand),
            new CompareAction("Push settings R→L", Primary: false, PromoteSelectedSettingsRightToLeftCommand),
        };
    }

    private IReadOnlyList<CompareExport.Column> BuildExportColumns() =>
        new CompareExport.Column[]
        {
            new("State", r => ((ExtensionDiffRow)r).State),
            new("Id",    r => ((ExtensionDiffRow)r).Id),
            new(string.IsNullOrEmpty(LeftColumnHeader)  ? "Left"  : LeftColumnHeader,  r => ((ExtensionDiffRow)r).LeftStatus),
            new(string.IsNullOrEmpty(RightColumnHeader) ? "Right" : RightColumnHeader, r => ((ExtensionDiffRow)r).RightStatus),
            new("Detail", r => ((ExtensionDiffRow)r).Summary),
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
        _leftCapture = null;
        LeftColumnHeader = value?.Name ?? "";
        LeftColumnStage = value?.Stage ?? EnvironmentStage.Unspecified;
        TryAutoCompare();
    }
    partial void OnRightEnvChanged(EnvironmentEntry? value)
    {
        _rightCapture = null;
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
            Status = $"Capturing extensions from '{LeftEnv.Name}'…";
            _leftCapture = await _shell.CaptureExtensionsFromEnvAsync(LeftEnv, leftSecret).ConfigureAwait(true);

            Status = $"Capturing extensions from '{RightEnv.Name}'…";
            _rightCapture = await _shell.CaptureExtensionsFromEnvAsync(RightEnv, rightSecret).ConfigureAwait(true);

            RecomputeDelta();
            _log.Success("CompareExtensions", $"Compared '{LeftEnv.Name}' vs '{RightEnv.Name}': {_delta?.TotalDifferences ?? 0} differences.");
        }
        catch (Exception ex)
        {
            Status = "Compare failed: " + ex.Message;
            _log.Error("CompareExtensions", ex.Message);
        }
        finally { Busy = false; }
    }

    private void RecomputeDelta()
    {
        Rows.Clear();
        Counts.Clear();

        if (_leftCapture is null || _rightCapture is null)
        {
            Summary = "";
            HasRows = false;
            return;
        }

        _delta = ExtensionsDiff.Compute(_leftCapture, _rightCapture);
        var leftMap = _leftCapture.ToDictionary(e => e.Id, StringComparer.Ordinal);
        var rightMap = _rightCapture.ToDictionary(e => e.Id, StringComparer.Ordinal);

        foreach (var id in _delta.OnlyInLeft) Rows.Add(new ExtensionDiffRow(id, "only-left", leftMap[id], null, summary: "only in LEFT"));
        foreach (var id in _delta.OnlyInRight) Rows.Add(new ExtensionDiffRow(id, "only-right", null, rightMap[id], summary: "only in RIGHT"));
        foreach (var c in _delta.Changed)
        {
            var summary = string.Join(" · ",
                c.Differences.Concat(c.Settings.TotalDifferences == 0
                    ? Enumerable.Empty<string>()
                    : new[]
                    {
                        $"settings: only-left {c.Settings.OnlyInLeft.Count}, only-right {c.Settings.OnlyInRight.Count}, changed {c.Settings.Changed.Count}"
                    }));
            Rows.Add(new ExtensionDiffRow(c.Id, "changed", leftMap[c.Id], rightMap[c.Id], summary: summary, change: c));
        }

        HasRows = Rows.Count > 0;
        RebuildCounts();

        Summary = _delta.TotalDifferences == 0
            ? $"No differences. ({_delta.UnchangedCount} extensions identical.)"
            : $"{_delta.TotalDifferences} differences  ·  only-left {_delta.OnlyInLeft.Count}  ·  only-right {_delta.OnlyInRight.Count}  ·  changed {_delta.Changed.Count}  ·  identical {_delta.UnchangedCount}";
        Status = "Comparison complete.";
    }

    private void RebuildCounts()
    {
        var max = 0;
        var groups = Rows.GroupBy(r => r.State)
                         .Select(g => (Title: g.Key, Count: g.Count()))
                         .OrderByDescending(t => t.Count)
                         .ToList();
        foreach (var g in groups) if (g.Count > max) max = g.Count;
        foreach (var g in groups)
            Counts.Add(new ConceptDiffCount(g.Title, g.Count, max == 0 ? 0 : (double)g.Count / max));
    }

    /// <summary>Push all setting differences for the selected row from left into right.</summary>
    [RelayCommand]
    public async Task PromoteSelectedSettingsLeftToRightAsync()
    {
        if (Selected?.Change is null || RightEnv is null || Selected.Left is null) return;
        await PromoteRowSettingsAsync(Selected, toRight: true).ConfigureAwait(true);
    }

    /// <summary>Push all setting differences for the selected row from right into left.</summary>
    [RelayCommand]
    public async Task PromoteSelectedSettingsRightToLeftAsync()
    {
        if (Selected?.Change is null || LeftEnv is null || Selected.Right is null) return;
        await PromoteRowSettingsAsync(Selected, toRight: false).ConfigureAwait(true);
    }

    private async Task PromoteRowSettingsAsync(ExtensionDiffRow row, bool toRight)
    {
        var targetEnv = toRight ? RightEnv : LeftEnv;
        if (targetEnv is null || row.Change is null) return;

        var secret = _vault.GetSecret(targetEnv.Id);
        if (secret is null || string.IsNullOrEmpty(secret.ApiKey))
        {
            Status = $"No API key on file for target '{targetEnv.Name}'."; return;
        }

        var sourceSettings = toRight ? row.Left!.Settings : row.Right!.Settings;
        var targetSettings = toRight ? row.Right!.Settings : row.Left!.Settings;
        var keysToWrite = new List<KeyValuePair<string, string?>>();

        // Add/overwrite from source.
        foreach (var kvp in sourceSettings)
        {
            if (!targetSettings.TryGetValue(kvp.Key, out var existing) || !string.Equals(existing, kvp.Value, StringComparison.Ordinal))
                keysToWrite.Add(new(kvp.Key, kvp.Value));
        }
        // Delete keys only on target.
        foreach (var kvp in targetSettings)
            if (!sourceSettings.ContainsKey(kvp.Key))
                keysToWrite.Add(new(kvp.Key, null));

        if (keysToWrite.Count == 0) { Status = "No setting differences for this extension."; return; }

        Busy = true;
        Status = $"Pushing {keysToWrite.Count} setting changes to '{targetEnv.Name}'/{row.Id}…";
        try
        {
            if (_main.ConnectedEnv?.Id != targetEnv.Id)
                await _shell.SwitchEnvAsync(targetEnv, secret).ConfigureAwait(true);

            int ok = 0, failed = 0;
            foreach (var kvp in keysToWrite)
            {
                var success = kvp.Value is null
                    ? await _shell.DeleteExtensionSettingAsync(row.Id, kvp.Key).ConfigureAwait(true)
                    : await _shell.SetExtensionSettingAsync(row.Id, kvp.Key, kvp.Value).ConfigureAwait(true);
                if (success) ok++; else failed++;
            }
            _log.Success("CompareExtensions", $"Promoted {ok}/{keysToWrite.Count} setting writes on '{row.Id}' → '{targetEnv.Name}'.");
            Status = failed == 0 ? $"Promoted {ok} settings." : $"{ok} applied, {failed} failed — see log.";
        }
        catch (Exception ex)
        {
            Status = "Promote failed: " + ex.Message;
            _log.Error("CompareExtensions", ex.Message);
        }
        finally { Busy = false; }

        // Re-run the full compare so the row updates and Counts/Summary refresh.
        await CompareAsync().ConfigureAwait(true);
    }
}

public sealed class ExtensionDiffRow
{
    public string Id { get; }
    public string State { get; }
    public ExtensionsService.ExtensionInfo? Left { get; }
    public ExtensionsService.ExtensionInfo? Right { get; }
    public string Summary { get; }
    public ExtensionChange? Change { get; }

    public ExtensionDiffRow(
        string id, string state,
        ExtensionsService.ExtensionInfo? left, ExtensionsService.ExtensionInfo? right,
        string summary, ExtensionChange? change = null)
    {
        Id = id;
        State = state;
        Left = left;
        Right = right;
        Summary = summary;
        Change = change;
    }

    public string LeftStatus => Left is null ? "—" : Left.IsStarted ? "running" : "stopped";
    public string RightStatus => Right is null ? "—" : Right.IsStarted ? "running" : "stopped";
    public string LeftType => Left?.TypeName ?? "";
    public string RightType => Right?.TypeName ?? "";
}
