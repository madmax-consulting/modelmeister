using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Styling;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using ModelMeister.Inriver.Diff;
using ModelMeister.Inriver.Snapshot;
using ModelMeister.Model.Loading;
using ModelMeister.Ui.Models;
using ModelMeister.Ui.Services;

namespace ModelMeister.Ui.ViewModels;

/// <summary>
/// Root view-model bound to <see cref="Views.MainWindow"/>. Hosts the per-section child VMs,
/// tracks the workflow step state, owns the shared loaded-model / live-snapshot / change-set
/// state every child reads from, and translates connection-lifecycle events to UI strings + toasts.
/// </summary>
public partial class MainWindowViewModel : ViewModelBase
{
    private readonly IEnvironmentVault _vault;
    private readonly ISettingsStore _settings;
    private readonly IEnvironmentTypeRegistry _envTypes;
    private readonly IOrganizationRegistry _orgs;
    private readonly IConnectionLifecycle _connection;
    private readonly IAppLog _log;
    private readonly Shell _shell;
    private int _lastSeenLogCount;
    private bool _suppressSubPagePersist;

    // Anchors captured at the moment ChangeSet is assigned by DiffViewModel. The diff is "fresh"
    // only while these still match the current LoadedModel / LiveSnapshot / policy / connection.
    private LoadedModel? _changeSetModel;
    private LiveModel? _changeSetLiveSnapshot;
    private string? _changeSetEnvUrl;
    private string? _changeSetPolicySignature;

    /// <summary>Library facade exposed for child view-models.</summary>
    public Shell ShellService => _shell;

    /// <summary>Scoped + full backup capture, listing, and restore for the Ui layer.</summary>
    public BackupService Backups { get; }

    /// <summary>Restores a saved backup snapshot back into the connected environment.</summary>
    public RestoreService Restores { get; }

    /// <summary>Process-wide log + toast bus.</summary>
    public IAppLog Log => _log;

    /// <summary>Vault exposed so secondary view-models (Extensions, Users, …) can read REST credentials.</summary>
    public IEnvironmentVault Vault => _vault;

    /// <summary>Organization registry exposed so child view-models (env editor) can offer the org list.</summary>
    public IOrganizationRegistry Orgs => _orgs;

    /// <summary>The currently connected env, or <c>null</c>. Wraps <see cref="IConnectionLifecycle.Connected"/>.</summary>
    public Models.EnvironmentEntry? ConnectedEnv => _connection.Connected;

    // ----- Legacy per-section VMs. Reachable through SubPageDescriptor.Legacy during the hub migration. -----

    public EnvironmentsViewModel EnvironmentsVm { get; }
    public ModelViewModel ModelVm { get; }
    public PolicyViewModel PolicyVm { get; }
    public DiffViewModel DiffVm { get; }
    public ApplyViewModel ApplyVm { get; }
    public HistoryViewModel HistoryVm { get; }
    public ToolsViewModel ToolsVm { get; }
    public CompareEnvsViewModel CompareEnvsVm { get; }
    public CvlWorkbenchViewModel CvlWorkbenchVm { get; }
    public CvlCompareViewModel CvlCompareVm { get; }
    public UsersViewModel UsersVm { get; }
    public UsersCompareViewModel UsersCompareVm { get; }
    public RolesViewModel RolesVm { get; }
    public RolesCompareViewModel RolesCompareVm { get; }
    public RestrictedFieldsViewModel RestrictedFieldsVm { get; }
    public RestrictedFieldsCompareViewModel RestrictedFieldsCompareVm { get; }
    public ExtensionsViewModel ExtensionsVm { get; }
    public ServerSettingsViewModel ServerSettingsVm { get; }
    public ServerSettingsCompareViewModel ServerSettingsCompareVm { get; }
    public CompareExtensionsViewModel CompareExtensionsVm { get; }
    public WorkAreaViewModel WorkAreaVm { get; }
    public WorkAreaCompareViewModel WorkAreaCompareVm { get; }
    public PersonalWorkAreaViewModel PersonalWorkAreaVm { get; }
    public PersonalWorkAreaCompareViewModel PersonalWorkAreaCompareVm { get; }
    public HtmlTemplatesViewModel HtmlTemplatesVm { get; }
    public HtmlTemplatesCompareViewModel HtmlTemplatesCompareVm { get; }

    // ----- New hub-level VMs (placeholders in Session 1, full content in later sessions). -----

    public DashboardViewModel DashboardVm { get; }
    public SetupViewModel SetupVm { get; }
    public EnvironmentTypesViewModel EnvironmentTypesVm { get; }
    public OrganizationsViewModel OrganizationsVm { get; }
    public SnapshotsViewModel SnapshotsVm { get; }

