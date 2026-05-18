using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using ModelMeister.Ui.Services;

namespace ModelMeister.Ui.ViewModels;

/// <summary>
/// View-model for the History page. Surfaces receipts and backups stored under
/// <c>.modelmeister/</c> next to the loaded model and offers basic cleanup commands.
/// </summary>
public partial class HistoryViewModel : ViewModelBase
{
    private static readonly TimeSpan ReceiptAgeCutoff = TimeSpan.FromDays(30);

    private readonly MainWindowViewModel _main;
    private readonly Shell _shell;
    private readonly IFileOpener _fileOpener;
    private readonly IAppLog _log;

    /// <summary>Short status line shown at the bottom of the page.</summary>
    [ObservableProperty] private string _statusMessage = "History from .modelmeister/ next to the loaded model.";

    private readonly List<HistoryRow> _allReceipts = new();
    private readonly List<HistoryRow> _allBackups = new();

    /// <summary>Receipts visible in the grid after applying <see cref="ReceiptFilter"/> / <see cref="ShowDryRuns"/>.</summary>
    public ObservableCollection<HistoryRow> Receipts { get; } = [];
    /// <summary>Backups visible in the grid after applying <see cref="ReceiptFilter"/>.</summary>
    public ObservableCollection<HistoryRow> Backups { get; } = [];

    /// <summary>Currently highlighted receipt (drives the preview panel).</summary>
    [ObservableProperty] private HistoryRow? _selectedReceipt;
    /// <summary>Currently highlighted backup row.</summary>
    [ObservableProperty] private HistoryRow? _selectedBackup;
    /// <summary>Substring filter against file name / environment-segment.</summary>
    [ObservableProperty] private string _receiptFilter = "";
    /// <summary>When false, dry-run receipts are hidden.</summary>
    [ObservableProperty] private bool _showDryRuns = true;
    /// <summary>Multi-line text preview of <see cref="SelectedReceipt"/>.</summary>
    [ObservableProperty] private string? _receiptPreview;

    public HistoryViewModel(MainWindowViewModel main, Shell shell, IFileOpener fileOpener, IAppLog log)
    {
        _main = main;
        _shell = shell;
        _fileOpener = fileOpener;
        _log = log;
    }

    /// <summary>Re-scan the on-disk history directories for the loaded model.</summary>
    [RelayCommand]
    public void Refresh()
    {
        _allReceipts.Clear();
        _allBackups.Clear();

        if (string.IsNullOrEmpty(_main.ModelPath))
        {
            StatusMessage = "Load a model to see its history.";
            ApplyFilters();
            return;
        }

        var dir = Path.GetDirectoryName(_main.ModelPath!);
        if (string.IsNullOrEmpty(dir)) return;

        _allReceipts.AddRange(EnumerateFiles(Path.Combine(dir, ".modelmeister", "receipts"), "*.json"));
        _allBackups.AddRange(EnumerateFiles(Path.Combine(dir, ".modelmeister", "backups"), "*.model.json"));

        ApplyFilters();
        StatusMessage = $"{_allReceipts.Count} receipts, {_allBackups.Count} backups.";
    }

    private static IEnumerable<HistoryRow> EnumerateFiles(string root, string pattern)
    {
        if (!Directory.Exists(root)) return Enumerable.Empty<HistoryRow>();
        return Directory.EnumerateFiles(root, pattern, SearchOption.AllDirectories)
            .OrderByDescending(File.GetLastWriteTimeUtc)
            .Select(path => new HistoryRow(path));
    }

    partial void OnReceiptFilterChanged(string value) => ApplyFilters();
    partial void OnShowDryRunsChanged(bool value) => ApplyFilters();
    partial void OnSelectedReceiptChanged(HistoryRow? value) => PreviewReceipt(value);

    private void ApplyFilters()
    {
        var query = (ReceiptFilter ?? "").Trim();

        Receipts.Clear();
        foreach (var r in _allReceipts.Where(r => (ShowDryRuns || !r.IsDryRun) && Matches(r, query)))
            Receipts.Add(r);

        Backups.Clear();
        foreach (var b in _allBackups.Where(b => Matches(b, query)))
            Backups.Add(b);
    }

    private static bool Matches(HistoryRow row, string query)
        => query.Length == 0
           || row.Name.Contains(query, StringComparison.OrdinalIgnoreCase)
           || row.Env.Contains(query, StringComparison.OrdinalIgnoreCase);

