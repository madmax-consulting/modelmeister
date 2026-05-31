using System.Collections.Generic;
using ModelMeister.Inriver.WorkAreas;
using ModelMeister.Ui.ViewModels;
using Shouldly;
using Xunit;

namespace ModelMeister.Ui.Tests;

/// <summary>
/// Pins the dependency-free <see cref="WorkAreaNode"/> contract that the work-area tree's clipboard,
/// filter, and expand/collapse features bind to. The command-driven behaviour on
/// <see cref="WorkAreaViewModel"/> itself (clipboard cut=move / copy=deep-copy, filter ancestor
/// visibility, CopyTo/MoveTo cross-scope Shell routing) runs against a live connection through the
/// sealed <c>Shell</c> + Avalonia <c>DialogHost</c>, so it is exercised at the service layer instead
/// (see <c>ModelMeister.Inriver.Tests.WorkAreaCopyTests</c> — deep/shallow/cross-scope clone, owner
/// stamping, "(copy)" de-collision, and the move-to-root guard). These tests pin the node-state
/// primitives those features compose on top of: the row icon/badge derivation and the observable
/// flags (<see cref="WorkAreaNode.IsCut"/>, <see cref="WorkAreaNode.IsVisible"/>,
/// <see cref="WorkAreaNode.IsExpanded"/>, <see cref="WorkAreaNode.IsSelected"/>) that drive the
/// cut-dim, filter hide/show, and expand/collapse visuals.
/// </summary>
public class WorkAreaNodeTests
{
    private static WorkAreaNode Node(
        string name = "Folder", string path = "Folder", bool isQuery = false,
        bool isSyndication = false, string? username = null, string? queryJson = null, int index = 0) =>
        new(new WorkAreaFolderDto
        {
            Id = System.Guid.NewGuid(),
            Name = name,
            Path = path,
            Index = index,
            IsQuery = isQuery,
            IsSyndication = isSyndication,
            QueryJson = queryJson,
            Username = username,
        });

    [Fact]
    public void Plain_folder_icon_toggles_open_closed_with_expansion()
    {
        var node = Node();

        node.IconKey.ShouldBe("IcoFolder");      // collapsed
        node.IsExpanded = true;
        node.IconKey.ShouldBe("IcoFolderOpen");  // expanded
        node.IsExpanded = false;
        node.IconKey.ShouldBe("IcoFolder");
    }

    [Fact]
    public void Query_folder_icon_is_search_and_ignores_expansion()
    {
        var node = Node(isQuery: true);

        node.IconKey.ShouldBe("IcoSearch");
        node.IsExpanded = true;
        node.IconKey.ShouldBe("IcoSearch"); // a query folder keeps its glyph regardless of expansion
        node.Kind.ShouldBe("query");
    }

    [Fact]
    public void Syndication_folder_wins_icon_and_badge_over_query()
    {
        // A syndication folder is also a query folder; the syndication treatment takes precedence.
        var node = Node(isQuery: true, isSyndication: true);

        node.IconKey.ShouldBe("IcoSyndication");
        node.Kind.ShouldBe("syndication");
    }

    [Fact]
    public void Plain_folder_badge_is_folder()
        => Node().Kind.ShouldBe("folder");

    [Fact]
    public void Owner_defaults_to_shared_when_no_username()
    {
        Node(username: null).Owner.ShouldBe("shared");
        Node(username: "alice@example.com").Owner.ShouldBe("alice@example.com");
    }

    [Fact]
    public void IsExpanded_change_raises_property_changed_for_icon_key()
    {
        var node = Node();
        var changed = new List<string?>();
        node.PropertyChanged += (_, e) => changed.Add(e.PropertyName);

        node.IsExpanded = true;

        // The icon is derived from IsExpanded, so the view must be told to re-read IconKey too.
        changed.ShouldContain(nameof(WorkAreaNode.IsExpanded));
        changed.ShouldContain(nameof(WorkAreaNode.IconKey));
    }

    [Fact]
    public void IsCut_is_observable_for_the_clipboard_dim_visual()
    {
        var node = Node();
        var changed = new List<string?>();
        node.PropertyChanged += (_, e) => changed.Add(e.PropertyName);

        node.IsCut.ShouldBeFalse();          // default
        node.IsCut = true;                   // a Cut marks the clipboard root
        changed.ShouldContain(nameof(WorkAreaNode.IsCut));
        node.IsCut = false;                  // cleared on paste / clipboard reset
        node.IsCut.ShouldBeFalse();
    }

    [Fact]
    public void IsVisible_defaults_true_and_is_observable_for_the_filter()
    {
        var node = Node();
        var changed = new List<string?>();
        node.PropertyChanged += (_, e) => changed.Add(e.PropertyName);

        node.IsVisible.ShouldBeTrue();       // rows are shown until a filter hides them
        node.IsVisible = false;              // filtered out
        changed.ShouldContain(nameof(WorkAreaNode.IsVisible));
    }

    [Fact]
    public void IsSelected_is_observable_for_multi_selection_mirroring()
    {
        var node = Node();
        var changed = new List<string?>();
        node.PropertyChanged += (_, e) => changed.Add(e.PropertyName);

        node.IsSelected = true;
        changed.ShouldContain(nameof(WorkAreaNode.IsSelected));
    }

    [Fact]
    public void Node_projects_its_backing_dto_fields()
    {
        var node = Node(name: "Launch 2026", path: "Marketing/Launch 2026", isQuery: true,
            queryJson: "{\"EntityTypeId\":\"Product\"}", index: 3);

        node.Name.ShouldBe("Launch 2026");
        node.Path.ShouldBe("Marketing/Launch 2026");
        node.Index.ShouldBe(3);
        node.IsQuery.ShouldBeTrue();
        node.QueryJson.ShouldBe("{\"EntityTypeId\":\"Product\"}");
        node.Children.ShouldBeEmpty();
        node.Parent.ShouldBeNull();
        node.Depth.ShouldBe(0);
    }
}