    /// <summary>Static hub descriptors in sidebar display order, grouped HOME / MANAGE / SYSTEM.
    /// Manage hubs carry two top-level sub-pages: <c>manage</c> (single-env CRUD on the connected env) and
    /// <c>compare</c> (side-by-side env-vs-env with copy-across). The Model hub additionally drives a
    /// WorkflowStrip inside <c>manage</c> for the Env / Load / Policy / Compare / Apply pipeline.</summary>
    public ObservableCollection<HubDescriptor> Hubs { get; } = new()
    {
        new HubDescriptor(Hub.Dashboard, "Dashboard", "IcoHome", HubGroup.Home,
            "OVERVIEW",
            "App landing page. Status across every hub plus quick actions.",
            Array.Empty<SubPageDescriptor>(),
            IsHidden: true),

        // ----- MANAGE: each hub has Manage (single-env) and Compare (env-vs-env) sub-pages -----

        new HubDescriptor(Hub.Model, "Model", "IcoModel", HubGroup.Manage,
            "LOAD · POLICY · COMPARE · APPLY",
            "The model-as-code workflow. Load a model project, configure policy, compare it against the connected env, and apply.",
            new SubPageDescriptor[]
            {
                new("manage",  "Manage",  null, "Single-env workflow: load model, configure policy, compare vs env, apply."),
                new("compare", "Compare", NavTarget.CompareEnvs, "Compare the model across two environments."),
            }),

        new HubDescriptor(Hub.Cvls, "CVLs", "IcoStar", HubGroup.Manage,
            "CONTROLLED VALUE LISTS",
            "List and edit CVLs in the connected env.",
            new SubPageDescriptor[]
            {
                new("manage",  "Manage",  NavTarget.CvlWorkbench, "List and edit CVLs in the connected env."),
                new("compare", "Compare", NavTarget.CvlWorkbench, "Compare CVLs across two environments and copy values across."),
            }),

        new HubDescriptor(Hub.Users, "Users", "IcoUsers", HubGroup.Manage,
            "USERS · ROLES · PROVISIONING",
            "List, export, and provision users in the connected env.",
            new SubPageDescriptor[]
            {
                new("manage",  "Manage",  NavTarget.Users, "List, export, and provision users in the connected env."),
                new("compare", "Compare", NavTarget.Users, "Compare users across two environments."),
            }),

        new HubDescriptor(Hub.Roles, "Roles", "IcoUsers", HubGroup.Manage,
            "ROLES · PERMISSIONS",
            "List, export, and provision roles + their permission bindings in the connected env.",
            new SubPageDescriptor[]
            {
                new("manage",  "Manage",  NavTarget.Roles, "List, export, and provision roles in the connected env."),
                new("compare", "Compare", NavTarget.Roles, "Compare roles across two environments and promote per-row."),
            }),

        new HubDescriptor(Hub.RestrictedFields, "Restricted fields", "IcoShield", HubGroup.Manage,
            "RESTRICTED FIELD PERMISSIONS",
            "List, export, add, and delete restricted-field permissions in the connected env.",
            new SubPageDescriptor[]
            {
                new("manage",  "Manage",  NavTarget.RestrictedFields, "List, add, and delete restricted-field permissions in the connected env."),
                new("compare", "Compare", NavTarget.RestrictedFields, "Compare restricted-field permissions across two environments and promote per-row."),
            }),

        new HubDescriptor(Hub.Extensions, "Extensions", "IcoZap", HubGroup.Manage,
            "EXTENSIONS",
            "Start, stop, run, edit extension settings in the connected env.",
            new SubPageDescriptor[]
            {
                new("manage",  "Manage",  NavTarget.Extensions, "Start, stop, run, edit extension settings in the connected env."),
                new("compare", "Compare", NavTarget.CompareExtensions, "Compare extensions across two environments."),
            },
            IsHidden: true),

        new HubDescriptor(Hub.ServerSettings, "Server settings", "IcoGear", HubGroup.Manage,
            "SERVER SETTINGS",
            "Server-settings dictionary for the connected env.",
            new SubPageDescriptor[]
            {
                new("manage",  "Manage",  NavTarget.ServerSettings, "Server-settings dictionary for the connected env."),
                new("compare", "Compare", NavTarget.ServerSettings, "Compare server settings across two environments."),
            }),

        new HubDescriptor(Hub.WorkAreas, "Work areas", "IcoFolder", HubGroup.Manage,
            "SHARED FOLDERS · SAVED QUERIES",
            "Shared work-area folders and their saved searches in the connected env.",
            new SubPageDescriptor[]
            {
                new("manage",  "Manage",  NavTarget.WorkAreas, "Browse, create, rename, and delete shared folders in the connected env."),
                new("compare", "Compare", NavTarget.CompareWorkAreas, "Compare shared folders across two environments and promote them."),
            }),

        new HubDescriptor(Hub.PersonalWorkAreas, "Personal work areas", "IcoUsers", HubGroup.Manage,
            "PER-USER FOLDERS · SAVED QUERIES",
            "A selected user's personal work-area folders and their saved searches.",
            new SubPageDescriptor[]
            {
                new("manage",  "Manage",  NavTarget.PersonalWorkAreas, "Pick a user, then browse, create, rename, and delete their personal folders."),
                new("compare", "Compare", NavTarget.ComparePersonalWorkAreas, "Compare a user's personal folders across two environments and promote them."),
            }),

        new HubDescriptor(Hub.HtmlTemplates, "HTML templates", "IcoTemplate", HubGroup.Manage,
            "PRINT · CONTENTSTORE TEMPLATES",
            "HTML print / ContentStore templates in the connected env.",
            new SubPageDescriptor[]
            {
                new("manage",  "Manage",  NavTarget.HtmlTemplates, "Edit, create, and delete HTML templates in the connected env."),
                new("compare", "Compare", NavTarget.CompareHtmlTemplates, "Compare HTML templates across two environments and promote them."),
            }),

        // ----- SYSTEM: vault, backups, scaffolding, app prefs -----

        new HubDescriptor(Hub.Environments, "Environments", "IcoEnvironments", HubGroup.System,
            "VAULT",
            "Stored credentials. Connect/disconnect.",
            new SubPageDescriptor[]
            {
                new("orgs",     "Organizations", null, "Group your environments. Pick the active organization in the title bar to scope environments and comparisons."),
                new("vault",    "Manage", null, "Stored credentials. Connect/disconnect, set default."),
                new("envtypes", "Types",  null, "Define and color the environment types you assign to environments."),
            }),

        new HubDescriptor(Hub.BackupRestore, "Backups & Restore", "IcoBackup", HubGroup.System,
            "SNAPSHOTS · RECEIPTS",
            "Backups produced anywhere in the app, plus apply receipts.",
            new SubPageDescriptor[]
            {
                new("snapshots", "Snapshots", null, "Central library of every backup produced anywhere in the app."),
                new("history", "Receipts & history", NavTarget.History, "Apply receipts (dry-run and real) and pre-apply model backups."),
            }),

        new HubDescriptor(Hub.Scaffolding, "Scaffolding", "IcoTools", HubGroup.System,
            "UTILITIES",
            "Scaffolder, Excel export, snapshot capture, probe.",
            Array.Empty<SubPageDescriptor>()),

        new HubDescriptor(Hub.Setup, "Setup", "IcoGear", HubGroup.System,
            "APP PREFERENCES",
            "Theme, defaults, storage, diagnostics, about.",
            Array.Empty<SubPageDescriptor>()),
    };

    /// <summary>Shared source-set state (slot A / slot B / Single|Compare mode). Read by feature pages and the SourceBar.</summary>
    public SourceContext Source { get; } = new();

    /// <summary>Organizations shown in the global title-bar picker; mirrors the registry, refreshed on change.</summary>
    public ObservableCollection<Organization> Organizations { get; } = new();

    /// <summary>The organization currently in scope. Selecting one filters the Environments page and every
    /// compare page (via <see cref="EnvironmentsInScope"/>), so comparisons stay within a single
    /// organization by construction. Persisted as <see cref="AppSettings.SelectedOrgKey"/>.</summary>
    [ObservableProperty] private Organization _selectedOrganization = null!;

    /// <summary>Raised when the in-scope organization changes. The Environments page and every compare VM
    /// subscribe to re-filter their environment list.</summary>
    public event Action? ScopeChanged;

    /// <summary>Vault environments belonging to the currently-selected organization (read-through: an
    /// entry with no <see cref="EnvironmentEntry.OrgKey"/> resolves to "Default"). The single funnel every
    /// environment-listing page reads from.</summary>
    public IReadOnlyList<EnvironmentEntry> EnvironmentsInScope()
        => _vault.List()
            .Where(e => string.Equals(_orgs.Resolve(e.OrgKey).Key, SelectedOrganization.Key, StringComparison.Ordinal))
            .OrderBy(e => e.Name, StringComparer.OrdinalIgnoreCase)
            .ToList();

