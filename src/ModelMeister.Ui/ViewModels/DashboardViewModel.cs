using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading.Tasks;
using Avalonia.Media;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using ModelMeister.Inriver.Diff;
using ModelMeister.Ui.Models;
using ModelMeister.Ui.Services;

namespace ModelMeister.Ui.ViewModels;

/// <summary>
/// Dashboard landing view-model. Surfaces high-level state across the six hubs and offers a few
/// quick actions. Reads everything from <see cref="MainWindowViewModel"/>; raises property-changed
/// notifications when relevant root state moves so tiles re-render.
/// </summary>
public partial class DashboardViewModel : ViewModelBase
{
    private readonly MainWindowViewModel _main;
    private readonly IAppLog _log;

    public DashboardViewModel(MainWindowViewModel main, IAppLog log)
    {
        _main = main;
        _log = log;
        _main.PropertyChanged += (_, e) =>
        {
            switch (e.PropertyName)
            {
                case nameof(MainWindowViewModel.ConnectionStatus):
                case nameof(MainWindowViewModel.IsConnected):
                case nameof(MainWindowViewModel.ConnectionDetail):
                case nameof(MainWindowViewModel.ConnectedStage):
                    OnPropertyChanged(nameof(IsConnected));
                    OnPropertyChanged(nameof(ConnectionDetail));
                    OnPropertyChanged(nameof(ConnectionStatus));
                    OnPropertyChanged(nameof(ConnectedStage));
                    OnPropertyChanged(nameof(ShowColdStart));
                    OnPropertyChanged(nameof(WelcomeSubtitle));
                    break;
                case nameof(MainWindowViewModel.LoadedModel):
                case nameof(MainWindowViewModel.ModelPath):
                    OnPropertyChanged(nameof(ModelPath));
                    OnPropertyChanged(nameof(HasModel));
                    OnPropertyChanged(nameof(ModelStatus));
                    OnPropertyChanged(nameof(ShowColdStart));
                    OnPropertyChanged(nameof(WelcomeSubtitle));
                    RaiseModelStats();
                    break;
                case nameof(MainWindowViewModel.ChangeSet):
                    OnPropertyChanged(nameof(PendingChangeCount));
                    OnPropertyChanged(nameof(HasPendingChanges));
                    OnPropertyChanged(nameof(PendingTotal));
                    OnPropertyChanged(nameof(PendingByOp));
                    OnPropertyChanged(nameof(PendingByConcept));
                    OnPropertyChanged(nameof(HasPendingConceptOverflow));
                    OnPropertyChanged(nameof(PendingOverflowLabel));
                    break;
                case nameof(MainWindowViewModel.IsEnvDone):
                case nameof(MainWindowViewModel.IsLoadDone):
                case nameof(MainWindowViewModel.IsPolicyDone):
                case nameof(MainWindowViewModel.IsCompareDone):
                case nameof(MainWindowViewModel.IsApplyDone):
                    OnPropertyChanged(nameof(NextStepLabel));
                    OnPropertyChanged(nameof(NextStep));
                    OnPropertyChanged(nameof(WorkflowDoneCount));
                    OnPropertyChanged(nameof(WorkflowFraction));
                    OnPropertyChanged(nameof(WorkflowProgressLabel));
                    OnPropertyChanged(nameof(WorkflowDots));
                    OnPropertyChanged(nameof(WorkflowDonutGeometry));
                    OnPropertyChanged(nameof(IsWorkflowComplete));
                    OnPropertyChanged(nameof(HasPartialProgress));
                    break;
            }
        };

        _main.Backups.Changed += OnBackupsChanged;
    }

    private void OnBackupsChanged()
    {
        // BackupService fires Changed on whichever thread captured the backup — marshal to UI.
        Dispatcher.UIThread.Post(() =>
        {
            OnPropertyChanged(nameof(LastBackup));
            OnPropertyChanged(nameof(HasAnyBackup));
            OnPropertyChanged(nameof(RecentBackups));
            OnPropertyChanged(nameof(TotalBackupCount));
            OnPropertyChanged(nameof(LastBackupAgo));
            OnPropertyChanged(nameof(BackupsLast14Days));
            OnPropertyChanged(nameof(HasAnyBackupInWindow));
        });
    }

