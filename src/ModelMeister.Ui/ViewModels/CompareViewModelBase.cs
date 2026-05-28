using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using ModelMeister.Ui.Models;
using ModelMeister.Ui.Services;

namespace ModelMeister.Ui.ViewModels;

/// <summary>
/// Shared base for env-vs-env compare pages. Hoists the chrome every compare VM repeated verbatim —
/// the env dropdowns + column headers/stages, the Busy/Status/Summary/HasRows state, the
/// AvailableEnvs/Counts/Rows collections, the Compare/Save-CSV/Copy commands, env-list refresh,
/// the env-equality guard and auto-compare — so each page only supplies its capture, row projection,
/// and promote logic. One shared definition means promote/pills/wording stay uniform across hubs.
/// </summary>
/// <remarks>
/// Derives from <see cref="FeaturePageViewModel"/> (the previous bases were a mix of that and
/// <see cref="ViewModelBase"/>) so the one page that needs the feature toolbar (Server settings)
/// keeps its Backup/Export overrides; the others simply ignore those no-op members.
/// </remarks>
public abstract partial class CompareViewModelBase<TRow> : FeaturePageViewModel, ICompareViewModel
    where TRow : class
{
    // Named with the underscore convention the migrated compare VMs already use in their bodies,
    // so their capture/promote logic moves over unchanged.
    protected readonly MainWindowViewModel _main;
    protected readonly Shell _shell;
    protected readonly IEnvironmentVault _vault;
    protected readonly IAppLog _log;

    public ObservableCollection<EnvironmentEntry> AvailableEnvs { get; } = [];
    /// <summary>The difference rows shown in the grid. Populated by each page's compare implementation.</summary>
    public ObservableCollection<TRow> Rows { get; } = [];
    public ObservableCollection<ConceptDiffCount> Counts { get; } = [];

    /// <summary>Bucket-bar toggle state shared by every compare page.</summary>
    public BucketToggleState Buckets { get; } = new();
    BucketToggleState? ICompareViewModel.Buckets => Buckets;
    /// <summary>Row property whose value is the bucket title. Overridden per page; empty disables bucket-toggling.</summary>
    public virtual string BucketPath => "";

    [ObservableProperty] private EnvironmentEntry? _leftEnv;
    [ObservableProperty] private EnvironmentEntry? _rightEnv;
    [ObservableProperty] private bool _busy;
    [ObservableProperty] private string _status = "Pick two environments to compare.";
    [ObservableProperty] private string _summary = "";
    [ObservableProperty] private bool _hasRows;
    [ObservableProperty] private string _leftColumnHeader = "";
    [ObservableProperty] private string _rightColumnHeader = "";
    [ObservableProperty] private string? _leftColumnStage;
    [ObservableProperty] private string? _rightColumnStage;

    public IAsyncRelayCommand CompareCommand { get; }
    public IAsyncRelayCommand SaveCsvCommand { get; }
    public IAsyncRelayCommand CopyMarkdownCommand { get; }
    /// <summary>Page-specific action buttons (e.g. "Promote selected →"). Set by the derived ctor.</summary>
    public IReadOnlyList<CompareAction> ExtraActions { get; protected set; } = [];

    /// <summary>Suggested file name for the Save-CSV picker.</summary>
    protected abstract string CsvFileName { get; }
    /// <summary>Log source tag for this page.</summary>
    protected abstract string LogSource { get; }
    /// <summary>Column projection for CSV / markdown export.</summary>
    protected abstract IReadOnlyList<CompareExport.Column> BuildExportColumns();
    /// <summary>Capture both sides and populate <see cref="Rows"/>/<see cref="Counts"/>/<see cref="Summary"/>.
    /// Kept entirely per-page so each hub's exact capture + status messaging is preserved.</summary>
    public abstract Task CompareAsync();

    /// <summary>Hook fired when either env selection changes (after headers update, before auto-compare).
    /// Capture-caching pages override this to discard their stale captures.</summary>
    protected virtual void OnEnvSelectionChanged() { }

    protected CompareViewModelBase(MainWindowViewModel main, Shell shell, IEnvironmentVault vault, IAppLog log)
    {
        _main = main;
        _shell = shell;
        _vault = vault;
        _log = log;

        _vault.Changed += RefreshEnvList;
        _main.ScopeChanged += RefreshEnvList;

        CompareCommand = new AsyncRelayCommand(CompareAsync, AsyncRelayCommandOptions.AllowConcurrentExecutions);
        SaveCsvCommand = CompareCommands.MakeSaveCsv(() => Rows, BuildExportColumns, CsvFileName, _log, LogSource);
        CopyMarkdownCommand = CompareCommands.MakeCopyMarkdown(() => Rows, BuildExportColumns, _log, LogSource);
    }

    /// <summary>Refresh the env dropdowns from the vault, preserving the current selection by id.
    /// Call after the user edits the vault or the org scope changes.</summary>
    public void RefreshEnvList()
    {
        var lid = LeftEnv?.Id;
        var rid = RightEnv?.Id;
        AvailableEnvs.Clear();
        foreach (var e in _main.EnvironmentsInScope())
            AvailableEnvs.Add(e);
        if (lid is { } li) LeftEnv = AvailableEnvs.FirstOrDefault(e => e.Id == li);
        if (rid is { } ri) RightEnv = AvailableEnvs.FirstOrDefault(e => e.Id == ri);

        if (LeftEnv is not null) { LeftColumnHeader = LeftEnv.Name; LeftColumnStage = LeftEnv.TypeKey; }
        if (RightEnv is not null) { RightColumnHeader = RightEnv.Name; RightColumnStage = RightEnv.TypeKey; }
    }

    partial void OnLeftEnvChanged(EnvironmentEntry? value)
    {
        LeftColumnHeader = value?.Name ?? "";
        LeftColumnStage = value?.TypeKey;
        OnEnvSelectionChanged();
        TryAutoCompare();
    }

    partial void OnRightEnvChanged(EnvironmentEntry? value)
    {
        RightColumnHeader = value?.Name ?? "";
        RightColumnStage = value?.TypeKey;
        OnEnvSelectionChanged();
        TryAutoCompare();
    }

    /// <summary>Auto-run a compare when both envs are set and distinct; otherwise reset the grid with a hint.</summary>
    protected void TryAutoCompare()
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
}
