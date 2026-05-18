namespace ModelMeister.Inriver.Diff;

/// <summary>
/// Flags controlling diff aggressiveness. Defaults are protective — apply will not delete
/// or perform destructive datatype changes unless explicitly allowed. Pass an instance to
/// <see cref="ModelDiffer.Diff"/>, or use <see cref="Default"/> for the safe baseline.
/// </summary>
public sealed record MergePolicy
{
    /// <summary>When true, code-side names and descriptions overwrite live values on update.</summary>
    public bool OverwriteNamesAndDescriptions { get; init; }

    /// <summary>When true, code-side CVL value labels and parent keys overwrite live values.</summary>
    public bool OverwriteCvlValues { get; init; }

    /// <summary>When true (the default), the per-item Index/SortOrder is ignored on update.</summary>
    public bool IgnoreIndexSortingOnUpdate { get; init; } = true;

    /// <summary>When true, items that exist on the live side but not in code are deleted/deactivated.</summary>
    public bool AllowDeletes { get; init; }

    /// <summary>When true, a field's <see cref="Model.Primitives.Datatype"/> change is applied; otherwise it surfaces as a warning.</summary>
    public bool AllowDatatypeChange { get; init; }

    /// <summary>Reserved — when true, a CVL value rename (key change while id is stable) is permitted.</summary>
    public bool AllowCvlValueRename { get; init; }

    /// <summary>The safe baseline: no destructive operations, no cosmetic overwrites.</summary>
    public static readonly MergePolicy Default = new();
}