    private void ReloadOrganizations()
    {
        Organizations.Clear();
        foreach (var o in _orgs.All) Organizations.Add(o);
        // Keep the selection pointing at a live instance (it may have been edited/removed).
        var key = SelectedOrganization?.Key;
        var match = Organizations.FirstOrDefault(o => string.Equals(o.Key, key, StringComparison.Ordinal));
        SelectedOrganization = match ?? Organizations.FirstOrDefault() ?? _orgs.Resolve(null);
    }

    partial void OnSelectedOrganizationChanged(Organization value)
    {
        if (value is null) return;
        _settings.Current.SelectedOrgKey = value.Key;
        _settings.Save();
        ScopeChanged?.Invoke();
    }

    /// <summary>HOME group hubs (just Dashboard today).</summary>
    public IReadOnlyList<HubDescriptor> HomeHubs => Hubs.Where(h => h.Group == HubGroup.Home && !h.IsHidden).ToList();
    /// <summary>True when the HOME group has at least one visible hub — gates the section ListBox.</summary>
    public bool HasHomeHubs => HomeHubs.Count > 0;
    /// <summary>HOME label visibility — only show when the rail is expanded AND the group is non-empty.</summary>
    public bool ShowHomeHeader => IsRailExpanded && HasHomeHubs;
    /// <summary>MANAGE group hubs — each carries Manage / Compare sub-pages.</summary>
    public IReadOnlyList<HubDescriptor> ManageHubs => Hubs.Where(h => h.Group == HubGroup.Manage && !h.IsHidden).ToList();
    /// <summary>SYSTEM group hubs — vault, backups, scaffolding, app preferences.</summary>
    public IReadOnlyList<HubDescriptor> SystemHubs => Hubs.Where(h => h.Group == HubGroup.System && !h.IsHidden).ToList();

    /// <summary>The currently highlighted hub in the left rail.</summary>
    [ObservableProperty] private HubDescriptor _selectedHub;

    /// <summary>Sub-page tabs visible for the current hub (empty for Dashboard / Setup).</summary>
    public ObservableCollection<SubPageDescriptor> SubPages { get; } = new();

    /// <summary>The active sub-page within the selected hub. <c>null</c> on hubs that have no sub-pages.</summary>
    [ObservableProperty] private SubPageDescriptor? _selectedSubPage;

    /// <summary>The view-model rendered in the main pane.</summary>
    [ObservableProperty] private ViewModelBase _currentPage;

    /// <summary>Whether the left nav rail is expanded (label mode) or collapsed (icon-only).</summary>
    [ObservableProperty] private bool _isRailExpanded;

    /// <summary>Human-readable connection state for the status bar ("Connected", "Faulted", ...).</summary>
    [ObservableProperty] private string _connectionStatus = "Disconnected";
    /// <summary>Detail line for the status bar: env name+URL, or the last connect error.</summary>
    [ObservableProperty] private string _connectionDetail = "";
    /// <summary>True when <see cref="IConnectionLifecycle.State"/> is Connected.</summary>
    [ObservableProperty] private bool _isConnected;
    /// <summary>String form of the connected env's stage (drives the stage badge color).</summary>
    [ObservableProperty] private string _connectedStage = nameof(EnvironmentStage.Unspecified);
    /// <summary>True when connected to a prod environment; enables the red prod-guard border.</summary>
    [ObservableProperty] private bool _isProdConnected;

    // ----- shared application state — accessible by children via the parent -----

    /// <summary>The currently loaded model, or <c>null</c> until the user loads one.</summary>
    [ObservableProperty] private LoadedModel? _loadedModel;
    /// <summary>Path to the model csproj/dll currently loaded.</summary>
    [ObservableProperty] private string? _modelPath;
    /// <summary>The most recent live snapshot captured from inriver (driven by Compare).</summary>
    [ObservableProperty] private LiveModel? _liveSnapshot;
    /// <summary>The most recently computed diff between code and snapshot.</summary>
    [ObservableProperty] private ModelChangeSet? _changeSet;
    /// <summary>Live per-entity-type instance counts captured alongside the last Compare. Volatile —
    /// used to weigh an apply's blast radius. Null when statistics are unavailable or stale.</summary>
    [ObservableProperty] private ModelMeister.Inriver.Statistics.EntityStatistics? _entityStats;

    /// <summary>True while a restore-from-backup is in flight. Guards against double-click re-entry.</summary>
    [ObservableProperty] private bool _isRestoring;

    // ----- workflow step state — proper booleans, one per step. Updated by RecomputeSteps() and
    // mirrored onto the per-step WorkflowStepItem entries that drive the WorkflowStrip. -----

    /// <summary>True once the user has connected to an environment.</summary>
    [ObservableProperty] private bool _isEnvDone;
    /// <summary>True once a model project/DLL has been loaded.</summary>
    [ObservableProperty] private bool _isLoadDone;
    /// <summary>Policy is configuration, not a discrete completion — stays <c>false</c>.</summary>
    [ObservableProperty] private bool _isPolicyDone;
    /// <summary>True once a diff has been computed against the live env.</summary>
    [ObservableProperty] private bool _isCompareDone;
    /// <summary>True once a non-dry-run apply has succeeded.</summary>
    [ObservableProperty] private bool _isApplyDone;

    /// <summary>The Env / Load / Policy / Compare / Apply chips shown by <see cref="Controls.WorkflowStrip"/>.</summary>
    public IReadOnlyList<WorkflowStepItem> WorkflowSteps { get; }

    /// <summary>Which workflow step body is shown inside Model → Manage. Driven by the WorkflowStrip buttons.</summary>
    [ObservableProperty] private WorkflowStep _activeWorkflowStep = WorkflowStep.Env;

    // ----- log drawer -----

    /// <summary>Whether the bottom log drawer is shown expanded; persisted to settings.</summary>
    [ObservableProperty] private bool _logDrawerExpanded;
    /// <summary>Number of log entries added since the drawer was last opened.</summary>
    [ObservableProperty] private int _logUnseenCount;

    /// <summary>True when the current theme is Dark; toggled by the header sun/moon button.</summary>
    [ObservableProperty] private bool _isDarkTheme;

    /// <summary>True when the Manage > Model hub is the active hub — drives the WorkflowStrip's visibility.</summary>
    public bool IsModelHubSelected => SelectedHub?.Hub == Hub.Model;

    /// <summary>True when Model → Manage is active. WorkflowStrip is visible only here.</summary>
    public bool IsModelManageActive =>
        SelectedHub?.Hub == Hub.Model && SelectedSubPage?.Key == "manage";

    /// <summary>The first step the user hasn't completed yet — the only pending step that's clickable.</summary>
    public WorkflowStep NextStep => FirstPendingStep();

