using System;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using ModelMeister.Excel;
using ModelMeister.Inriver.Users;
using ModelMeister.Ui.Models;
using ModelMeister.Ui.Services;

namespace ModelMeister.Ui.ViewModels;

/// <summary>
/// Users page view-model. Lists users in the connected env, lets the user export a workbook
/// (pre-seeded with current users + roles), and provision/update users from an edited workbook.
/// </summary>
public partial class UsersViewModel : FeaturePageViewModel
{
    readonly MainWindowViewModel _main;
    readonly Shell _shell;
    readonly IAppLog _log;

    /// <inheritdoc/>
    public override bool SupportsCompare => true;
    /// <inheritdoc/>
    public override BackupScope BackupScope => BackupScope.Users;
    /// <inheritdoc/>
    public override ExcelCapability Excel => ExcelCapability.ExportImport;

    /// <inheritdoc/>
    public override async Task BackupAsync()
    {
        if (!_main.IsConnected) { _log.Toast(LogLevel.Warn, "Backup", "Connect first."); return; }
        try
        {
            var path = await _main.Backups.CaptureUsersAsync().ConfigureAwait(true);
            _log.Success("Backup", $"Users backup saved → {path}");
            _log.Toast(LogLevel.Success, "Users backup saved", Path.GetFileName(path));
        }
        catch (Exception ex)
        {
            _log.Error("Backup", $"Users backup failed: {ex.Message}", ex);
            _log.Toast(LogLevel.Error, "Backup failed", ex.Message);
        }
    }

    /// <inheritdoc/>
    public override bool HasExcelTemplate => true;

    /// <inheritdoc/>
    public override Task ExportExcelAsync() => DownloadListAsync();

    /// <inheritdoc/>
    public override Task ExportTemplateAsync() => DownloadTemplateAsync();

    /// <inheritdoc/>
    public override async Task ImportExcelAsync()
    {
        if (!_main.IsConnected) { _log.Toast(LogLevel.Warn, "Import users", "Connect first."); return; }
        var plan = new ModelMeister.Ui.Services.Import.Plans.UsersImportPlan(_main, _shell, _log);
        var ran = await DialogHost.ShowImportWorkflowAsync(
            plan, _log, _main.Settings.Current.RecentWorkbookPaths).ConfigureAwait(true);
        if (!ran) return;
        RememberWorkbook(_main.Settings, plan.LastWorkbookPath);
        MarkDataDirty();
        await RefreshAsync().ConfigureAwait(true);
    }

    public ObservableCollection<UserListRow> Users { get; } = [];
    public ObservableCollection<string> Roles { get; } = [];

