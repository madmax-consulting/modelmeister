namespace ModelMeister.Inriver.HtmlTemplates;

/// <summary>
/// Plain, inriver-free projection of an HTML template (print / ContentStore template) for the UI, CLI
/// and Excel. inriver assigns a per-environment integer <see cref="Id"/>, so cross-environment identity
/// is <see cref="Name"/> (+ <see cref="TemplateType"/>) — that is what compare and promote match on.
/// </summary>
public sealed class HtmlTemplateDto
{
    /// <summary>Per-environment id. Meaningless across environments; not used for matching.</summary>
    public int Id { get; set; }

    public string Name { get; set; } = string.Empty;

    /// <summary>inriver template type/category string (e.g. a ContentStore template type). Part of identity.</summary>
    public string TemplateType { get; set; } = string.Empty;

    /// <summary>Opaque properties blob (settings string inriver stores alongside the template).</summary>
    public string Properties { get; set; } = string.Empty;

    /// <summary>The template body (HTML). Can be large — Excel round-trip spills oversize content to a sidecar file.</summary>
    public string Content { get; set; } = string.Empty;

    /// <summary>Per-language display name (iso → value). Empty when inriver carries no localized name.</summary>
    public Dictionary<string, string> LocalizedName { get; set; } = new(StringComparer.OrdinalIgnoreCase);
}
