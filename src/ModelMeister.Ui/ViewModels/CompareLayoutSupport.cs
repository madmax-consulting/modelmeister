using System;
using System.Collections;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using ModelMeister.Ui.Models;
using ModelMeister.Ui.Services;

namespace ModelMeister.Ui.ViewModels;

/// <summary>
/// Shared chrome contract for env-vs-env compare pages. Every compare VM (Model, CVL, Users,
/// Extensions) exposes these properties + commands so a single <see cref="Views.CompareLayoutView"/>
/// renders the dropdowns / stage pills / Compare-Save-Copy bar / bottom counts bar chart / status
/// strip on its behalf — each page is then just a DataGrid with FilterableHeader columns.
/// </summary>
public interface ICompareViewModel
{
    ObservableCollection<EnvironmentEntry> AvailableEnvs { get; }
    EnvironmentEntry? LeftEnv { get; set; }
    EnvironmentEntry? RightEnv { get; set; }

    /// <summary>Header label for the left-value column = left env name.</summary>
    string LeftColumnHeader { get; }
    string RightColumnHeader { get; }
    EnvironmentStage LeftColumnStage { get; }
    EnvironmentStage RightColumnStage { get; }

    bool Busy { get; }
    string Status { get; }
    /// <summary>One-line summary shown next to the action buttons (e.g. "42 differences across 5 concepts").</summary>
    string Summary { get; }
    /// <summary>True when there is at least one row to show; gates visibility of the bottom chart.</summary>
    bool HasRows { get; }

    /// <summary>Per-bucket counts shown as the bottom bar chart.</summary>
    ObservableCollection<ConceptDiffCount> Counts { get; }

    IAsyncRelayCommand CompareCommand { get; }
    IAsyncRelayCommand SaveCsvCommand { get; }
    IAsyncRelayCommand CopyMarkdownCommand { get; }

    /// <summary>Extra page-specific action buttons rendered to the right of Save/Copy. Empty for most pages.</summary>
    IReadOnlyList<CompareAction> ExtraActions { get; }

    /// <summary>Clicking a bottom-chart bucket bar toggles its membership in the grid's hidden set
    /// (dims the bar + hides its rows from the table). Compare pages that opt into bucket-toggling
    /// expose a non-null <see cref="Buckets"/>; the layout's bar template binds its Click to
    /// <c>Buckets.Toggle</c> via a converter when present, and the hosting view subscribes to
    /// <see cref="BucketToggleState.Changed"/> to push the set onto the grid's filter.</summary>
    BucketToggleState? Buckets => null;

    /// <summary>The property path on each row whose value is the bucket title (e.g. <c>Concept</c>,
    /// <c>Bucket</c>). Empty for compare pages that don't support bucket-toggling.</summary>
    string BucketPath => "";
}

/// <summary>Shared state for bucket-bar toggling — each compare VM that opts in owns one of these.</summary>
public sealed class BucketToggleState
{
    private readonly HashSet<string> _hidden = new(StringComparer.OrdinalIgnoreCase);
    /// <summary>Fired after every toggle with the full set of currently-hidden bucket titles.</summary>
    public event Action<IReadOnlySet<string>>? Changed;

    /// <summary>Toggle <paramref name="c"/>'s membership in the hidden set; mirrors onto its IsHidden flag for the bar visual.</summary>
    public void Toggle(ConceptDiffCount? c)
    {
        if (c is null) return;
        if (_hidden.Contains(c.Title)) _hidden.Remove(c.Title);
        else _hidden.Add(c.Title);
        c.IsHidden = _hidden.Contains(c.Title);
        Changed?.Invoke(_hidden);
    }

    /// <summary>Drop every hidden flag — call when the compare run re-populates the counts.</summary>
    public void Reset(IEnumerable<ConceptDiffCount> counts)
    {
        _hidden.Clear();
        foreach (var c in counts) c.IsHidden = false;
        Changed?.Invoke(_hidden);
    }
}

/// <summary>One row of the bottom bar chart. <see cref="Fraction"/> is the bar fill ratio in [0,1].
/// <see cref="IsHidden"/> drives the dim/strike visual when the user has clicked the bar.</summary>
public partial class ConceptDiffCount : ObservableObject
{
    public ConceptDiffCount(string title, int count, double fraction)
    {
        Title = title;
        Count = count;
        Fraction = fraction;
    }
    public string Title { get; }
    public int Count { get; }
    public double Fraction { get; }
    [ObservableProperty] private bool _isHidden;
}

