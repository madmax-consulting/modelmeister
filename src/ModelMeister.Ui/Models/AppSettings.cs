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
}