    /// <summary>True when the active step's exit conditions are met, enabling the Next button.
    /// Policy has no completion criterion — the user moves on whenever they're ready.</summary>
    public bool CanGoNext => ActiveWorkflowStep switch
    {
        WorkflowStep.Env     => IsEnvDone,
        WorkflowStep.Load    => IsLoadDone,
        WorkflowStep.Policy  => true,
        WorkflowStep.Compare => IsCompareDone,
        WorkflowStep.Apply   => false,
        _                    => false,
    };

    /// <summary>Apply is the terminal step — hide the Next button there entirely rather than
    /// leaving it visible-but-disabled.</summary>
    public bool ShowNext => ActiveWorkflowStep != WorkflowStep.Apply;

    public MainWindowViewModel(
        IEnvironmentVault vault,
        ISettingsStore settings,
        IEnvironmentTypeRegistry envTypes,
        IOrganizationRegistry organizations,
        IConnectionLifecycle connection,
        IFileOpener fileOpener,
        IAppLog log,
        Shell shell)
    {
        _vault = vault;
        _settings = settings;
        _envTypes = envTypes;
        _orgs = organizations;
        _connection = connection;
        _log = log;
        _shell = shell;
        Backups = new BackupService(shell, connection, vault);
        Restores = new RestoreService(shell, vault);

        // Initialise the global organization scope before any env-listing child VM is built — they read
        // EnvironmentsInScope() in their constructors. Set the backing field directly so the initial
        // restore doesn't re-persist settings.
        foreach (var o in _orgs.All) Organizations.Add(o);
        _selectedOrganization = _orgs.Resolve(settings.Current.SelectedOrgKey);
        _orgs.Changed += ReloadOrganizations;

        EnvironmentsVm = new EnvironmentsViewModel(this, vault, settings, envTypes, organizations, connection, log);
        ModelVm = new ModelViewModel(this, settings, shell, fileOpener, log);
        PolicyVm = new PolicyViewModel(this, settings);
        DiffVm = new DiffViewModel(this, settings, shell, log);
        ApplyVm = new ApplyViewModel(this, shell, fileOpener, log);
        HistoryVm = new HistoryViewModel(this, shell, fileOpener, log);
        ToolsVm = new ToolsViewModel(this, shell, fileOpener, log);
        CompareEnvsVm = new CompareEnvsViewModel(this, shell, log);
        CvlWorkbenchVm = new CvlWorkbenchViewModel(this, shell, log);
        CvlCompareVm = new CvlCompareViewModel(this, shell, log);
        UsersVm = new UsersViewModel(this, shell, log);
        UsersCompareVm = new UsersCompareViewModel(this, shell, log);
        RolesVm = new RolesViewModel(this, shell, log);
        RolesCompareVm = new RolesCompareViewModel(this, shell, log);
        RestrictedFieldsVm = new RestrictedFieldsViewModel(this, shell, log);
        RestrictedFieldsCompareVm = new RestrictedFieldsCompareViewModel(this, shell, log);
        ExtensionsVm = new ExtensionsViewModel(this, shell, log);
        ServerSettingsVm = new ServerSettingsViewModel(this, shell, log);
        ServerSettingsCompareVm = new ServerSettingsCompareViewModel(this, shell, vault, log);
        CompareExtensionsVm = new CompareExtensionsViewModel(this, shell, vault, log);
        WorkAreaVm = new WorkAreaViewModel(this, shell, log);
        WorkAreaCompareVm = new WorkAreaCompareViewModel(this, shell, vault, log);
        PersonalWorkAreaVm = new PersonalWorkAreaViewModel(this, shell, log);
        PersonalWorkAreaCompareVm = new PersonalWorkAreaCompareViewModel(this, shell, vault, log);
        HtmlTemplatesVm = new HtmlTemplatesViewModel(this, shell, log);
        HtmlTemplatesCompareVm = new HtmlTemplatesCompareViewModel(this, shell, vault, log);

        DashboardVm = new DashboardViewModel(this, log);
        SetupVm = new SetupViewModel(this, settings);
        EnvironmentTypesVm = new EnvironmentTypesViewModel(this, envTypes, vault, log);
        OrganizationsVm = new OrganizationsViewModel(this, organizations, log);
        SnapshotsVm = new SnapshotsViewModel(this, fileOpener, log);

        WorkflowSteps = new[]
        {
            new WorkflowStepItem(WorkflowStep.Env,     "ENVIRONMENT", hasSeparator: true,  GoEnvCommand),
            new WorkflowStepItem(WorkflowStep.Load,    "MODEL",       hasSeparator: true,  GoModelCommand),
            new WorkflowStepItem(WorkflowStep.Policy,  "POLICY",      hasSeparator: true,  GoPolicyCommand),
            new WorkflowStepItem(WorkflowStep.Compare, "COMPARE",     hasSeparator: true,  GoDiffCommand),
            new WorkflowStepItem(WorkflowStep.Apply,   "APPLY",       hasSeparator: false, GoApplyCommand),
        };

        _logDrawerExpanded = settings.Current.LogDrawerExpanded;
        _isDarkTheme = settings.Current.PreferDarkTheme;
        _isRailExpanded = settings.Current.RailExpanded;

        // Always start at Environments — connection is the prerequisite for every feature page.
        var restoredHub = Hubs.First(h => h.Hub == Hub.Environments);
        _suppressSubPagePersist = true;
        _selectedHub = restoredHub;
        RebuildSubPages(restoredHub);
        _currentPage = ResolvePage(restoredHub, SelectedSubPage);
        _suppressSubPagePersist = false;

        _connection.Changed += OnConnectionChanged;

        _log.Entries.CollectionChanged += (_, _) =>
        {
            if (!LogDrawerExpanded)
                LogUnseenCount = _log.Entries.Count - _lastSeenLogCount;
        };

        OnConnectionChanged();
        RecomputeSteps();

        _ = AutoConnectDefaultAsync();
    }

    /// <summary>
    /// If the user has marked an environment as default and its secret is on file, connect to it
    /// at startup. Silent on missing secret / connect failure — the Environments view will show
    /// the unconnected state and the warning column flags any vault gaps.
    /// </summary>
    private async Task AutoConnectDefaultAsync()
    {
        var id = _settings.Current.DefaultEnvId;
        if (id is null) return;

        var entry = _vault.List().FirstOrDefault(e => e.Id == id);
        if (entry is null) return;

        var secret = _vault.GetSecret(entry.Id);
        if (secret is null) return;

        try
        {
            await _connection.ConnectAsync(entry, secret).ConfigureAwait(true);
        }
        catch (Exception ex)
        {
            _log.Warn("Startup", $"Auto-connect to '{entry.Name}' failed: {ex.Message}", ex);
        }
    }

    partial void OnSelectedHubChanged(HubDescriptor value)
    {
        // Defensive: the sidebar ListBoxes are bound OneWay so non-owning groups
        // can't push their cleared SelectedItem back into us, but guard regardless.
        if (value is null) return;

        if (!_suppressSubPagePersist)
        {
            _settings.Current.LastHubKey = value.Hub.ToString();
            _settings.Save();
        }
        RebuildSubPages(value);
        OnPropertyChanged(nameof(IsModelHubSelected));
        OnPropertyChanged(nameof(IsModelManageActive));
    }

