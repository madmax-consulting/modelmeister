using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using ModelMeister.Ui.Services;

namespace ModelMeister.Ui.ViewModels;

/// <summary>
/// Base for feature pages rendered inside a <c>FeaturePage</c> shell. Subclasses declare which
/// uniform toolbar actions (Compare / Backup / Export / Import / Refresh) they support; the shell
/// renders the action group from those flags so placement is uniform across every page.
/// </summary>
public abstract partial class FeaturePageViewModel : ViewModelBase
{
    /// <summary>Header shown above the page content (eyebrow label). Set by subclasses.</summary>
    [ObservableProperty] private string _pageTitle = "";

    /// <summary>One-line subtitle / context shown next to the title.</summary>
    [ObservableProperty] private string _pageSubtitle = "";

    /// <summary>Whether the SourceBar Compare toggle is shown on this page.</summary>
    public virtual bool SupportsCompare => false;

    /// <summary>What scope this page's Backup button captures.</summary>
    public virtual BackupScope BackupScope => BackupScope.None;

    /// <summary>Whether the page exposes Excel export / import on its toolbar.</summary>
    public virtual ExcelCapability Excel => ExcelCapability.None;

    /// <summary>True when the page can produce a backup for the current SourceBar slot A.</summary>
    public virtual bool CanBackup => BackupScope != BackupScope.None;

    /// <summary>True when the Excel Export button should render.</summary>
    public bool HasExcelExport => Excel is ExcelCapability.Export or ExcelCapability.ExportImport;

    /// <summary>True when the Excel Import button should render.</summary>
    public bool HasExcelImport => Excel is ExcelCapability.ExportImport;

    /// <summary>Uniform toolbar command: refresh data for the current source set.</summary>
    public IAsyncRelayCommand RefreshCommand { get; }
    /// <summary>Uniform toolbar command: capture a scoped backup of slot A.</summary>
    public IAsyncRelayCommand BackupCommand { get; }
    /// <summary>Uniform toolbar command: export to Excel workbook.</summary>
    public IAsyncRelayCommand ExportExcelCommand { get; }
    /// <summary>Uniform toolbar command: import an Excel workbook into slot A.</summary>
    public IAsyncRelayCommand ImportExcelCommand { get; }

    /// <summary>
    /// Dirty-flag reload gate. Starts <c>true</c> (data has not been loaded yet), is flipped to
    /// <c>false</c> after <see cref="RefreshAsync"/> succeeds, and back to <c>true</c> whenever the
    /// page-VM detects a state change that invalidates its cache (mutation, connection switch).
    /// <see cref="EnsureLoadedAsync"/> consults this flag; the toolbar Refresh button bypasses it.
    /// </summary>
    protected bool IsDataDirty { get; private set; } = true;

    /// <summary>Mark the page's data as stale — the next <see cref="EnsureLoadedAsync"/> call will re-fetch.</summary>
    protected void MarkDataDirty() => IsDataDirty = true;

    /// <summary>Refresh only when the dirty flag is set; cheap no-op otherwise.</summary>
    public async Task EnsureLoadedAsync()
    {
        if (!IsDataDirty) return;
        await RefreshAsync().ConfigureAwait(true);
        IsDataDirty = false;
    }

    protected FeaturePageViewModel()
    {
        // The Refresh button always forces a reload — gating the toolbar button on the dirty flag
        // would defeat its purpose (user clicks it specifically to bypass any cache).
        RefreshCommand     = new AsyncRelayCommand(async () =>
        {
            await RefreshAsync().ConfigureAwait(true);
            IsDataDirty = false;
        });
        BackupCommand      = new AsyncRelayCommand(BackupAsync);
        ExportExcelCommand = new AsyncRelayCommand(ExportExcelAsync);
        ImportExcelCommand = new AsyncRelayCommand(ImportExcelAsync);
    }

    /// <summary>Override to refresh data when the user clicks the uniform Refresh button or the source set changes.</summary>
    public virtual Task RefreshAsync() => Task.CompletedTask;

    /// <summary>Override to capture a scoped backup of slot A's data.</summary>
    public virtual Task BackupAsync() => Task.CompletedTask;

    /// <summary>Override to write the page's data to an Excel workbook.</summary>
    public virtual Task ExportExcelAsync() => Task.CompletedTask;

    /// <summary>Override to read a workbook and apply it to slot A.</summary>
    public virtual Task ImportExcelAsync() => Task.CompletedTask;
}