    /// <summary>Checkbox multi-selection over <see cref="Users"/> (header select-all + shift-click range).</summary>
    public RowSelectionModel Selection { get; }

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(DownloadListCommand))]
    [NotifyCanExecuteChangedFor(nameof(DownloadTemplateCommand))]
    [NotifyCanExecuteChangedFor(nameof(AddUserCommand))]
    private bool _busy;
    [ObservableProperty] private string _status = "";

    public UsersViewModel(MainWindowViewModel main, Shell shell, IAppLog log)
    {
        _main = main;
        _shell = shell;
        _log = log;
        Selection = new RowSelectionModel(Users);
        _main.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(MainWindowViewModel.IsConnected))
            {
                // Switching envs invalidates the user list — flag it so next visit re-fetches.
                MarkDataDirty();
                if (_main.IsConnected) _ = EnsureLoadedAsync();
                DownloadListCommand.NotifyCanExecuteChanged();
                DownloadTemplateCommand.NotifyCanExecuteChanged();
                AddUserCommand.NotifyCanExecuteChanged();
            }
        };
    }

    private bool CanExportTemplate() => !Busy && _main.IsConnected;

    /// <inheritdoc/>
    public override async Task RefreshAsync()
    {
        if (!_main.IsConnected) { Status = "Connect to an environment first."; return; }
        Busy = true;
        try
        {
            var users = await _shell.ListUsersAsync().ConfigureAwait(true);
            var roles = await _shell.ListRoleNamesAsync().ConfigureAwait(true);
            Users.Clear();
            foreach (var u in users.OrderBy(u => u.Username, StringComparer.OrdinalIgnoreCase)) Users.Add(new UserListRow(u));
            Roles.Clear();
            foreach (var r in roles) Roles.Add(r);
            Status = $"{Users.Count} users · {Roles.Count} roles";
        }
        catch (Exception ex)
        {
            Status = "Failed: " + ex.Message;
            _log.Error("Users", ex.Message, ex);
        }
        finally { Busy = false; }
    }

    [RelayCommand] private Task CopyUsername(UserListRow? u) => ClipboardHelpers.CopyAsync(u?.Username);
    [RelayCommand] private Task CopyEmail(UserListRow? u)    => ClipboardHelpers.CopyAsync(u?.Email);
    [RelayCommand] private Task CopyRoles(UserListRow? u)    => ClipboardHelpers.CopyAsync(u is null ? null : string.Join(", ", u.Roles));

    /// <summary>Copy every checked user as tab-separated rows (username, email, roles) — the one bulk
    /// action available on Users since the Remoting/REST surface can't delete or edit users here.</summary>
    [RelayCommand]
    private async Task CopySelectedAsync()
    {
        var rows = Selection.SelectedOf<UserListRow>();
        if (rows.Count == 0) { Status = "Select at least one user."; return; }
        var text = string.Join(Environment.NewLine,
            rows.Select(u => string.Join('\t', u.Username, u.Email ?? "", string.Join(", ", u.Roles))));
        await ClipboardHelpers.CopyAsync(text).ConfigureAwait(true);
        Status = $"Copied {rows.Count} user(s) to the clipboard.";
    }

    // ----- CRUD: create / edit / bulk-role. All go through ProvisionUserAsync (REST upsert), which
    // needs the env's REST base URL + API key — same requirement as Excel import. -----

    private bool CanMutate() => !Busy && _main.IsConnected;

    /// <summary>Open the user editor blank; on Save provision the new user (create).</summary>
    [RelayCommand(CanExecute = nameof(CanMutate))]
    private async Task AddUserAsync()
    {
        if (!_main.IsConnected) { Status = "Connect first."; return; }
        var vm = await DialogHost.UserEditorAsync(null, null, null, null, null, [], Roles.ToList(), isEdit: false).ConfigureAwait(true);
        if (vm is null) return;
        var spec = new UserProvisioning.UserSpec(
            vm.Username.Trim(), NullIfEmpty(vm.Email), NullIfEmpty(vm.FirstName), NullIfEmpty(vm.LastName),
            vm.SelectedRoles, NormalizeLanguage(vm.Language), vm.GenerateApiKey);
        await ProvisionUserSpecAsync(spec, "Created").ConfigureAwait(true);
    }

    /// <summary>Open the user editor pre-filled; on Save re-provision the user (upsert by username).</summary>
    [RelayCommand]
    private async Task EditUserAsync(UserListRow? row)
    {
        if (row is null || !_main.IsConnected) return;
        var vm = await DialogHost.UserEditorAsync(
            row.Username, row.Email, row.FirstName, row.LastName, null, row.Roles, Roles.ToList(), isEdit: true).ConfigureAwait(true);
        if (vm is null) return;
        var spec = new UserProvisioning.UserSpec(
            row.Username, NullIfEmpty(vm.Email), NullIfEmpty(vm.FirstName), NullIfEmpty(vm.LastName),
            vm.SelectedRoles, NormalizeLanguage(vm.Language), GenerateApiKey: false);
        await ProvisionUserSpecAsync(spec, "Updated").ConfigureAwait(true);
    }

    private async Task ProvisionUserSpecAsync(UserProvisioning.UserSpec spec, string verb)
    {
        if (!TryGetRestContext(out var env, out var secret)) return;
        Busy = true;
        Status = $"Saving user '{spec.Username}'…";
        try
        {
            var result = await _shell.ProvisionUserAsync(spec, secret, env).ConfigureAwait(true);
            if (result.Errors.Count > 0)
            {
                Status = $"'{spec.Username}': {string.Join("; ", result.Errors)}";
                _log.Warn("Users", Status);
            }
            else
            {
                _log.Success("Users", $"{verb} user '{spec.Username}'.");
                Status = $"{verb} user '{spec.Username}'.";
            }
            MarkDataDirty();
            await RefreshAsync().ConfigureAwait(true);
        }
        catch (Exception ex) { Status = "Failed: " + ex.Message; _log.Error("Users", ex.Message, ex); }
        finally { Busy = false; }
    }

    /// <summary>Grant or revoke a single role across every checked user (one dialog, one batch).</summary>
    [RelayCommand]
    private async Task BulkSetRoleAsync()
    {
        if (!_main.IsConnected) { Status = "Connect first."; return; }
        var rows = Selection.SelectedOf<UserListRow>();
        if (rows.Count == 0) { Status = "Select at least one user."; return; }

        var vm = await DialogHost.BulkUserRoleAsync(rows.Count, Roles.ToList()).ConfigureAwait(true);
        if (vm is null || string.IsNullOrWhiteSpace(vm.SelectedRole)) return;
        if (!TryGetRestContext(out var env, out var secret)) return;

        var role = vm.SelectedRole!;
        Busy = true;
        var verb = vm.Add ? "Granted" : "Revoked";
        int ok = 0, failed = 0;
        try
        {
            await RunBulkAsync(rows,
                async row =>
                {
                    var roles = new List<string>(row.Roles);
                    if (vm.Add)
                    {
                        if (!roles.Contains(role, StringComparer.OrdinalIgnoreCase)) roles.Add(role);
                    }
                    else
                    {
                        roles.RemoveAll(r => string.Equals(r, role, StringComparison.OrdinalIgnoreCase));
                    }
                    var spec = new UserProvisioning.UserSpec(
                        row.Username, NullIfEmpty(row.Email), NullIfEmpty(row.FirstName), NullIfEmpty(row.LastName),
                        roles, "en", GenerateApiKey: false);
                    try
                    {
                        var result = await _shell.ProvisionUserAsync(spec, secret, env).ConfigureAwait(false);
                        if (result.Errors.Count > 0) { failed++; foreach (var e in result.Errors) _log.Warn("Users", $"{row.Username}: {e}"); }
                        else ok++;
                    }
                    catch (Exception ex) { failed++; _log.Warn("Users", $"{row.Username}: {ex.Message}"); }
                },
                (i, total, row) => Status = $"{(vm.Add ? "Granting" : "Revoking")} '{role}' {i} / {total} ('{row.Username}')…").ConfigureAwait(true);
            Status = failed == 0
                ? $"{verb} '{role}' on {ok} user(s)."
                : $"{verb} on {ok}, {failed} failed.";
            _log.Success("Users", Status);
            MarkDataDirty();
            await RefreshAsync().ConfigureAwait(true);
        }
        finally { Busy = false; }
    }

    /// <summary>Resolve the connected env + its REST secret, setting a clear status when either is missing
    /// (single + bulk user writes both need a REST endpoint and key — the Remoting surface can't create users).</summary>
    private bool TryGetRestContext(
        [System.Diagnostics.CodeAnalysis.NotNullWhen(true)] out Models.EnvironmentEntry? env,
        out Models.EnvironmentSecret? secret)
    {
        env = _main.ConnectedEnv;
        secret = env is null ? null : _main.Vault.GetSecret(env.Id);
        if (env is null || string.IsNullOrWhiteSpace(env.RestBaseUrl))
        {
            Status = "Creating or editing users requires a REST base URL on the connected environment.";
            _log.Warn("Users", Status);
            return false;
        }
        if (secret is null || string.IsNullOrWhiteSpace(secret.RestApiKey))
        {
            Status = "Creating or editing users requires a REST API key on the connected environment.";
            _log.Warn("Users", Status);
            return false;
        }
        return true;
    }

    private static string NormalizeLanguage(string? language) => string.IsNullOrWhiteSpace(language) ? "en" : language.Trim();
    private static string? NullIfEmpty(string? s) => string.IsNullOrWhiteSpace(s) ? null : s;

    /// <summary>Download the current user list as an xlsx workbook.</summary>
    [RelayCommand(CanExecute = nameof(CanExportTemplate))]
    public async Task DownloadListAsync() => await ExportWorkbookAsync(seedSingleExample: false).ConfigureAwait(true);

    /// <summary>Download a minimal example workbook (one row) callers can edit + re-import.</summary>
    [RelayCommand(CanExecute = nameof(CanExportTemplate))]
    public async Task DownloadTemplateAsync() => await ExportWorkbookAsync(seedSingleExample: true).ConfigureAwait(true);

    private async Task ExportWorkbookAsync(bool seedSingleExample)
    {
        if (!_main.IsConnected) { Status = "Connect to an environment first."; return; }
        var path = await FilePickerHelpers.PickSaveAsync(
            "Save users workbook",
            seedSingleExample ? "users-template.xlsx" : "users.xlsx",
            "xlsx").ConfigureAwait(true);
        if (path is null) return;

        Busy = true;
        try
        {
            var roles = await _shell.ListRoleNamesAsync().ConfigureAwait(true);
            List<UsersWorkbook.UserRow> rows;
            if (seedSingleExample)
            {
                // Template: one example row so the workbook columns are obvious. Email is a
                // placeholder (and doubles as the username) so accidental re-import without edits
                // creates exactly one harmless user (or fails validation), never overwrites a real one.
                rows = new List<UsersWorkbook.UserRow>
                {
                    new()
                    {
                        Email = "example.user@example.com",
                        FirstName = "Example",
                        LastName = "User",
                        Roles = new List<string>(),
                        Language = "en",
                    },
                };
            }
            else
            {
                var users = await _shell.ListUsersAsync().ConfigureAwait(true);
                rows = users.Select(u => new UsersWorkbook.UserRow
                {
                    Username = u.Username,
                    Email = u.Email ?? "",
                    FirstName = u.FirstName ?? "",
                    LastName = u.LastName ?? "",
                    Roles = u.Roles.ToList(),
                    Language = "en",
                }).ToList();
            }
            UsersWorkbook.Save(rows, roles.ToList(), path);
            Status = $"Wrote {Path.GetFileName(path)}";
            _log.Success("Users", $"Exported {(seedSingleExample ? "template" : "list")}: {path}");
        }
        catch (Exception ex) { Status = "Failed: " + ex.Message; _log.Error("Users", ex.Message, ex); }
        finally { Busy = false; }
    }
}

/// <summary>Selectable grid row wrapping a <see cref="UserSummary"/> for the Users page.</summary>
public sealed partial class UserListRow : SelectableRow
{
    public UserListRow(UserSummary source) => Source = source;
    public UserSummary Source { get; }
    public string Username => Source.Username;
    public string? FirstName => Source.FirstName;
    public string? LastName => Source.LastName;
    public string? Email => Source.Email;
    public IReadOnlyList<string> Roles => Source.Roles;
}