    partial void OnSelectedSubPageChanged(SubPageDescriptor? value)
    {
        if (value is not null && !_suppressSubPagePersist)
        {
            _settings.Current.HubSubPageKeys[SelectedHub.Hub.ToString()] = value.Key;
            _settings.Save();
        }
        // Manage hubs: sub-page key drives source mode. "compare" = env-vs-env, anything else = single env.
        Source.Mode = value?.Key == "compare" ? SourceMode.Compare : SourceMode.Single;
        OnPropertyChanged(nameof(IsModelManageActive));

        if (SelectedHub is not null)
            CurrentPage = ResolvePage(SelectedHub, value);
    }

    partial void OnActiveWorkflowStepChanged(WorkflowStep value)
    {
        // Re-stamp which chip glows accent.
        foreach (var item in WorkflowSteps)
            item.IsCurrent = item.Step == value;
        OnPropertyChanged(nameof(CanGoNext));
        OnPropertyChanged(nameof(ShowNext));
        // Switching workflow step only matters when Model → Manage is the active view.
        if (IsModelManageActive)
            CurrentPage = ResolvePage(SelectedHub, SelectedSubPage);

        // Always refresh from the environment when entering the Compare step — the user expects
        // a fresh snapshot/diff every time, even at the cost of losing the prior row exclusions.
        if (value == WorkflowStep.Compare
            && IsConnected
            && LoadedModel is not null
            && !DiffVm.Busy)
        {
            _ = DiffVm.CompareCommand.ExecuteAsync(null);
        }
    }

    /// <summary>Programmatically navigate to a hub. Restores the hub's last-used sub-page if any.</summary>
    public void GoToHub(Hub hub)
    {
        var match = Hubs.FirstOrDefault(h => h.Hub == hub);
        if (match is not null) SelectedHub = match;
    }

    /// <summary>
    /// Back-compat: legacy view-models call <c>_main.GoTo(NavTarget.X)</c>. The Model workflow
    /// targets (Model/Policy/Diff/Apply) now live inside Model → Manage as workflow steps; route
    /// them through the WorkflowStrip. Everything else routes to the owning hub + sub-page.
    /// </summary>
    public void GoTo(NavTarget target)
    {
        // Workflow-step targets — keep the user in Model → Manage and switch the active step.
        switch (target)
        {
            case NavTarget.Model:    GoStep(WorkflowStep.Load);    return;
            case NavTarget.Policy:   GoStep(WorkflowStep.Policy);  return;
            case NavTarget.Diff:     GoStep(WorkflowStep.Compare); return;
            case NavTarget.Apply:    GoStep(WorkflowStep.Apply);   return;
        }

        foreach (var hub in Hubs)
        {
            var match = hub.SubPages.FirstOrDefault(s => s.Legacy == target);
            if (match is null) continue;

            // Navigate to the hub first; that resets sub-pages. Then select the matching sub-page.
            if (!ReferenceEquals(SelectedHub, hub))
                SelectedHub = hub;
            SelectedSubPage = SubPages.FirstOrDefault(s => s.Key == match.Key);
            return;
        }
    }

    /// <summary>Switch to a workflow step within Model → Manage, navigating to that hub first if needed.
    /// Leaving Policy for any downstream step marks Policy done — chip-click navigation should produce
    /// the same green check as clicking Next.</summary>
    public void GoStep(WorkflowStep step)
    {
        var modelHub = Hubs.FirstOrDefault(h => h.Hub == Hub.Model);
        if (modelHub is not null && !ReferenceEquals(SelectedHub, modelHub))
            SelectedHub = modelHub;

        var manage = SubPages.FirstOrDefault(s => s.Key == "manage");
        if (manage is not null && !ReferenceEquals(SelectedSubPage, manage))
            SelectedSubPage = manage;

        if (ActiveWorkflowStep == WorkflowStep.Policy && step != WorkflowStep.Policy)
        {
            IsPolicyDone = true;
            RecomputeSteps();
        }

        ActiveWorkflowStep = step;
        // Force CurrentPage refresh even when the step value didn't change.
        CurrentPage = ResolvePage(SelectedHub, SelectedSubPage);
    }

    [RelayCommand] private void GoEnv()    => GoStep(WorkflowStep.Env);
    [RelayCommand] private void GoModel()  => GoStep(WorkflowStep.Load);
    [RelayCommand] private void GoPolicy() => GoStep(WorkflowStep.Policy);
    [RelayCommand] private void GoDiff()   => GoStep(WorkflowStep.Compare);
    [RelayCommand] private void GoApply()  => GoStep(WorkflowStep.Apply);

    /// <summary>Advance to the next workflow step in strip order. Bound to the strip's Next button.
    /// Clicking Next from Policy marks it done — that's the chip's only path to green, since policy
    /// has no automatic completion criterion.</summary>
    [RelayCommand]
    private void GoNext()
    {
        if (ActiveWorkflowStep == WorkflowStep.Policy)
        {
            IsPolicyDone = true;
            RecomputeSteps();
        }
        var next = ActiveWorkflowStep switch
        {
            WorkflowStep.Env     => WorkflowStep.Load,
            WorkflowStep.Load    => WorkflowStep.Policy,
            WorkflowStep.Policy  => WorkflowStep.Compare,
            WorkflowStep.Compare => WorkflowStep.Apply,
            _                    => ActiveWorkflowStep,
        };
        GoStep(next);
    }

    /// <summary>Toggle the left rail between expanded (with labels) and collapsed (icon-only).</summary>
    [RelayCommand]
    public void ToggleRail()
    {
        IsRailExpanded = !IsRailExpanded;
        _settings.Current.RailExpanded = IsRailExpanded;
        _settings.Save();
    }

    private void RebuildSubPages(HubDescriptor hub)
    {
        SubPages.Clear();
        foreach (var sp in hub.SubPages) SubPages.Add(sp);

        SubPageDescriptor? next = null;
        if (SubPages.Count > 0)
        {
            var rememberedKey = _settings.Current.HubSubPageKeys.GetValueOrDefault(hub.Hub.ToString());
            next = SubPages.FirstOrDefault(s => s.Key == rememberedKey) ?? SubPages[0];
        }

        // Always force an update so CurrentPage is resolved even when staying on the same descriptor.
        var prev = SelectedSubPage;
        SelectedSubPage = null;
        SelectedSubPage = next;
        if (next is null && ReferenceEquals(prev, null))
        {
            // Hubs with no sub-pages (Dashboard, Setup) — resolve CurrentPage directly.
            CurrentPage = ResolvePage(hub, null);
        }
    }

