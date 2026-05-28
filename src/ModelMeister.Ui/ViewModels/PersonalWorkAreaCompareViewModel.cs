using System;
using System.Linq;
using System.Threading.Tasks;
using ModelMeister.Ui.Services;

namespace ModelMeister.Ui.ViewModels;

/// <summary>
/// Compare a <b>selected user's</b> personal work-area folders across two environments and promote them —
/// the shared <see cref="WorkAreaCompareViewModel"/> re-pointed at personal scope via <see cref="PersonalUsername"/>.
/// Nothing compares until a user is picked.
/// </summary>
public sealed partial class PersonalWorkAreaCompareViewModel : WorkAreaCompareViewModel
{
    public PersonalWorkAreaCompareViewModel(MainWindowViewModel main, Shell shell, IEnvironmentVault vault, IAppLog log)
        : base(main, shell, vault, log)
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
    protected override bool ScopeReady => SelectedUser is not null;
    protected override string ScopeNotReadyMessage => "Pick a user to compare their personal work areas.";

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
        catch (Exception ex) { Log.Error("ComparePersonalWorkAreas", $"Couldn't list users: {ex.Message}", ex); }
    }
}
