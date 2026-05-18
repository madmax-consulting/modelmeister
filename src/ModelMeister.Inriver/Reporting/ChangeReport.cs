using System.Text;
using System.Text.Json;
using ModelMeister.Inriver.Diff;

namespace ModelMeister.Inriver.Reporting;

/// <summary>Human-readable and JSON renderers for a <see cref="ModelChangeSet"/>.</summary>
public static class ChangeReport
{
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

    /// <summary>Render the change set as a multi-line text report, grouped by concept (Fields, CVLs, etc.).</summary>
    public static string ToText(ModelChangeSet set)
    {
        if (set.IsEmpty && set.Warnings.Count == 0) return "No changes.";

        var sb = new StringBuilder();
        sb.AppendLine($"{set.Changes.Count} change(s):");

        var groups = set.Changes
            .GroupBy(c => GroupOf(c.GetType().Name))
            .OrderBy(g => g.Key);
        foreach (var grp in groups)
        {
            sb.AppendLine($"  [{grp.Key}]");
            foreach (var ch in grp) sb.AppendLine($"    {ch.Describe()}");
        }

        if (set.Warnings.Count > 0)
        {
            sb.AppendLine();
            sb.AppendLine($"{set.Warnings.Count} warning(s):");
            foreach (var w in set.Warnings) sb.AppendLine($"  ! {w.Code}: {w.Message}");
        }
        return sb.ToString();
    }

    /// <summary>Render the change set as a structured JSON document for machine consumption.</summary>
    public static string ToJson(ModelChangeSet set)
    {
        var payload = new
        {
            Count = set.Changes.Count,
            set.Warnings,
            Changes = set.Changes.Select(c => new { Kind = c.GetType().Name, Description = c.Describe() }),
        };
        return JsonSerializer.Serialize(payload, JsonOptions);
    }

    /// <summary>Bucket a <see cref="ModelChange"/> CLR type name into a human-friendly grouping.</summary>
    private static string GroupOf(string kind) => kind switch
    {
        _ when kind.Contains("EntityType") => "EntityTypes",
        _ when kind.Contains("FieldType") || kind.Contains("Field") => "Fields",
        _ when kind.Contains("Cvl") => "CVLs",
        _ when kind.Contains("Fieldset") => "Fieldsets",
        _ when kind.Contains("LinkType") => "LinkTypes",
        _ when kind.Contains("Category") => "Categories",
        _ when kind.Contains("Role") || kind.Contains("Permission") => "Security",
        _ when kind.Contains("Language") => "Languages",
        _ when kind.Contains("Completeness") => "Completeness",
        _ => "Other",
    };
}