    private ViewModelBase ResolvePage(HubDescriptor hub, SubPageDescriptor? sub) =>
        (hub.Hub, sub?.Key) switch
        {
            (Hub.Dashboard, _)               => DashboardVm,
            (Hub.Setup, _)                   => SetupVm,
            (Hub.BackupRestore, "snapshots") => SnapshotsVm,
            // Model → Manage: WorkflowStrip drives which legacy step VM is hosted.
            (Hub.Model, "manage")            => ResolveWorkflowStep(),
            (Hub.Model, "compare")           => CompareEnvsVm,
            // Other Manage hubs: Manage = single-env VM, Compare = env-vs-env (some reuse with Source.Mode=Compare).
            (Hub.Cvls, "compare")            => CvlCompareVm,
            (Hub.Cvls, _)                    => CvlWorkbenchVm,
            (Hub.Users, "compare")           => UsersCompareVm,
            (Hub.Users, _)                   => UsersVm,
            (Hub.Roles, "compare")           => RolesCompareVm,
            (Hub.Roles, _)                   => RolesVm,
            (Hub.RestrictedFields, "compare")=> RestrictedFieldsCompareVm,
            (Hub.RestrictedFields, _)        => RestrictedFieldsVm,
            (Hub.Extensions, "compare")      => CompareExtensionsVm,
            (Hub.Extensions, _)              => ExtensionsVm,
            (Hub.ServerSettings, "compare")  => ServerSettingsCompareVm,
            (Hub.ServerSettings, _)          => ServerSettingsVm,
            (Hub.WorkAreas, "compare")       => WorkAreaCompareVm,
            (Hub.WorkAreas, _)               => WorkAreaVm,
            (Hub.PersonalWorkAreas, "compare") => PersonalWorkAreaCompareVm,
            (Hub.PersonalWorkAreas, _)         => PersonalWorkAreaVm,
            (Hub.HtmlTemplates, "compare")   => HtmlTemplatesCompareVm,
            (Hub.HtmlTemplates, _)           => HtmlTemplatesVm,
            // System hubs
            (Hub.Environments, "orgs")       => OrganizationsVm,
            (Hub.Environments, "envtypes")   => EnvironmentTypesVm,
            (Hub.Environments, _)            => EnvironmentsVm,
            (Hub.Scaffolding, _)             => ToolsVm,
            _ => ResolveLegacy(sub?.Legacy),
        };

    private ViewModelBase ResolveWorkflowStep() => ActiveWorkflowStep switch
    {
        WorkflowStep.Env     => EnvironmentsVm,
        WorkflowStep.Load    => ModelVm,
        WorkflowStep.Policy  => PolicyVm,
        WorkflowStep.Compare => DiffVm,
        WorkflowStep.Apply   => ApplyVm,
        _                    => ModelVm,
    };

    private ViewModelBase ResolveLegacy(NavTarget? target) => target switch
    {
        NavTarget.Environments      => EnvironmentsVm,
        NavTarget.Model             => ModelVm,
        NavTarget.Policy            => PolicyVm,
        NavTarget.Diff              => DiffVm,
        NavTarget.Apply             => ApplyVm,
        NavTarget.History           => HistoryVm,
        NavTarget.Tools             => ToolsVm,
        NavTarget.CompareEnvs       => CompareEnvsVm,
        NavTarget.CvlWorkbench      => CvlWorkbenchVm,
        NavTarget.Users             => UsersVm,
        NavTarget.Roles             => RolesVm,
        NavTarget.RestrictedFields  => RestrictedFieldsVm,
        NavTarget.Extensions        => ExtensionsVm,
        NavTarget.ServerSettings    => ServerSettingsVm,
        NavTarget.CompareExtensions => CompareExtensionsVm,
        NavTarget.WorkAreas         => WorkAreaVm,
        NavTarget.CompareWorkAreas  => WorkAreaCompareVm,
        NavTarget.PersonalWorkAreas => PersonalWorkAreaVm,
        NavTarget.ComparePersonalWorkAreas => PersonalWorkAreaCompareVm,
        NavTarget.HtmlTemplates     => HtmlTemplatesVm,
        NavTarget.CompareHtmlTemplates => HtmlTemplatesCompareVm,
        _                            => DashboardVm,
    };

    /// <summary>Called by <see cref="PolicyViewModel"/> when any policy toggle changes — refreshes step state and dependent VMs.</summary>
    public void NotifyPolicyChanged()
    {
        RecomputeSteps();
        DiffVm.OnPolicyChanged();
    }

    [RelayCommand]
    private void DismissToast(ToastEntry? entry)
    {
        if (entry is not null) _log.DismissToast(entry);
    }

    /// <summary>
    /// Restore-from-backup: scaffolds the backup snapshot as a typed C# project to a temp folder,
    /// builds/loads it, swaps it into <see cref="LoadedModel"/>, and routes the user to Compare so they
    /// can review the reverse change set before applying.
    /// </summary>
    public async Task RestoreFromBackupAsync(string backupSnapshotPath)
    {
        if (IsRestoring) return;
        if (!File.Exists(backupSnapshotPath))
        {
            _log.Error("Restore", $"Backup file not found: {backupSnapshotPath}");
            return;
        }

        IsRestoring = true;
        try
        {
            _log.Info("Restore", $"Loading backup snapshot {Path.GetFileName(backupSnapshotPath)}…");
            var live = await _shell.LoadSnapshotJsonAsync(backupSnapshotPath).ConfigureAwait(true);

            // Compare runs against whatever is connected, NOT the env the backup came from. If those
            // differ, a careless Apply would revert the wrong environment — so warn before routing.
            var backupUrl = live.EnvironmentUrl;
            var connectedUrl = ConnectedEnv?.Url;
            if (IsConnected
                && !string.IsNullOrEmpty(backupUrl)
                && !string.Equals(backupUrl, connectedUrl, StringComparison.OrdinalIgnoreCase))
            {
                var proceed = await DialogHost.ConfirmAsync(
                    "Backup is from a different environment",
                    $"This backup was taken from:\n{backupUrl}\n\nbut you are connected to:\n{connectedUrl}\n\n"
                    + "Compare and Apply will target the CONNECTED environment, not the backup's origin. "
                    + "Connect to the backup's environment first to revert it. Continue anyway?",
                    confirmLabel: "Continue anyway",
                    cancelLabel: "Cancel").ConfigureAwait(true);
                if (!proceed)
                {
                    _log.Info("Restore", "Cancelled — connected environment differs from the backup's origin.");
                    return;
                }
            }
            else if (!string.IsNullOrEmpty(backupUrl))
            {
                _log.Toast(LogLevel.Info, "Backup origin",
                    $"This backup is from {backupUrl}. Compare will target the connected environment.");
            }

            var token = Guid.NewGuid().ToString("N")[..8];
            var tempDir = Path.Combine(Path.GetTempPath(), $"modelmeister-restore-{token}");
            Directory.CreateDirectory(tempDir);

            _log.Info("Restore", $"Scaffolding backup as model project to {tempDir}");
            await _shell.ScaffoldFromLiveModelAsync(live, tempDir, "Restored.PimModel", detectBaseClasses: true)
                .ConfigureAwait(true);

            // Load the freshly scaffolded csproj as a typed model.
            var csproj = Directory.EnumerateFiles(tempDir, "*.csproj").FirstOrDefault()
                ?? throw new InvalidOperationException("Scaffold did not produce a csproj.");

            _log.Info("Restore", "Building scaffolded project…");
            var loaded = await _shell.LoadModelAsync(csproj).ConfigureAwait(true);

            LoadedModel = loaded;
            ModelPath = csproj;
            ChangeSet = null;

            _log.Success("Restore", "Backup loaded as model. Run Compare to see what will revert.");
            _log.Toast(LogLevel.Success, "Backup loaded",
                "Run Compare to see the changes needed to revert the live env to this backup.");
            GoTo(NavTarget.Diff);
        }
        catch (Exception ex)
        {
            _log.Error("Restore", $"Restore failed: {ex.Message}", ex);
            _log.Toast(LogLevel.Error, "Restore failed", ex.Message);
        }
        finally
        {
            IsRestoring = false;
        }
    }