/// <summary>An extra compare-page-specific action exposed via <see cref="ICompareViewModel.ExtraActions"/>.
/// <see cref="Primary"/> renders as the accent button (otherwise ghost).</summary>
public sealed record CompareAction(string Label, bool Primary, IAsyncRelayCommand Command);

/// <summary>CSV + Markdown export of a compare-page rows collection. Column projection is supplied by the host VM.</summary>
public static class CompareExport
{
    /// <summary>One exported column: (header, value-extractor against a row instance).</summary>
    public sealed record Column(string Header, Func<object, string> Cell);

    public static string ToCsv(IEnumerable rows, IReadOnlyList<Column> columns)
    {
        var sb = new StringBuilder();
        sb.Append(string.Join(",", columns.Select(c => CsvField(c.Header)))).Append('\n');
        foreach (var row in rows)
        {
            if (row is null) continue;
            sb.Append(string.Join(",", columns.Select(c => CsvField(c.Cell(row))))).Append('\n');
        }
        return sb.ToString();
    }

    public static string ToMarkdown(IEnumerable rows, IReadOnlyList<Column> columns)
    {
        var sb = new StringBuilder();
        sb.Append("| ").Append(string.Join(" | ", columns.Select(c => MdField(c.Header)))).Append(" |\n");
        sb.Append("|").Append(string.Join("|", columns.Select(_ => "---"))).Append("|\n");
        foreach (var row in rows)
        {
            if (row is null) continue;
            sb.Append("| ").Append(string.Join(" | ", columns.Select(c => MdField(c.Cell(row))))).Append(" |\n");
        }
        return sb.ToString();
    }

    static string CsvField(string s)
    {
        if (string.IsNullOrEmpty(s)) return "";
        if (s.IndexOfAny(new[] { ',', '"', '\n', '\r' }) < 0) return s;
        return "\"" + s.Replace("\"", "\"\"") + "\"";
    }

    static string MdField(string s) =>
        string.IsNullOrEmpty(s) ? "" : s.Replace("|", "\\|").Replace("\n", " ").Replace("\r", "");
}

/// <summary>
/// Factory helpers that build the Save-CSV / Copy-markdown commands shared by every compare page.
/// Each host VM provides its rows + column projection lambda — these helpers handle file-picker,
/// clipboard, and logging.
/// </summary>
internal static class CompareCommands
{
    public static AsyncRelayCommand MakeSaveCsv(
        Func<IEnumerable> rowsSource,
        Func<IReadOnlyList<CompareExport.Column>> columns,
        string suggestedFileName,
        IAppLog log,
        string logSource)
    {
        return new AsyncRelayCommand(async () =>
        {
            var path = await FilePickerHelpers.PickSaveAsync(
                title: "Save comparison as CSV",
                suggestedFileName: suggestedFileName,
                defaultExtension: "csv").ConfigureAwait(true);
            if (string.IsNullOrEmpty(path)) return;

            try
            {
                var csv = CompareExport.ToCsv(rowsSource(), columns());
                await File.WriteAllTextAsync(path, csv).ConfigureAwait(true);
                log.Success(logSource, $"Comparison exported to {path}");
            }
            catch (Exception ex)
            {
                log.Error(logSource, $"Export failed: {ex.Message}");
            }
        });
    }

    public static AsyncRelayCommand MakeCopyMarkdown(
        Func<IEnumerable> rowsSource,
        Func<IReadOnlyList<CompareExport.Column>> columns,
        IAppLog log,
        string logSource)
    {
        return new AsyncRelayCommand(async () =>
        {
            var clipboard = MainWindowOrNull()?.Clipboard;
            if (clipboard is null) return;
            try
            {
                var md = CompareExport.ToMarkdown(rowsSource(), columns());
                await clipboard.SetTextAsync(md);
                log.Info(logSource, "Comparison copied to clipboard as markdown.");
            }
            catch (Exception ex)
            {
                log.Error(logSource, $"Copy failed: {ex.Message}");
            }
        });
    }

    static Window? MainWindowOrNull()
        => Application.Current?.ApplicationLifetime is IClassicDesktopStyleApplicationLifetime d
            ? d.MainWindow
            : null;
}
