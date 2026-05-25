using System.Text;

namespace ModelMeister.Model.Primitives;

/// <summary>
/// Turns a PascalCase/camelCase CLR identifier into a human-readable display name by inserting
/// spaces at word boundaries: <c>ProductList</c> → <c>Product List</c>, <c>WeightGrams</c> →
/// <c>Weight Grams</c>, <c>CvlId</c> → <c>Cvl Id</c>, <c>XMLField</c> → <c>XML Field</c>,
/// <c>Weight2</c> → <c>Weight 2</c>. Already-spaced or single-word names pass through unchanged
/// (<c>Density</c> → <c>Density</c>). Underscores and hyphens are treated as word separators.
/// </summary>
/// <remarks>
/// Used to default display names across the model (fields, entity types, categories, fieldsets,
/// roles, …) so authors don't have to spell out a label that mirrors the property/class name.
/// </remarks>
public static class NameHumanizer
{
    public static string Humanize(string identifier)
    {
        if (string.IsNullOrEmpty(identifier)) return identifier;

        var sb = new StringBuilder(identifier.Length + 8);
        for (var i = 0; i < identifier.Length; i++)
        {
            var c = identifier[i];

            // Underscores / hyphens collapse to a single separating space.
            if (c is '_' or '-')
            {
                if (sb.Length > 0 && sb[^1] != ' ') sb.Append(' ');
                continue;
            }

            if (i > 0)
            {
                var prev = identifier[i - 1];
                var boundary =
                    // lower/digit → Upper:  "productList" → "product List"
                    (char.IsUpper(c) && (char.IsLower(prev) || char.IsDigit(prev)))
                    // end of an acronym run → Upper + lower:  "XMLField" → "XML Field"
                    || (char.IsUpper(c) && char.IsUpper(prev)
                        && i + 1 < identifier.Length && char.IsLower(identifier[i + 1]))
                    // letter → digit:  "Weight2" → "Weight 2"
                    || (char.IsDigit(c) && char.IsLetter(prev));

                if (boundary && sb.Length > 0 && sb[^1] != ' ') sb.Append(' ');
            }

            sb.Append(c);
        }

        return sb.ToString().Trim();
    }
}