    /// <summary>Disconnect from the current environment. Surfaced by the title-bar connection chip's flyout so disconnect is reachable from any page.</summary>
    [RelayCommand]
    public async Task DisconnectAsync() => await _connection.DisconnectAsync().ConfigureAwait(true);

    /// <summary>
    /// Connect (or switch) to the given environment from the title-bar chip's environment-picker
    /// flyout. Clicking the currently-connected env is a no-op; clicking a different env
    /// disconnects first, then connects.
    /// </summary>
    [RelayCommand]
    public async Task ConnectToEnvAsync(Models.EnvironmentEntry? entry)
    {
        if (entry is null) return;
        if (_connection.Connected?.Id == entry.Id) return;
        if (_connection.State == ConnectionState.Connected)
            await _connection.DisconnectAsync().ConfigureAwait(true);
        await EnvironmentsVm.ConnectToEnvironmentAsync(entry).ConfigureAwait(true);
    }

    /// <summary>Navigate to the Environments hub. Surfaced by the title-bar connection chip's flyout.</summary>
    [RelayCommand]
    public void GoToEnvironments() => GoToHub(Hub.Environments);

    /// <summary>Navigate to the Model hub. Bound to <c>Ctrl+Shift+M</c>.</summary>
    [RelayCommand]
    public void GoToModelHub() => GoToHub(Hub.Model);

    /// <summary>Trigger Refresh on the active page if it inherits <see cref="FeaturePageViewModel"/>. Bound to <c>F5</c>.</summary>
    [RelayCommand]
    public async Task RefreshCurrentPageAsync()
    {
        if (CurrentPage is FeaturePageViewModel fp && fp.RefreshCommand.CanExecute(null))
            await fp.RefreshCommand.ExecuteAsync(null).ConfigureAwait(true);
    }

    /// <summary>Flip between Dark and Light theme and persist the choice.</summary>
    [RelayCommand]
    public void ToggleTheme()
    {
        IsDarkTheme = !IsDarkTheme;
        if (Application.Current is { } app)
            app.RequestedThemeVariant = IsDarkTheme ? ThemeVariant.Dark : ThemeVariant.Light;
        _settings.Current.PreferDarkTheme = IsDarkTheme;
        _settings.Save();
    }

    /// <summary>Toggle the bottom log drawer and persist the new state.</summary>
    [RelayCommand]
    public void ToggleLogDrawer()
    {
        LogDrawerExpanded = !LogDrawerExpanded;
        _settings.Current.LogDrawerExpanded = LogDrawerExpanded;
        _settings.Save();
        if (LogDrawerExpanded)
        {
            _lastSeenLogCount = _log.Entries.Count;
            LogUnseenCount = 0;
        }
    }

    /// <summary>Clear the in-app log buffer.</summary>
    [RelayCommand]
    public void ClearLog()
    {
        _log.Clear();
        _lastSeenLogCount = 0;
        LogUnseenCount = 0;
    }

    partial void OnLoadedModelChanged(LoadedModel? value)
    {
        // A model swap invalidates any cached diff — it was computed against a different model instance.
        if (!ReferenceEquals(_changeSetModel, value) && ChangeSet is not null)
            InvalidateChangeSet("Model changed");
        RecomputeSteps();
    }

    partial void OnChangeSetChanged(ModelChangeSet? value) => RecomputeSteps();

    // Entity statistics are tied to a specific live snapshot; drop them when the snapshot clears so a
    // later apply never weighs blast radius against counts from a different (or disconnected) env.
    partial void OnLiveSnapshotChanged(LiveModel? value)
    {
        if (value is null) EntityStats = null;
    }
    partial void OnIsConnectedChanged(bool value) => RecomputeSteps();
    partial void OnIsRailExpandedChanged(bool value) => OnPropertyChanged(nameof(ShowHomeHeader));

    // Feature pages that read from the connected env (Extensions, Users, CvlWorkbench, …) only
    // ran their initial load when IsConnected transitioned to true — navigating to them later
    // showed empty content. Trigger a load on page entry, but go through EnsureLoadedAsync so it
    // respects the page's dirty flag: it loads on first open, re-fetches after a mutation or env
    // switch (both MarkDataDirty), and no-ops on every other navigation. (F5 still force-refreshes.)
    partial void OnCurrentPageChanged(ViewModelBase value)
    {
        if (value is FeaturePageViewModel page) _ = page.EnsureLoadedAsync();
    }

    /// <summary>Called by <see cref="ApplyViewModel"/> once a real (non-dry-run) apply succeeds.</summary>
    public void NotifyApplyCompleted(bool dryRun)
    {
        if (!dryRun)
        {
            IsApplyDone = true;
            // The diff and snapshot now describe state that has already been applied. Clear both
            // so the user must re-run Compare before any further Apply — prevents the "applied twice"
            // crash where ChangeApplier sends already-applied operations and the server errors.
            LiveSnapshot = null;
            InvalidateChangeSet(null);
            RecomputeSteps();
        }
    }

    /// <summary>
    /// Called by <see cref="DiffViewModel.CompareAsync"/> immediately after assigning <see cref="ChangeSet"/>.
    /// Captures the upstream values the diff was computed against, so later changes to any of them
    /// (model swap, env switch, policy toggle) can be detected.
    /// </summary>
    internal void StampChangeSetAnchors(LiveModel live, string policySignature)
    {
        _changeSetModel = LoadedModel;
        _changeSetLiveSnapshot = live;
        _changeSetEnvUrl = ConnectedEnv?.Url;
        _changeSetPolicySignature = policySignature;
    }