    private void RaiseModelStats()
    {
        OnPropertyChanged(nameof(EntityTypeCount));
        OnPropertyChanged(nameof(FieldCount));
        OnPropertyChanged(nameof(CvlCount));
        OnPropertyChanged(nameof(CvlValueCount));
        OnPropertyChanged(nameof(CategoryCount));
        OnPropertyChanged(nameof(FieldsetCount));
        OnPropertyChanged(nameof(LinkTypeCount));
        OnPropertyChanged(nameof(LanguageCount));
        OnPropertyChanged(nameof(AvgFieldsPerEntityLabel));
        OnPropertyChanged(nameof(EntitiesSubLabel));
        OnPropertyChanged(nameof(FieldsSubLabel));
        OnPropertyChanged(nameof(CvlsSubLabel));
        OnPropertyChanged(nameof(CategoriesSubLabel));
    }

    // ----- Connection -----
    public bool IsConnected => _main.IsConnected;
    public string ConnectionStatus => _main.ConnectionStatus;
    public string ConnectionDetail => _main.ConnectionDetail;
    public string ConnectedStage => _main.ConnectedStage;

    // ----- Model -----
    public bool HasModel => _main.LoadedModel is not null;
    public string? ModelPath => _main.ModelPath;
    public string ModelStatus => _main.LoadedModel is { } m
        ? $"{m.EntityTypes.Count} entities · {m.Cvls.Count} CVLs · {m.Categories.Count} categories"
        : "No model loaded.";

    public int EntityTypeCount => _main.LoadedModel?.EntityTypes.Count ?? 0;
    public int FieldCount => _main.LoadedModel?.EntityTypes.Sum(et => et.Fields.Count) ?? 0;
    public int CvlCount => _main.LoadedModel?.Cvls.Count ?? 0;
    public int CvlValueCount => _main.LoadedModel?.Cvls.Sum(c => c.Values.Count) ?? 0;
    public int CategoryCount => _main.LoadedModel?.Categories.Count ?? 0;
    public int FieldsetCount => _main.LoadedModel?.Fieldsets.Count ?? 0;
    public int LinkTypeCount => _main.LoadedModel?.LinkTypes.Count ?? 0;
    public int LanguageCount => _main.LoadedModel?.Languages.Count ?? 0;

    public string AvgFieldsPerEntityLabel
    {
        get
        {
            if (_main.LoadedModel is not { } m || m.EntityTypes.Count == 0) return "—";
            var avg = (double)m.EntityTypes.Sum(et => et.Fields.Count) / m.EntityTypes.Count;
            return string.Format(CultureInfo.InvariantCulture, "avg {0:0.0} / entity", avg);
        }
    }

    public string EntitiesSubLabel => HasModel ? AvgFieldsPerEntityLabel : "no model loaded";
    public string FieldsSubLabel => HasModel ? $"{LinkTypeCount} link types" : "—";
    public string CvlsSubLabel => HasModel ? $"{CvlValueCount} values" : "—";
    public string CategoriesSubLabel => HasModel ? $"{FieldsetCount} fieldsets · {LanguageCount} languages" : "—";

    // ----- Welcome hero -----
    public string WelcomeHeadline => "Welcome back";

    public string WelcomeSubtitle
    {
        get
        {
            var connection = IsConnected ? ConnectionDetail : "Not connected.";
            return $"{connection}   ·   {ModelStatus}";
        }
    }

    // ----- Workflow progress -----
    public int WorkflowDoneCount =>
        (_main.IsEnvDone ? 1 : 0)
        + (_main.IsLoadDone ? 1 : 0)
        + (_main.IsPolicyDone ? 1 : 0)
        + (_main.IsCompareDone ? 1 : 0)
        + (_main.IsApplyDone ? 1 : 0);

    public double WorkflowFraction => WorkflowDoneCount / 5.0;

    public string WorkflowProgressLabel => $"{WorkflowDoneCount} of 5 complete";

    public bool IsWorkflowComplete => WorkflowDoneCount >= 5;
    public bool HasPartialProgress => WorkflowDoneCount > 0 && WorkflowDoneCount < 5;

    public IReadOnlyList<WorkflowDot> WorkflowDots
    {
        get
        {
            var current = NextStep;
            return new[]
            {
                Dot(WorkflowStep.Env,     "ENV",     _main.IsEnvDone,     current),
                Dot(WorkflowStep.Load,    "MODEL",   _main.IsLoadDone,    current),
                Dot(WorkflowStep.Policy,  "POLICY",  _main.IsPolicyDone,  current),
                Dot(WorkflowStep.Compare, "COMPARE", _main.IsCompareDone, current),
                Dot(WorkflowStep.Apply,   "APPLY",   _main.IsApplyDone,   current),
            };
        }
    }

