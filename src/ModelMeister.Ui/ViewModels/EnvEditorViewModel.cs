using System;
using System.Diagnostics;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using ModelMeister.Rest;
using ModelMeister.Ui.Models;
using ModelMeister.Ui.Services;

namespace ModelMeister.Ui.ViewModels;

/// <summary>
/// View-model behind the Add/Edit environment dialog. Mutates a working copy of the entry +
/// secret and only writes back to the originals when the user clicks Save.
/// </summary>
public partial class EnvEditorViewModel : ViewModelBase
{
    /// <summary>Default Remoting URL pre-filled for new connections.</summary>
    public const string DefaultUrl = "https://remoting.productmarketingcloud.com";

    /// <summary>Default REST API base URL pre-filled for new connections.</summary>
    public const string DefaultRestBaseUrl = "https://apieuw.productmarketingcloud.com";

    private readonly IConnectionLifecycle? _connection;
    private readonly IAppLog? _log;

    /// <summary>Working-copy entry; the surrounding view-model rebinds field values to this on Save.</summary>
    public EnvironmentEntry Entry { get; }

    /// <summary>Working-copy secret; the surrounding view-model rebinds field values to this on Save.</summary>
    public EnvironmentSecret Secret { get; }

    /// <summary>Allowed values for the Stage dropdown.</summary>
    public EnvironmentStage[] Stages { get; } =
    {
        EnvironmentStage.Unspecified,
        EnvironmentStage.Dev,
        EnvironmentStage.Test,
        EnvironmentStage.QA,
        EnvironmentStage.UAT,
        EnvironmentStage.Stage,
        EnvironmentStage.Prod,
    };

    /// <summary>Display name shown in the environment list.</summary>
    [ObservableProperty] private string _name;
    /// <summary>The inriver Remoting endpoint URL.</summary>
    [ObservableProperty] private string _url;
    /// <summary>Optional base URL for the inriver REST API (e.g. https://apieuw.productmarketingcloud.com).</summary>
    [ObservableProperty] private string? _restBaseUrl;
    /// <summary>Stage (Dev/Test/Prod/...) used for the prod-guard styling.</summary>
    [ObservableProperty] private EnvironmentStage _stage;
    /// <summary>API key for the Remoting connection.</summary>
    [ObservableProperty] private string? _apiKey;
    /// <summary>Separate REST API key (used for user creation + Extensions). May be the same as ApiKey in many envs.</summary>
    [ObservableProperty] private string? _restApiKey;
    /// <summary>Free-form user notes attached to the entry.</summary>
    [ObservableProperty] private string? _notes;
    /// <summary>When true, this entry is the auto-connect-on-startup default. Bound from the surrounding VM.</summary>
    [ObservableProperty] private bool _isDefault;
    /// <summary>Validation message shown in red under the Save button.</summary>
    [ObservableProperty] private string _validation = "";
    /// <summary>True while a Test probe is in flight.</summary>
    [ObservableProperty] private bool _testing;
    /// <summary>Last test result string ("OK · 123 ms" or "FAILED · &lt;error&gt;").</summary>
    [ObservableProperty] private string _testResult = "";
    /// <summary>True when the last test connected successfully.</summary>
    [ObservableProperty] private bool _testSucceeded;

    /// <summary>The dialog result, set just before <see cref="Closed"/> fires.</summary>
    public bool? Result { get; private set; }

    /// <summary>Raised when the dialog should close (Save or Cancel).</summary>
    public event Action? Closed;

    public EnvEditorViewModel(
        EnvironmentEntry entry,
        EnvironmentSecret secret,
        bool isDefault = false,
        IConnectionLifecycle? connection = null,
        IAppLog? log = null)
    {
        Entry = entry;
        Secret = secret;
        _connection = connection;
        _log = log;

        _name = entry.Name;
        _url = string.IsNullOrWhiteSpace(entry.Url) ? DefaultUrl : entry.Url;
        _restBaseUrl = string.IsNullOrWhiteSpace(entry.RestBaseUrl) ? DefaultRestBaseUrl : entry.RestBaseUrl;
        _stage = entry.Stage;
        _apiKey = secret.ApiKey;
        _restApiKey = secret.RestApiKey;
        _notes = entry.Notes;
        _isDefault = isDefault;
    }