    /// <summary>
    /// Drop the cached <see cref="ChangeSet"/> (the user must run Compare again before Apply).
    /// Optionally raises an Info-log entry so the user understands why the Apply chip just greyed.
    /// </summary>
    internal void InvalidateChangeSet(string? reasonForLog)
    {
        if (ChangeSet is null) return;
        ChangeSet = null;
        IsCompareDone = false;
        _changeSetModel = null;
        _changeSetLiveSnapshot = null;
        _changeSetEnvUrl = null;
        _changeSetPolicySignature = null;
        RecomputeSteps();
        if (reasonForLog is not null)
            _log.Info("State", $"{reasonForLog} — re-run Compare.");
    }

    private void RecomputeSteps()
    {
        IsEnvDone     = IsConnected;
        IsLoadDone    = LoadedModel is not null;
        IsCompareDone = ChangeSet is not null;
        // IsPolicyDone and IsApplyDone are sticky — set by the user's Next click (Policy) or by
        // NotifyApplyCompleted (Apply). They stay true for the session unless explicitly reset.

        // No auto-advance: the user drives transitions via the Next button or by clicking a chip.

        // Restamp the per-chip view-models (done glyph, accent highlight, clickable gate).
        UpdateStepItems();
        OnPropertyChanged(nameof(NextStep));
        OnPropertyChanged(nameof(CanGoNext));
    }

    private void UpdateStepItems()
    {
        foreach (var item in WorkflowSteps)
        {
            item.IsDone = IsDoneFor(item.Step);
            item.IsCurrent = item.Step == ActiveWorkflowStep;
            item.IsClickable = IsReachable(item.Step);
        }
    }

    /// <summary>A chip is clickable once its required prerequisites are met. Policy is optional
    /// configuration — it doesn't gate Compare or Apply. Apply additionally requires the diff to
    /// contain at least one change — an "in sync" state shouldn't expose Apply at all.</summary>
    private bool IsReachable(WorkflowStep step) => step switch
    {
        WorkflowStep.Env     => true,
        WorkflowStep.Load    => IsEnvDone,
        WorkflowStep.Policy  => IsEnvDone && IsLoadDone,
        WorkflowStep.Compare => IsEnvDone && IsLoadDone,
        WorkflowStep.Apply   => IsEnvDone && IsLoadDone && IsCompareDone
                                && (ChangeSet?.Changes.Count ?? 0) > 0,
        _                    => false,
    };

    private bool IsDoneFor(WorkflowStep step) => step switch
    {
        WorkflowStep.Env     => IsEnvDone,
        WorkflowStep.Load    => IsLoadDone,
        WorkflowStep.Policy  => IsPolicyDone,
        WorkflowStep.Compare => IsCompareDone,
        WorkflowStep.Apply   => IsApplyDone,
        _                    => false,
    };

    /// <summary>The first workflow step that hasn't been completed yet — used by Dashboard's
    /// Continue button to jump the user to the next required action.</summary>
    private WorkflowStep FirstPendingStep()
    {
        if (!IsEnvDone)     return WorkflowStep.Env;
        if (!IsLoadDone)    return WorkflowStep.Load;
        if (!IsCompareDone) return WorkflowStep.Compare;
        if (!IsApplyDone)   return WorkflowStep.Apply;
        return WorkflowStep.Apply;
    }

    /// <summary>Shared persisted settings — exposed so feature pages (e.g. Excel import recents) can
    /// read/write the same store the main window uses.</summary>
    public ISettingsStore Settings => _settings;

    private bool _suspendConnectionIndicator;

    /// <summary>
    /// While true, transient connection switches don't repaint the connection indicator (top-right
    /// chip + bottom-left status). A Compare refresh swaps the single Remoting connection to each
    /// environment in turn to read both sides; without this the indicator flashed through every env
    /// mid-refresh. Compare VMs set this around their switch sequence; setting it back to false
    /// settles the indicator to the now-current connection exactly once.
    /// </summary>
    public bool SuspendConnectionIndicator
    {
        get => _suspendConnectionIndicator;
        set
        {
            if (_suspendConnectionIndicator == value) return;
            _suspendConnectionIndicator = value;
            if (!value) OnConnectionChanged(); // settle to the final connection once, no flashing
        }
    }

    private void OnConnectionChanged()
    {
        // A compare is mid-refresh, cycling the connection through each env — don't flash the indicator.
        if (_suspendConnectionIndicator) return;

        IsConnected = _connection.State == ConnectionState.Connected;
        ConnectionStatus = _connection.State.ToString();

        var env = _connection.Connected;
        ConnectionDetail = env is not null
            ? $"{env.Name}  ({env.Url})"
            : _connection.LastError ?? "No environment connected";
        ConnectedStage = env?.TypeKey ?? EnvironmentTypeRegistry.UnspecifiedKey;
        IsProdConnected = IsConnected && _envTypes.IsProtected(env?.TypeKey);

        // Connection swapped (or disconnected): any cached snapshot/diff was computed against a
        // different env and is now meaningless. Clear so Apply can't fire against the wrong env.
        var newUrl = env?.Url;
        if (_changeSetEnvUrl is not null && !string.Equals(_changeSetEnvUrl, newUrl, StringComparison.Ordinal))
        {
            LiveSnapshot = null;
            InvalidateChangeSet("Connection changed");
        }
        else if (!IsConnected && (LiveSnapshot is not null || ChangeSet is not null))
        {
            LiveSnapshot = null;
            InvalidateChangeSet(ChangeSet is not null ? "Disconnected" : null);
        }
        OnPropertyChanged(nameof(ConnectedEnv));

        // Slot A mirrors the active connection so every feature page sees a sensible default.
        Source.SlotA = env is not null
            ? new SourceSlot(SourceSlotKind.LiveEnv, env)
            : SourceSlot.None;

        switch (_connection.State)
        {
            case ConnectionState.Connected when env is not null:
                _log.Success("Connection", $"Connected to '{env.Name}' ({env.Url}).");
                break;
            case ConnectionState.Faulted:
                var err = _connection.LastError ?? "unknown error";
                _log.Error("Connection", $"Connect failed: {err}");
                _log.Toast(LogLevel.Error, "Connect failed", err);
                break;
            case ConnectionState.Disconnected when env is null:
                // intentionally silent on the initial Disconnected emit at startup
                break;
        }
    }
}

/// <summary>Identifier for one of the main-window navigation sections. Retained for back-compat
/// during the hub migration — legacy view-models still call <c>_main.GoTo(NavTarget.X)</c>.</summary>
public enum NavTarget { Environments, Model, Policy, Diff, Apply, History, Tools, CompareEnvs, CvlWorkbench, Users, Roles, RestrictedFields, Extensions, ServerSettings, CompareExtensions, WorkAreas, CompareWorkAreas, PersonalWorkAreas, ComparePersonalWorkAreas, HtmlTemplates, CompareHtmlTemplates }