    private static WorkflowDot Dot(WorkflowStep step, string label, bool isDone, WorkflowStep current)
        => new(label, isDone, !isDone && step == current);

    /// <summary>Stream-geometry for the workflow-progress donut arc. Renders a 96x96 arc with radius 40
    /// centered at (48,48), starting at 12 o'clock and sweeping clockwise. Returns <c>null</c> at the
    /// degenerate 0% / 100% endpoints — the XAML overlays a faint full-ring and a full bright-ring
    /// there respectively.</summary>
    public Geometry? WorkflowDonutGeometry
    {
        get
        {
            var fraction = WorkflowFraction;
            if (fraction <= 0 || fraction >= 1) return null;
            var angle = fraction * 2 * Math.PI;
            var ex = 48 + 40 * Math.Sin(angle);
            var ey = 48 - 40 * Math.Cos(angle);
            var large = fraction > 0.5 ? 1 : 0;
            var path = string.Format(
                CultureInfo.InvariantCulture,
                "M 48,8 A 40,40 0 {0} 1 {1:0.###},{2:0.###}",
                large, ex, ey);
            return StreamGeometry.Parse(path);
        }
    }

    public string NextStepLabel
    {
        get
        {
            if (!_main.IsEnvDone) return "Connect to an environment";
            if (!_main.IsLoadDone) return "Load a model";
            if (!_main.IsCompareDone) return "Compare code to env";
            if (!_main.IsApplyDone) return "Apply changes";
            return "Workflow complete";
        }
    }

    /// <summary>Workflow step the Continue button should jump to within Model → Manage.
    /// EnvStep "not done" routes to the Env step (system Environments view rendered inline).</summary>
    public WorkflowStep NextStep
    {
        get
        {
            if (!_main.IsEnvDone)     return WorkflowStep.Env;
            if (!_main.IsLoadDone)    return WorkflowStep.Load;
            if (!_main.IsCompareDone) return WorkflowStep.Compare;
            return WorkflowStep.Apply;
        }
    }

    // ----- Pending changes -----
    public int PendingChangeCount => _main.ChangeSet?.Changes.Count ?? 0;
    public int PendingTotal => PendingChangeCount;
    public bool HasPendingChanges => PendingChangeCount > 0;

    /// <summary>Four fixed rows (Add/Update/Delete/Other) showing the count + fraction of the largest
    /// bucket. Always returns 4 entries so the XAML layout is stable. <c>Fraction</c> is normalized
    /// against the largest count in the set; when all four are zero, fraction is zero.</summary>
    public IReadOnlyList<KindCount> PendingByOp
    {
        get
        {
            var changes = _main.ChangeSet?.Changes ?? Array.Empty<ModelChange>();
            int adds = 0, updates = 0, deletes = 0, other = 0;
            foreach (var c in changes)
            {
                switch (ClassifyOp(c.GetType().Name))
                {
                    case "Add":    adds++; break;
                    case "Update": updates++; break;
                    case "Delete": deletes++; break;
                    default:       other++; break;
                }
            }
            var max = Math.Max(1, Math.Max(Math.Max(adds, updates), Math.Max(deletes, other)));
            return new[]
            {
                new KindCount("Adds",    "Add",    adds,    (double)adds    / max),
                new KindCount("Updates", "Update", updates, (double)updates / max),
                new KindCount("Deletes", "Delete", deletes, (double)deletes / max),
                new KindCount("Other",   "Other",  other,   (double)other   / max),
            };
        }
    }

    /// <summary>Top 6 concepts (entity/field/cvl/category/...) by count. Concepts smaller than the
    /// top-6 cutoff are summed into <see cref="PendingOverflowLabel"/>.</summary>
    public IReadOnlyList<DashConceptCount> PendingByConcept
    {
        get
        {
            var changes = _main.ChangeSet?.Changes ?? Array.Empty<ModelChange>();
            if (changes.Count == 0) return Array.Empty<DashConceptCount>();

            var groups = changes
                .GroupBy(c => ExtractConcept(c.GetType().Name))
                .Select(g => new { Label = g.Key, Count = g.Count() })
                .OrderByDescending(x => x.Count)
                .ToList();

            var top = groups.Take(6).ToList();
            var max = Math.Max(1, top[0].Count);
            return top
                .Select(x => new DashConceptCount(x.Label, x.Count, (double)x.Count / max))
                .ToList();
        }
    }