    private void PreviewReceipt(HistoryRow? row)
    {
        ReceiptPreview = null;
        if (row is null) return;

        try
        {
            using var doc = JsonDocument.Parse(File.ReadAllText(row.Path));
            var root = doc.RootElement;
            var summary = SummariseEntries(root);

            var sb = new StringBuilder()
                .AppendLine($"Receipt: {row.Name}")
                .AppendLine($"Dry-run: {row.IsDryRun}")
                .AppendLine($"Entries: {summary.Total} · Succeeded {summary.Succeeded} · Failed {summary.Failed}")
                .AppendLine($"Total duration: {summary.TotalMs:0} ms")
                .AppendLine();

            if (summary.TopKinds.Length > 0)
                sb.Append("Top kinds:\n  ").Append(summary.TopKinds);

            ReceiptPreview = sb.ToString();
        }
        catch (Exception ex)
        {
            ReceiptPreview = $"(could not parse receipt: {ex.Message})";
        }
    }

    private static (int Total, int Succeeded, int Failed, double TotalMs, string TopKinds) SummariseEntries(JsonElement root)
    {
        if (!root.TryGetProperty("Entries", out var entries) || entries.ValueKind != JsonValueKind.Array)
            return (0, 0, 0, 0, "");

        int total = 0, succeeded = 0, failed = 0;
        double totalMs = 0;
        var kindCounts = new Dictionary<string, int>(StringComparer.Ordinal);

        foreach (var e in entries.EnumerateArray())
        {
            total++;
            if (e.TryGetProperty("Succeeded", out var ok) && ok.GetBoolean()) succeeded++;
            else failed++;
            if (e.TryGetProperty("DurationMs", out var ms) && ms.TryGetInt64(out var v)) totalMs += v;
            if (e.TryGetProperty("Kind", out var k) && k.GetString() is { } kind)
                kindCounts[kind] = kindCounts.GetValueOrDefault(kind) + 1;
        }

        var top = string.Join("\n  ", kindCounts
            .OrderByDescending(p => p.Value)
            .Take(8)
            .Select(p => $"{p.Key} ×{p.Value}"));

        return (total, succeeded, failed, totalMs, top);
    }

    [RelayCommand] private void OpenReceipt()  { if (SelectedReceipt is not null) _fileOpener.Open(SelectedReceipt.Path); }
    [RelayCommand] private void OpenBackup()   { if (SelectedBackup  is not null) _fileOpener.Open(SelectedBackup.Path); }
    [RelayCommand] private void RevealReceipt(){ if (SelectedReceipt is not null) _fileOpener.RevealInExplorer(SelectedReceipt.Path); }
    [RelayCommand] private void RevealBackup() { if (SelectedBackup  is not null) _fileOpener.RevealInExplorer(SelectedBackup.Path); }
    [RelayCommand] private Task CopyReceiptPath() => ClipboardHelpers.CopyAsync(SelectedReceipt?.Path);
    [RelayCommand] private Task CopyBackupPath()  => ClipboardHelpers.CopyAsync(SelectedBackup?.Path);

    [RelayCommand]
    private async Task RestoreBackupAsync()
    {
        if (SelectedBackup is null) return;
        await _main.RestoreFromBackupAsync(SelectedBackup.Path);
    }

    [RelayCommand]
    private void DeleteDryRuns()
    {
        var removed = DeleteWhere(r => r.IsDryRun);
        if (removed > 0) _log.Info("History", $"Deleted {removed} dry-run receipt(s).");
        Refresh();
    }

    [RelayCommand]
    private void DeleteOldReceipts()
    {
        var cutoff = DateTime.UtcNow - ReceiptAgeCutoff;
        var removed = DeleteWhere(r => File.GetLastWriteTimeUtc(r.Path) < cutoff);
        if (removed > 0) _log.Info("History", $"Deleted {removed} receipt(s) older than {ReceiptAgeCutoff.TotalDays:0} days.");
        Refresh();
    }

    private int DeleteWhere(Func<HistoryRow, bool> predicate)
    {
        var removed = 0;
        foreach (var r in _allReceipts.Where(predicate).ToList())
        {
            try
            {
                File.Delete(r.Path);
                removed++;
            }
            catch (Exception ex)
            {
                _log.Warn("History", $"Could not delete {r.Path}: {ex.Message}");
            }
        }
        return removed;
    }
}

/// <summary>One row in the receipts/backups grids. Property names are referenced from <c>HistoryView.axaml</c>.</summary>
public sealed class HistoryRow
{
    public HistoryRow(string path)
    {
        Path = path;
        Name = System.IO.Path.GetFileName(path);
        var info = new FileInfo(path);
        Modified = info.LastWriteTime.ToString("yyyy-MM-dd HH:mm:ss");
        Size = info.Length;
        Env = System.IO.Path.GetFileName(System.IO.Path.GetDirectoryName(path) ?? "");
        IsDryRun = Name.EndsWith(".dryrun.json", StringComparison.OrdinalIgnoreCase);
    }

    public string Path { get; }
    public string Name { get; }
    public string Env { get; }
    public string Modified { get; }
    public long Size { get; }
    public bool IsDryRun { get; }
    public string Kind => IsDryRun ? "dry-run" : "apply";
}
