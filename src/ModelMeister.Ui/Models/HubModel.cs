using System.Collections.Generic;
using ModelMeister.Ui.ViewModels;

namespace ModelMeister.Ui.Models;

/// <summary>Top-level navigation hub. The sidebar lists one entry per hub.</summary>
public enum Hub
{
    Dashboard,
    // Manage group — each hub has Manage / Compare sub-pages (single-env CRUD vs. env-vs-env).
    Model,
    Cvls,
    Users,
    Roles,
    RestrictedFields,
    Extensions,
    ServerSettings,
    WorkAreas,
    PersonalWorkAreas,
    HtmlTemplates,
    // System group
    Environments,
    BackupRestore,
    Scaffolding,
    Setup,
}

/// <summary>Sidebar grouping label above a set of hubs.</summary>
public enum HubGroup
{
    Home,
    Manage,
    System,
}

/// <summary>Workflow step shown inside the Model hub's Manage sub-page. Drives both the
/// WorkflowStrip's button states and which legacy view is hosted in the body.</summary>
public enum WorkflowStep
{
    Env,
    Load,
    Policy,
    Compare,
    Apply,
}

/// <summary>
/// Static metadata for a hub. <see cref="SubPages"/> defines the segmented tabs shown in the
/// TitleBar; the entry's <see cref="SubPageDescriptor.Legacy"/> wires it to an existing legacy
/// view-model during the migration. New <c>FeaturePage</c>-based sub-pages will replace those
/// targets in later sessions.
/// </summary>
public sealed record HubDescriptor(
    Hub Hub,
    string Title,
    string IconKey,
    HubGroup Group,
    string Subtitle,
    string Description,
    IReadOnlyList<SubPageDescriptor> SubPages,
    bool IsHidden = false);

/// <summary>One sub-page tab inside a hub.</summary>
public sealed record SubPageDescriptor(
    string Key,
    string Title,
    NavTarget? Legacy,
    string Description = "");
