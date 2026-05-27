using System;
using System.Collections.Generic;
using System.Linq;
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
}
