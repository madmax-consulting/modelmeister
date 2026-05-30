using System.Globalization;
using ModelMeister.Inriver.Snapshot;
using ModelMeister.Inriver.Statistics;

namespace ModelMeister.Inriver.Diff;

/// <summary>What kind of destructive change is putting live data at risk.</summary>
public enum BlastRadiusKind
{
    /// <summary>An entire entity type is being deleted — every instance and all its data goes with it.</summary>
    EntityTypeDelete,
    /// <summary>A field type is being deleted — that field's value is erased on every instance of its owner.</summary>
    FieldDelete,
    /// <summary>A field's datatype is changing — values may be coerced or dropped on every instance.</summary>
    DatatypeChange,
}

/// <summary>One destructive change weighed against how much live data it touches.</summary>
public sealed record BlastRadiusEntry(
    BlastRadiusKind Kind,
    string EntityTypeId,
    string EntityTypeName,
    int EntityCount,
    string Detail)
{
    /// <summary>A single-line, operator-facing summary, e.g. "Delete field SKU.Weight — 48,231 SKU instances".</summary>
    public string Describe()
    {
        var n = EntityCount.ToString("N0", CultureInfo.InvariantCulture);
        return Kind switch
        {
            BlastRadiusKind.EntityTypeDelete => $"Delete entity type {EntityTypeName} — {n} instance(s) and all their data",
            BlastRadiusKind.FieldDelete => $"Delete field {Detail} — clears that value on {n} {EntityTypeName} instance(s)",
            BlastRadiusKind.DatatypeChange => $"Change datatype of {Detail} — re-coerces {n} {EntityTypeName} instance(s)",
            _ => Detail,
        };
    }
}

/// <summary>
/// Pure analysis that joins a <see cref="ModelChangeSet"/>'s destructive changes to live instance
/// counts from <see cref="EntityStatistics"/>, so the UI can warn before an apply wipes real data.
/// Only changes that touch <b>populated</b> entity types are reported — an edit to an empty type is
/// cosmetic and shouldn't cry wolf.
/// </summary>
public static class BlastRadius
{
    /// <summary>
    /// Assess <paramref name="changes"/> against <paramref name="live"/> and <paramref name="stats"/>.
    /// Returns one entry per destructive change touching a populated entity type, heaviest first.
    /// </summary>
    public static IReadOnlyList<BlastRadiusEntry> Assess(ModelChangeSet changes, LiveModel live, EntityStatistics stats)
    {
        ArgumentNullException.ThrowIfNull(changes);
        ArgumentNullException.ThrowIfNull(live);
        ArgumentNullException.ThrowIfNull(stats);

        // field id -> owning entity type id, resolved from the live model (delete records carry only the id).
        var fieldOwner = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var typeName = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var et in live.EntityTypes)
        {
            typeName[et.Id] = string.IsNullOrEmpty(et.Name.DefaultValue) ? et.Id : et.Name.DefaultValue;
            foreach (var f in et.Fields)
                fieldOwner[f.Id] = et.Id;
        }

        string NameOf(string id) => stats.ForType(id)?.Name ?? typeName.GetValueOrDefault(id, id);

        var entries = new List<BlastRadiusEntry>();

        foreach (var change in changes.Changes)
        {
            switch (change)
            {
                case DeleteEntityType del:
                {
                    var count = stats.CountFor(del.Id);
                    if (count > 0)
                        entries.Add(new BlastRadiusEntry(BlastRadiusKind.EntityTypeDelete, del.Id, NameOf(del.Id), count, del.Id));
                    break;
                }
                case DeleteFieldType del when fieldOwner.TryGetValue(del.Id, out var ownerId):
                {
                    var count = stats.CountFor(ownerId);
                    if (count > 0)
                        entries.Add(new BlastRadiusEntry(BlastRadiusKind.FieldDelete, ownerId, NameOf(ownerId), count, del.Id));
                    break;
                }
                case ChangeFieldDatatype dt:
                {
                    var ownerId = dt.Owner.EntityTypeId;
                    var count = stats.CountFor(ownerId);
                    if (count > 0)
                        entries.Add(new BlastRadiusEntry(BlastRadiusKind.DatatypeChange, ownerId, NameOf(ownerId), count, dt.Field.Id));
                    break;
                }
            }
        }

        return entries
            .OrderByDescending(e => e.EntityCount)
            .ThenBy(e => e.EntityTypeName, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    /// <summary>Total instances at risk across all reported entries (a folder may count a type twice — that's intended emphasis).</summary>
    public static int TotalAtRisk(IReadOnlyList<BlastRadiusEntry> entries) => entries.Sum(e => e.EntityCount);
}
