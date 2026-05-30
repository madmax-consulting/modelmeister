using Shouldly;
using ModelMeister.Inriver.Snapshot;
using Xunit;
using Flags = ModelMeister.Inriver.Snapshot.EnvironmentChangesService.ChangeFlags;

namespace ModelMeister.Inriver.Tests;

/// <summary>
/// Drift mapping: inriver's flag bag becomes an ordered, friendly area list, and the "any changes"
/// roll-up is honoured even when the explicit flags disagree (so a stale snapshot is never trusted).
/// </summary>
public class EnvironmentChangesTests
{
    private static readonly DateTime Since = new(2026, 5, 30, 8, 0, 0, DateTimeKind.Utc);
    private static readonly DateTime Now = new(2026, 5, 30, 9, 0, 0, DateTimeKind.Utc);

    [Fact]
    public void No_flags_set_reports_no_changes()
    {
        var changes = EnvironmentChangesService.FromFlags(new Flags(), Since, Now);
        changes.AnyChanges.ShouldBeFalse();
        changes.ChangedAreas.ShouldBeEmpty();
        changes.Summary().ShouldContain("No model changes");
    }

    [Fact]
    public void Flagged_areas_are_ordered_by_impact()
    {
        var flags = new Flags { AnyChanges = true, Roles = true, EntityTypes = true, CvlValues = true };
        var changes = EnvironmentChangesService.FromFlags(flags, Since, Now);
        changes.AnyChanges.ShouldBeTrue();
        // Entity types outrank CVL values, which outrank Roles.
        changes.ChangedAreas.ShouldBe(new[] { "Entity types", "CVL values", "Roles" });
        changes.Summary().ShouldBe("Entity types, CVL values and Roles changed since the snapshot was captured.");
    }

    [Fact]
    public void Single_area_summary_reads_naturally()
    {
        var flags = new Flags { AnyChanges = true, FieldTypes = true };
        EnvironmentChangesService.FromFlags(flags, Since, Now).Summary()
            .ShouldBe("Fields changed since the snapshot was captured.");
    }

    [Fact]
    public void Model_reload_counts_as_a_change_even_without_area_flags()
    {
        var flags = new Flags { AnyChanges = false, ModelReloaded = true };
        var changes = EnvironmentChangesService.FromFlags(flags, Since, Now);
        changes.AnyChanges.ShouldBeTrue();
        changes.ModelReloaded.ShouldBeTrue();
    }

    [Fact]
    public void Any_flagged_area_forces_any_changes_even_if_rollup_lags()
    {
        var flags = new Flags { AnyChanges = false, Categories = true };
        EnvironmentChangesService.FromFlags(flags, Since, Now).AnyChanges.ShouldBeTrue();
    }
}
