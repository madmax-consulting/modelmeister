namespace ModelMeister.Inriver.WorkAreas;

/// <summary>
/// Plain, inriver-free projection of a shared work-area folder for the UI tree, CLI and Excel. The saved
/// search is carried as an opaque <see cref="QueryJson"/> blob; <see cref="Path"/> (parent-chain of names)
/// is the stable cross-environment identity used for compare and promote.
/// </summary>
public sealed class WorkAreaFolderDto
{
    public Guid Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public Guid? ParentId { get; set; }
    public int Index { get; set; }

    /// <summary>True when the folder holds a saved search rather than a manual entity collection.</summary>
    public bool IsQuery { get; set; }
    public bool IsSyndication { get; set; }

    /// <summary>Serialized <c>ComplexQuery</c> (opaque). Null when the folder has no saved search.</summary>
    public string? QueryJson { get; set; }

    /// <summary>Owner of the folder: a username for a personal folder; <c>null</c> for a shared folder.</summary>
    public string? Username { get; set; }

    /// <summary>Parent-chain of names joined by '/', e.g. <c>Marketing/Launch 2026</c>.</summary>
    public string Path { get; set; } = string.Empty;
}
