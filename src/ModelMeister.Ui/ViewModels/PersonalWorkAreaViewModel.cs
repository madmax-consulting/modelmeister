using System;
using System.Linq;
using System.Threading.Tasks;
using ModelMeister.Ui.Services;

namespace ModelMeister.Ui.ViewModels;

/// <summary>
/// Personal work-area page: the shared <see cref="WorkAreaViewModel"/> Manage experience (tree CRUD, reorder,
/// query builder, Excel) re-pointed at a <b>selected user's</b> personal folders via <see cref="PersonalUsername"/>.
/// Syndication is hidden (personal folders have no syndication concept). Nothing loads until a user is picked.
/// </summary>
public sealed partial class PersonalWorkAreaViewModel : WorkAreaViewModel
{
    public PersonalWorkAreaViewModel(MainWindowViewModel main, Shell shell, IAppLog log) : base(main, shell, log)
    {
        main.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(MainWindowViewModel.IsConnected) && main.IsConnected)
                _ = LoadUsersAsync();
        };
        if (main.IsConnected) _ = LoadUsersAsync();
    }

    protected override string? PersonalUsername => SelectedUser?.Username;
    public override bool ShowUserPicker => true;
    public override bool ShowSyndicationToggle => false;
    public override BackupScope BackupScope => BackupScope.PersonalWorkAreas;

    public override string WorkAreaEyebrow => "PERSONAL WORK AREAS";
    public override string WorkAreaSubtitle => "Browse and manage a selected user's personal folders and saved searches";
    public override string EmptyTitle => SelectedUser is null ? "Pick a user" : "No personal folders";
    protected override string EmptyStatus =>
        SelectedUser is null ? "Pick a user to view their personal work areas."
                             : $"{SelectedUser.Username} has no personal work-area folders.";

    private async Task LoadUsersAsync()
    {
        try
        {
            var users = await ShellSvc.ListUsersAsync().ConfigureAwait(true);
            var prior = SelectedUser?.Username;
            Users.Clear();
            foreach (var u in users.OrderBy(u => u.Username, StringComparer.OrdinalIgnoreCase)) Users.Add(u);
            if (prior is not null) SelectedUser = Users.FirstOrDefault(u => string.Equals(u.Username, prior, StringComparison.OrdinalIgnoreCase));
        }
        catch (Exception ex) { Log.Error("PersonalWorkAreas", $"Couldn't list users: {ex.Message}", ex); }
    }

    /// <inheritdoc/>
    public override async Task RefreshAsync()
    {
        if (!Main.IsConnected) { Status = "Connect to an environment first."; return; }
        if (SelectedUser is null)
        {
            Tree.Clear();
            Selected = null;
            Status = "Pick a user to view their personal work areas.";
            return;
        }
        await base.RefreshAsync().ConfigureAwait(true);
    }

    /// <inheritdoc/>
    public override Task ExportExcelAsync()
    {
        if (SelectedUser is null) { Status = "Pick a user first."; Log.Toast(LogLevel.Warn, "Export", "Pick a user first."); return Task.CompletedTask; }
        return base.ExportExcelAsync();
    }

    /// <inheritdoc/>
    public override Task ImportExcelAsync()
    {
        if (SelectedUser is null) { Status = "Pick a user first."; Log.Toast(LogLevel.Warn, "Import", "Pick a user first."); return Task.CompletedTask; }
        return base.ImportExcelAsync();
    }

    /// <inheritdoc/>
    public override async Task BackupAsync()
    {
        if (!Main.IsConnected) { Log.Toast(LogLevel.Warn, "Backup", "Connect first."); return; }
        if (SelectedUser is null) { Log.Toast(LogLevel.Warn, "Backup", "Pick a user first."); return; }
        try
        {
            var path = await Main.Backups.CapturePersonalWorkAreasAsync(SelectedUser.Username).ConfigureAwait(true);
            Log.Success("Backup", $"Personal work-areas backup saved → {path}");
            Log.Toast(LogLevel.Success, "Personal work-areas backup saved", System.IO.Path.GetFileName(path));
        }
        catch (Exception ex)
        {
            Log.Error("Backup", $"Personal work-areas backup failed: {ex.Message}", ex);
            Log.Toast(LogLevel.Error, "Backup failed", ex.Message);
        }
    }
}