    [RelayCommand]
    private void Save()
    {
        if (!ValidateForm()) return;

        Entry.Name = Name.Trim();
        Entry.Url = Url.Trim();
        Entry.RestBaseUrl = string.IsNullOrWhiteSpace(RestBaseUrl) ? null : RestBaseUrl!.Trim();
        Entry.Stage = Stage;
        Entry.Notes = string.IsNullOrWhiteSpace(Notes) ? null : Notes;

        Secret.ApiKey = ApiKey;
        Secret.RestApiKey = string.IsNullOrWhiteSpace(RestApiKey) ? null : RestApiKey;

        Result = true;
        Closed?.Invoke();
    }

    [RelayCommand]
    private void Cancel()
    {
        Result = false;
        Closed?.Invoke();
    }

    /// <summary>
    /// Probe the URL + API key currently in the form before saving. Re-uses the same connect path
    /// the rest of the app uses, then disconnects so we don't leave an unsaved env attached.
    /// </summary>
    [RelayCommand]
    private async Task TestAsync()
    {
        if (_connection is null)
        {
            TestResult = "Test unavailable (no connection service).";
            return;
        }
        if (!ValidateForm()) return;

        Testing = true;
        TestResult = "Testing…";
        TestSucceeded = false;
        try
        {
            var probeEntry = new EnvironmentEntry
            {
                Id = Entry.Id,
                Name = Name.Trim(),
                Url = Url.Trim(),
                Stage = Stage,
            };
            var probeSecret = new EnvironmentSecret { ApiKey = ApiKey };

            // --- Remoting probe ---
            var sw = Stopwatch.StartNew();
            await _connection.ConnectAsync(probeEntry, probeSecret).ConfigureAwait(true);
            sw.Stop();
            var remotingMs = sw.ElapsedMilliseconds;
            var remotingOk = _connection.State == ConnectionState.Connected;
            var remotingErr = remotingOk ? null : (_connection.LastError ?? "unknown error");
            if (remotingOk) await _connection.DisconnectAsync().ConfigureAwait(true);

            // --- REST probe (only when a REST key is configured) ---
            string? restPart;
            bool restOk = true;
            if (!string.IsNullOrWhiteSpace(RestApiKey) && !string.IsNullOrWhiteSpace(RestBaseUrl))
            {
                try
                {
                    using var rest = new InriverRestClient(RestBaseUrl!.Trim(), RestApiKey!.Trim());
                    var rsw = Stopwatch.StartNew();
                    restOk = await rest.PingAsync().ConfigureAwait(true);
                    rsw.Stop();
                    restPart = restOk ? $"REST OK · {rsw.ElapsedMilliseconds} ms" : "REST FAILED · unauthorized or unreachable";
                }
                catch (Exception ex)
                {
                    restOk = false;
                    restPart = $"REST FAILED · {ex.Message}";
                }
            }
            else
            {
                restPart = "REST skipped (no key)";
            }

            var remotingPart = remotingOk
                ? $"Remoting OK · {remotingMs} ms"
                : $"Remoting FAILED · {remotingErr}";

            TestSucceeded = remotingOk && restOk;
            TestResult = $"{remotingPart} · {restPart}";

            if (TestSucceeded)
                _log?.Success("EnvEditor", $"Test '{probeEntry.Name}' OK · {TestResult}.");
            else
                _log?.Error("EnvEditor", $"Test '{probeEntry.Name}' failed · {TestResult}.");
        }
        catch (Exception ex)
        {
            TestSucceeded = false;
            TestResult = $"FAILED · {ex.Message}";
            _log?.Error("EnvEditor", "Test threw: " + ex.Message);
        }
        finally
        {
            Testing = false;
        }
    }

    private bool ValidateForm()
    {
        if (string.IsNullOrWhiteSpace(Name))
        {
            Validation = "Name is required.";
            return false;
        }
        if (string.IsNullOrWhiteSpace(Url))
        {
            Validation = "URL is required.";
            return false;
        }
        if (string.IsNullOrWhiteSpace(ApiKey))
        {
            Validation = "API key is required.";
            return false;
        }

        Validation = "";
        return true;
    }
}