    public bool HasPendingConceptOverflow
    {
        get
        {
            var total = _main.ChangeSet?.Changes.Count ?? 0;
            if (total == 0) return false;
            var top6 = (_main.ChangeSet?.Changes ?? Array.Empty<ModelChange>())
                .GroupBy(c => ExtractConcept(c.GetType().Name))
                .OrderByDescending(g => g.Count())
                .Take(6)
                .Sum(g => g.Count());
            return top6 < total;
        }
    }

    public string PendingOverflowLabel
    {
        get
        {
            var changes = _main.ChangeSet?.Changes ?? Array.Empty<ModelChange>();
            var total = changes.Count;
            if (total == 0) return string.Empty;
            var top6 = changes
                .GroupBy(c => ExtractConcept(c.GetType().Name))
                .OrderByDescending(g => g.Count())
                .Take(6)
                .Sum(g => g.Count());
            var rest = total - top6;
            return rest > 0 ? $"+{rest} more across smaller concepts" : string.Empty;
        }
    }

    private static string ClassifyOp(string typeName)
    {
        if (typeName.StartsWith("Add", StringComparison.Ordinal)) return "Add";
        if (typeName.StartsWith("Update", StringComparison.Ordinal)
            || typeName.StartsWith("Change", StringComparison.Ordinal)) return "Update";
        if (typeName.StartsWith("Delete", StringComparison.Ordinal)
            || typeName.StartsWith("Remove", StringComparison.Ordinal)
            || typeName.StartsWith("Deactivate", StringComparison.Ordinal)) return "Delete";
        return "Other";
    }

    /// <summary>Friendly bucket name for a ModelChange record type. Strips the verb prefix
    /// (Add/Update/Delete/Remove/Deactivate/Change) and the trailing suffix verbs
    /// (ToRole/FromFieldset/Datatype/Permission) that describe the action target.</summary>
    private static string ExtractConcept(string typeName)
    {
        // Strip verb prefix.
        var s = typeName;
        foreach (var prefix in s_prefixes)
        {
            if (s.StartsWith(prefix, StringComparison.Ordinal))
            {
                s = s[prefix.Length..];
                break;
            }
        }
        // Strip trailing modifiers like "ToRole", "FromFieldset", etc.
        foreach (var suffix in s_suffixes)
        {
            if (s.EndsWith(suffix, StringComparison.Ordinal))
            {
                s = s[..^suffix.Length];
                break;
            }
        }
        // Camel-case to spaced label: "EntityType" -> "Entity type", "CvlValue" -> "CVL value".
        return Humanize(s);
    }

    private static readonly string[] s_prefixes =
        { "Deactivate", "Add", "Update", "Delete", "Remove", "Change" };
    private static readonly string[] s_suffixes =
        { "ToRole", "FromRole", "FromFieldset", "ToFieldset", "Datatype" };

    private static string Humanize(string raw)
    {
        if (string.IsNullOrEmpty(raw)) return "Other";
        // Specific overrides for nicer reads.
        return raw switch
        {
            "Cvl"             => "CVLs",
            "CvlValue"        => "CVL values",
            "EntityType"      => "Entity types",
            "Field"           => "Fields",
            "LinkType"        => "Link types",
            "Fieldset"        => "Fieldsets",
            "Category"        => "Categories",
            "Language"        => "Languages",
            "Role"            => "Roles",
            "Permission"      => "Permissions",
            "RestrictedFieldPermission" => "Field permissions",
            "PermissionRole"  => "Role permissions",
            "FieldFieldset"   => "Fieldset fields",
            _                  => raw,
        };
    }

    // ----- Last backup -----
    public BackupFileInfo? LastBackup => _main.Backups.List().FirstOrDefault();
    public bool HasAnyBackup => _main.Backups.List().Count > 0;

    public IReadOnlyList<BackupFileInfo> RecentBackups => _main.Backups.List().Take(4).ToList();
    public int TotalBackupCount => _main.Backups.List().Count;

    public string LastBackupAgo
    {
        get
        {
            var last = LastBackup;
            if (last is null) return "—";
            var delta = DateTime.UtcNow - last.CapturedAtUtc;
            if (delta.TotalSeconds < 60) return "just now";
            if (delta.TotalMinutes < 60) return $"{(int)delta.TotalMinutes}m ago";
            if (delta.TotalHours < 48) return $"{(int)delta.TotalHours}h ago";
            return $"{(int)delta.TotalDays}d ago";
        }
    }

