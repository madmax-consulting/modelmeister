using System;
using System.Collections.Generic;
using ModelMeister.Inriver.Diff;

namespace ModelMeister.Ui.Models;

/// <summary>User preferences persisted to <c>settings.json</c> next to the encrypted vault.</summary>
public sealed class AppSettings
{
    /// <summary>The most recently connected environment, used as the default selection in the Environments view.</summary>
    public Guid? LastUsedEnvId { get; set; }

    /// <summary>When set, the UI auto-connects to this environment on startup.</summary>
    public Guid? DefaultEnvId { get; set; }

    /// <summary>Path of the model csproj/dll loaded last time, restored on startup.</summary>
    public string? LastModelPath { get; set; }

    /// <summary>Merge policy toggle: allow updates to Name / Description fields.</summary>
    public bool OverwriteNamesAndDescriptions { get; set; }

    /// <summary>Merge policy toggle: allow updates to CVL value names.</summary>
    public bool OverwriteCvlValues { get; set; }

    /// <summary>Merge policy toggle: allow deletes (deactivations).</summary>
    public bool AllowDeletes { get; set; }

    /// <summary>Merge policy toggle: allow field datatype changes.</summary>
    public bool AllowDatatypeChange { get; set; }

    /// <summary>Merge policy toggle: allow CVL value rename (key migrations).</summary>
    public bool AllowCvlValueRename { get; set; }

    /// <summary>Merge policy toggle: apply field-type Index/sort-order changes on update (off by default → ignored).</summary>
    public bool ApplyFieldIndexChanges { get; set; }

    /// <summary>Merge policy toggle: apply category Index/sort-order changes on update (off by default → ignored).</summary>
    public bool ApplyCategoryIndexChanges { get; set; }

    /// <summary>Merge policy toggle: apply link-type Index/sort-order changes on update (off by default → ignored).</summary>
    public bool ApplyLinkTypeIndexChanges { get; set; }

    /// <summary>MRU list of model csproj/dll paths, newest first. Capped at 10.</summary>
    public List<string> RecentModelPaths { get; set; } = [];

    /// <summary>MRU list of imported Excel workbook paths, newest first. Capped at 10.</summary>
    public List<string> RecentWorkbookPaths { get; set; } = [];

    /// <summary>Persist the log drawer expand/collapse state across sessions.</summary>
    public bool LogDrawerExpanded { get; set; }

    /// <summary>Theme variant preference. <c>true</c> = Dark, <c>false</c> = Light. Defaults to Dark.</summary>
    public bool PreferDarkTheme { get; set; } = true;

    /// <summary>Whether the left rail navigation is expanded (label mode) or collapsed (icon-only).</summary>
    public bool RailExpanded { get; set; } = true;

    /// <summary>Last-selected hub key, restored on startup.</summary>
    public string? LastHubKey { get; set; }

    /// <summary>Per-hub remembered sub-page key, keyed by hub name.</summary>
    public Dictionary<string, string> HubSubPageKeys { get; set; } = new();

    /// <summary>Field-type property names (e.g. "TrackChanges") whose diffs the differ should ignore.</summary>
    public List<string> IgnoredFieldProperties { get; set; } = [];

    /// <summary>Field-type id ignore rules (contains / starts-with / ends-with) applied during diff.</summary>
    public List<FieldIdIgnoreRule> IgnoredFieldIdPatterns { get; set; } = [];

    /// <summary>User-defined environment types plus any edits to the built-in seven. Empty on first run
    /// (the registry seeds the built-ins). Persisted here because types are not secret.</summary>
    public List<EnvironmentType> EnvironmentTypes { get; set; } = [];

    /// <summary>Keys of built-in environment types the user has deleted. The registry skips re-seeding
    /// these on <c>Rebuild</c>, so a deleted built-in stays gone across restarts (the built-ins are
    /// only a starter set).</summary>
    public List<string> DeletedBuiltInTypeKeys { get; set; } = [];

    /// <summary>User-defined organizations plus any edits to the built-in "Default". Empty on first run
    /// (the registry seeds the built-in). Persisted here because organizations are not secret.</summary>
    public List<Organization> Organizations { get; set; } = [];

    /// <summary>Keys of built-in organizations the user has deleted. The registry skips re-seeding these
    /// on <c>Rebuild</c>, so a deleted built-in stays gone across restarts.</summary>
    public List<string> DeletedBuiltInOrgKeys { get; set; } = [];

    /// <summary>The organization currently selected in the global picker. Scopes the Environments page and
    /// every compare page. Restored on startup; falls back to "Default" when unset/unknown.</summary>
    public string? SelectedOrgKey { get; set; }
}
