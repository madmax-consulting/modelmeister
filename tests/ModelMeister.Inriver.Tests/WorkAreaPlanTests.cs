using System;
using System.Collections.Generic;
using System.Linq;
using inRiver.Remoting.Query;
using Shouldly;
using ModelMeister.Inriver.WorkAreas;
using Xunit;
using IriverWorkAreaFolder = inRiver.Remoting.Objects.WorkAreaFolder;

namespace ModelMeister.Inriver.Tests;

/// <summary>
/// Pins the pure reconcile planner that the import workflow drives one folder at a time:
/// creates ordered parents-before-children, deletes deepest-first, and create-vs-update classification.
/// This is the behaviour the legacy whole-set <c>ApplyAsync</c> relied on (it now wraps the same planner).
/// </summary>
public class WorkAreaPlanTests
{
    private static WorkAreaService.DesiredFolder D(string path, string? parent, int index = 0) =>
        new(path, parent, path.Split('/').Last(), index, IsQuery: false, IsSyndication: false, Query: null);

    private static IriverWorkAreaFolder Live(Guid id, string name, Guid? parent) =>
        new() { Id = id, Name = name, ParentId = parent };

    [Fact]
    public void Creates_are_ordered_parents_before_children()
    {
        var desired = new List<WorkAreaService.DesiredFolder>
        {
            D("A/B/C", "A/B"),
            D("A", null),
            D("A/B", "A"),
        };

        var (_, actions) = WorkAreaService.BuildPlan(Array.Empty<IriverWorkAreaFolder>(), desired, allowDeletes: false);

        actions.Select(a => a.Path).ShouldBe(new[] { "A", "A/B", "A/B/C" });
        actions.ShouldAllBe(a => a.Kind == WorkAreaActionKind.Create);
        // Each child's parent path appears earlier in the list, so its parent is created first.
        for (var i = 0; i < actions.Count; i++)
            if (actions[i].ParentPath is { } pp)
                actions.Take(i).Any(a => a.Path == pp).ShouldBeTrue();
    }

    [Fact]
    public void Existing_folder_is_update_missing_is_create()
    {
        var aId = Guid.NewGuid();
        var live = new[] { Live(aId, "A", null) };
        var desired = new List<WorkAreaService.DesiredFolder> { D("A", null), D("A/New", "A") };

        var (_, actions) = WorkAreaService.BuildPlan(live, desired, allowDeletes: false);

        var a = actions.Single(x => x.Path == "A");
        a.Kind.ShouldBe(WorkAreaActionKind.Update);
        a.LiveId.ShouldBe(aId);
        a.CurrentName.ShouldBe("A");
        actions.Single(x => x.Path == "A/New").Kind.ShouldBe(WorkAreaActionKind.Create);
    }

    [Fact]
    public void Deletes_are_appended_deepest_first_when_allowed()
    {
        var aId = Guid.NewGuid();
        var xId = Guid.NewGuid();
        var yId = Guid.NewGuid();
        var live = new[]
        {
            Live(aId, "A", null),
            Live(xId, "X", aId),     // A/X
            Live(yId, "Y", xId),     // A/X/Y
        };
        // Desired keeps only A → A/X and A/X/Y must be deleted, children before parents.
        var desired = new List<WorkAreaService.DesiredFolder> { D("A", null) };

        var (_, actions) = WorkAreaService.BuildPlan(live, desired, allowDeletes: true);

        var deletes = actions.Where(a => a.Kind == WorkAreaActionKind.Delete).Select(a => a.Path).ToList();
        deletes.ShouldBe(new[] { "A/X/Y", "A/X" });
        // The single desired folder is processed (update) before any delete.
        actions.First().Kind.ShouldBe(WorkAreaActionKind.Update);
    }

    // ---- Reconcile Update now carries live state and applies reorder / syndication idempotently ----

    [Fact]
    public void Update_carries_live_index_and_syndication_for_diffing()
    {
        var aId = Guid.NewGuid();
        var live = new[] { new IriverWorkAreaFolder { Id = aId, Name = "A", ParentId = null, Index = 5, IsSyndication = true } };
        var desired = new List<WorkAreaService.DesiredFolder>
        {
            new("A", null, "A", Index: 2, IsQuery: false, IsSyndication: false, Query: null),
        };

        var (_, actions) = WorkAreaService.BuildPlan(live, desired, allowDeletes: false);

        var a = actions.Single();
        a.Kind.ShouldBe(WorkAreaActionKind.Update);
        a.CurrentIndex.ShouldBe(5);
        a.Index.ShouldBe(2);
        a.CurrentIsSyndication.ShouldBeTrue();
        a.IsSyndication.ShouldBeFalse();
    }

    private static WorkAreaAction Update(string name = "A", int index = 0, bool syndication = false,
        ComplexQuery? query = null, string currentName = "A", int currentIndex = 0,
        bool currentSyndication = false, string? currentQueryJson = null) =>
        new(WorkAreaActionKind.Update, "A", null, name, index, query is not null, syndication, query,
            LiveId: Guid.NewGuid(), CurrentName: currentName,
            CurrentIndex: currentIndex, CurrentIsSyndication: currentSyndication, CurrentQueryJson: currentQueryJson);

    [Fact]
    public void Reorder_emits_only_set_index()
        => WorkAreaReconcileSession.ComputeOps(Update(index: 2, currentIndex: 5))
            .ShouldBe(new[] { WorkAreaOp.SetIndex });

    [Fact]
    public void Syndication_toggle_emits_only_set_syndication()
        => WorkAreaReconcileSession.ComputeOps(Update(syndication: true, currentSyndication: false))
            .ShouldBe(new[] { WorkAreaOp.SetSyndication });

    [Fact]
    public void Rename_emits_only_rename()
        => WorkAreaReconcileSession.ComputeOps(Update(name: "NewName", currentName: "OldName"))
            .ShouldBe(new[] { WorkAreaOp.Rename });

    [Fact]
    public void Converged_update_emits_no_ops()
        => WorkAreaReconcileSession.ComputeOps(Update(index: 3, currentIndex: 3)).ShouldBeEmpty();

    [Fact]
    public void Create_with_query_emits_create_then_set_query()
    {
        var a = new WorkAreaAction(WorkAreaActionKind.Create, "A", null, "A", Index: 0,
            IsQuery: true, IsSyndication: false, Query: new ComplexQuery { EntityTypeId = "Product" },
            LiveId: Guid.Empty, CurrentName: null);
        WorkAreaReconcileSession.ComputeOps(a).ShouldBe(new[] { WorkAreaOp.Create, WorkAreaOp.SetQuery });
    }

    [Fact]
    public void Query_change_is_detected_by_serialized_json_and_is_idempotent()
    {
        var q = new ComplexQuery { EntityTypeId = "Product" };

        // Live query serializes to something different → SetQuery is emitted.
        WorkAreaReconcileSession.ComputeOps(Update(query: q, currentQueryJson: "{\"EntityTypeId\":\"Other\"}"))
            .ShouldContain(WorkAreaOp.SetQuery);

        // Live query already matches the desired query's serialization → no write.
        WorkAreaReconcileSession.ComputeOps(Update(query: q, currentQueryJson: WorkAreaService.SerializeQuery(q)))
            .ShouldBeEmpty();
    }
}