    /// <summary>14 day-buckets in chronological order (oldest first), each with a normalized 0..1
    /// height for the sparkline. Empty input → all heights 0.</summary>
    public IReadOnlyList<DayBucket> BackupsLast14Days
    {
        get
        {
            var today = DateTime.UtcNow.Date;
            var start = today.AddDays(-13);
            var counts = new int[14];
            foreach (var b in _main.Backups.List())
            {
                var day = b.CapturedAtUtc.Date;
                if (day < start || day > today) continue;
                var idx = (day - start).Days;
                if (idx is >= 0 and < 14) counts[idx]++;
            }
            var max = counts.Max();
            var result = new DayBucket[14];
            for (int i = 0; i < 14; i++)
            {
                var height = max == 0 ? 0d : (double)counts[i] / max;
                result[i] = new DayBucket(DateOnly.FromDateTime(start.AddDays(i)), counts[i], height);
            }
            return result;
        }
    }

    public bool HasAnyBackupInWindow => BackupsLast14Days.Any(d => d.Count > 0);

    // ----- Cold start -----
    public bool ShowColdStart => !IsConnected && !HasModel;

    // ----- Navigation actions -----
    [RelayCommand] private void GoEnvironments() => _main.GoToHub(Hub.Environments);
    [RelayCommand] private void GoModel() => _main.GoToHub(Hub.Model);
    [RelayCommand] private void GoBackup() => _main.GoToHub(Hub.BackupRestore);
    [RelayCommand] private void GoTools() => _main.GoToHub(Hub.Scaffolding);
    [RelayCommand] private void GoUsers() => _main.GoToHub(Hub.Users);
    [RelayCommand] private void GoSetup() => _main.GoToHub(Hub.Setup);

    [RelayCommand]
    private void Continue() => _main.GoStep(NextStep);

    /// <summary>Jump directly to the Compare step within Model → Manage. Triggers the compare run
    /// automatically because <see cref="MainWindowViewModel.OnActiveWorkflowStepChanged"/> kicks
    /// off a fresh compare when entering that step with a model + connection.</summary>
    [RelayCommand]
    private void CompareNow() => _main.GoStep(WorkflowStep.Compare);

    /// <summary>Jump to the Policy step — the closest landing for a manual validation review until
    /// a dedicated validator page exists.</summary>
    [RelayCommand]
    private void Validate() => _main.GoStep(WorkflowStep.Policy);

    [RelayCommand]
    private async Task FullBackupAsync()
    {
        if (!_main.IsConnected)
        {
            _log.Toast(LogLevel.Warn, "Backup", "Connect to an environment first.");
            return;
        }
        try
        {
            // Skip the model slice if nothing's loaded — capturing a Model backup needs the live snapshot path.
            var path = await _main.Backups.CaptureFullAsync(includeModel: _main.LoadedModel is not null).ConfigureAwait(true);
            _log.Success("Backup", $"Full snapshot saved → {path}");
            _log.Toast(LogLevel.Success, "Full snapshot saved", System.IO.Path.GetFileName(path));
            // Backups.Changed fires for us — but raise here too in case the subscriber is racey.
            OnPropertyChanged(nameof(LastBackup));
            OnPropertyChanged(nameof(HasAnyBackup));
            OnPropertyChanged(nameof(RecentBackups));
            OnPropertyChanged(nameof(BackupsLast14Days));
            OnPropertyChanged(nameof(TotalBackupCount));
            OnPropertyChanged(nameof(LastBackupAgo));
            OnPropertyChanged(nameof(HasAnyBackupInWindow));
        }
        catch (Exception ex)
        {
            _log.Error("Backup", $"Full snapshot failed: {ex.Message}", ex);
            _log.Toast(LogLevel.Error, "Backup failed", ex.Message);
        }
    }
}

/// <summary>One bar in the dashboard's pending-changes "by operation" mini chart.</summary>
public sealed record KindCount(string Label, string Op, int Count, double Fraction);

/// <summary>One bar in the dashboard's pending-changes "by concept" mini chart.</summary>
public sealed record DashConceptCount(string Label, int Count, double Fraction);

/// <summary>One bar in the dashboard's 14-day backup activity sparkline.</summary>
public sealed record DayBucket(DateOnly Day, int Count, double NormalizedHeight);

/// <summary>One segment in the dashboard's 5-step workflow strip. Combination of done/current
/// determines the bar color; XAML chooses via the three exposed bool flags.</summary>
public sealed record WorkflowDot(string Label, bool IsDone, bool IsCurrent)
{
    /// <summary>True when neither done nor current — drawn in the faint border color.</summary>
    public bool IsPending => !IsDone && !IsCurrent;
}
