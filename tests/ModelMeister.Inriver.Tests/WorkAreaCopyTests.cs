using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;
using inRiver.Remoting.Query;
using ModelMeister.Inriver.WorkAreas;
using Shouldly;
using Xunit;
using IriverWorkAreaFolder = inRiver.Remoting.Objects.WorkAreaFolder;
using RemoteManager = inRiver.Remoting.RemoteManager;

namespace ModelMeister.Inriver.Tests;

/// <summary>
/// Pins the copy/duplicate/cross-scope primitives <see cref="WorkAreaService"/> grew for "work-area
/// supremacy". Two layers are exercised:
/// <list type="bullet">
/// <item>The pure planners (<c>FlattenSubtree</c>, <c>DefaultCopyName</c>) — no inriver at all.</item>
/// <item>The through-the-service clone (<c>CopyFolderAsync</c>/<c>CopySubtreeAsync</c>/<c>CopyToServiceAsync</c>)
/// driven against a <see cref="FakeWorkAreaScope"/> in-memory store with a per-op call counter, so the
/// remoting surface is never touched. The service still pumps every scope call through
/// <see cref="InriverClient.Read{T}"/>/<see cref="InriverClient.WriteAsync{T}"/>, which require a non-null
/// <c>RemoteManager</c>; we inject an uninitialised stub (the fake scope ignores the manager arg).</item>
/// </list>
/// </summary>
public class WorkAreaCopyTests
{
    private static IriverWorkAreaFolder Folder(
        Guid id, string name, Guid? parent, int index = 0, bool isQuery = false,
        bool isSyndication = false, ComplexQuery? query = null) =>
        new() { Id = id, Name = name, ParentId = parent, Index = index, IsQuery = isQuery, IsSyndication = isSyndication, Query = query };

    private static ComplexQuery MakeQuery(string entityType, string field) =>
        new()
        {
            EntityTypeId = entityType,
            DataQuery = new Query
            {
                Join = Join.And,
                Criteria = new List<Criteria> { new() { FieldTypeId = field, Operator = Operator.Contains, Value = "x" } },
            },
        };

    // ---------------- Pure planner: FlattenSubtree ----------------

    [Fact]
    public void FlattenSubtree_yields_parents_before_children_with_preserved_index_and_depth()
    {
        var root = Guid.NewGuid();
        var childB = Guid.NewGuid();   // index 1
        var childA = Guid.NewGuid();   // index 0
        var grandkid = Guid.NewGuid(); // under childA
        var unrelated = Guid.NewGuid();
        var live = new[]
        {
            Folder(grandkid, "Grand", childA, index: 0),
            Folder(childB, "B", root, index: 1),
            Folder(root, "Root", null, index: 7),
            Folder(childA, "A", root, index: 0),
            Folder(unrelated, "Other", null, index: 0), // not part of the subtree
        };

        var nodes = WorkAreaService.FlattenSubtree(live, root);

        // Root first (depth 0), then its children ordered by Index (A before B), then the grandchild (depth 2).
        nodes.Select(n => n.Name).ShouldBe(new[] { "Root", "A", "B", "Grand" });
        nodes[0].Depth.ShouldBe(0);
        nodes[0].Index.ShouldBe(7);          // the root keeps its own live Index
        nodes[1].Depth.ShouldBe(1);
        nodes[1].Index.ShouldBe(0);          // A
        nodes[2].Depth.ShouldBe(1);
        nodes[2].Index.ShouldBe(1);          // B
        nodes[3].Depth.ShouldBe(2);          // grandchild
        nodes.ShouldNotContain(n => n.Name == "Other");

        // Every non-root node appears AFTER its parent (parents-before-children invariant).
        for (var i = 0; i < nodes.Count; i++)
            if (nodes[i].SourceParentId is { } pid && nodes.Any(n => n.SourceId == pid))
                nodes.Take(i).ShouldContain(n => n.SourceId == pid);
    }

    [Fact]
    public void FlattenSubtree_returns_empty_when_root_absent()
        => WorkAreaService.FlattenSubtree(Array.Empty<IriverWorkAreaFolder>(), Guid.NewGuid()).ShouldBeEmpty();

    [Fact]
    public void FlattenSubtree_terminates_on_self_parent()
    {
        // A folder that is its own parent (s.Parent == s) appears in its own children list, so a naive BFS
        // would re-enqueue it forever and emit up to ~100k duplicate CopyNodes — one bogus folder-create write
        // each. The visited guard must bound the walk and keep the output distinct. The self-parented folder is
        // NOT a child of the root (its only parent is itself), so the reachable set is just {root, ok}.
        var root = Guid.NewGuid();
        var self = Guid.NewGuid();
        var ok = Guid.NewGuid();
        var live = new[]
        {
            Folder(root, "Root", null, index: 0),
            Folder(self, "Self", self, index: 0),  // self.Parent == self
            Folder(ok, "Ok", root, index: 1),
        };

        var flat = WorkAreaService.FlattenSubtree(live, root);

        flat.Count.ShouldBeLessThan(100);
        flat.Select(n => n.SourceId).Distinct().Count().ShouldBe(flat.Count);
        flat[0].SourceId.ShouldBe(root);
        flat.ShouldNotContain(n => n.SourceId == self);
        flat.ShouldContain(n => n.SourceId == ok);
    }

    [Fact]
    public void FlattenSubtree_terminates_on_self_parented_root()
    {
        // The root itself is self-parented (root.Parent == root): it appears in its own children list, so a
        // naive BFS re-enqueues it forever. The guard must emit the root exactly once, with its child.
        var root = Guid.NewGuid();
        var child = Guid.NewGuid();
        var live = new[]
        {
            Folder(root, "Root", root, index: 0),    // root.Parent == root
            Folder(child, "Child", root, index: 0),
        };

        var flat = WorkAreaService.FlattenSubtree(live, root);

        flat.Count.ShouldBe(2);
        flat.Select(n => n.SourceId).Distinct().Count().ShouldBe(flat.Count);
        flat[0].SourceId.ShouldBe(root);
        flat.ShouldContain(n => n.SourceId == child);
    }

    // ---------------- Pure planner: DefaultCopyName ----------------

    [Fact]
    public void DefaultCopyName_first_copy_is_plain_copy_suffix()
        => WorkAreaService.DefaultCopyName("X", Array.Empty<string>()).ShouldBe("X (copy)");

    [Fact]
    public void DefaultCopyName_skips_taken_names_and_numbers_the_next()
    {
        WorkAreaService.DefaultCopyName("X", new[] { "X (copy)" }).ShouldBe("X (copy 2)");
        WorkAreaService.DefaultCopyName("X", new[] { "X (copy)", "X (copy 2)" }).ShouldBe("X (copy 3)");
        // Case-insensitive collision detection.
        WorkAreaService.DefaultCopyName("X", new[] { "x (COPY)" }).ShouldBe("X (copy 2)");
    }

    // ---------------- Through-the-service: deep copy ----------------

    [Fact]
    public async Task Deep_copy_clones_subtree_with_fresh_ids_preserving_order_and_query()
    {
        var root = Guid.NewGuid();
        var childA = Guid.NewGuid();
        var childB = Guid.NewGuid();
        var grand = Guid.NewGuid();
        var savedQuery = MakeQuery("Product", "ProductName");

        var store = new FakeWorkAreaScope(syndication: true);
        store.Seed(Folder(root, "Marketing", null, index: 0));
        store.Seed(Folder(childA, "Campaigns", root, index: 0, isSyndication: true));
        store.Seed(Folder(childB, "Assets", root, index: 1, isQuery: true, query: savedQuery));
        store.Seed(Folder(grand, "Launch", childA, index: 0));

        var svc = NewService(store);
        var newRoot = await svc.CopySubtreeAsync(root, newParentId: null, newIndex: 5);

        // A brand-new root id was minted; the source is untouched and the store grew by 4.
        newRoot.ShouldNotBe(root);
        newRoot.ShouldNotBe(Guid.Empty);
        store.Folders.Count.ShouldBe(8);

        // The four clones share NO ids with the originals.
        var originals = new HashSet<Guid> { root, childA, childB, grand };
        var clones = store.Folders.Values.Where(f => !originals.Contains(f.Id)).ToList();
        clones.Count.ShouldBe(4);
        clones.ShouldNotContain(f => originals.Contains(f.Id));

        var newRootFolder = store.Folders[newRoot];
        // The clone lands at the root (destParentId null), which IS the source root's own parent in this same
        // service, so the "(copy)" de-collision fires on the root (only the root is ever renamed).
        newRootFolder.Name.ShouldBe("Marketing (copy)");
        newRootFolder.ParentId.ShouldBeNull();
        newRootFolder.Index.ShouldBe(5);                   // destination index applied to the root

        // Children of the clone, by their preserved relative index.
        var newChildren = store.Folders.Values.Where(f => f.ParentId == newRoot).OrderBy(f => f.Index).ToList();
        newChildren.Select(f => f.Name).ShouldBe(new[] { "Campaigns", "Assets" });
        newChildren[0].Index.ShouldBe(0);
        newChildren[1].Index.ShouldBe(1);
        newChildren[0].IsSyndication.ShouldBeTrue();       // flags preserved
        newChildren[1].IsQuery.ShouldBeTrue();

        // The saved query was copied faithfully (serialised JSON equality).
        var clonedAssets = newChildren[1];
        WorkAreaService.SerializeQuery(clonedAssets.Query).ShouldBe(WorkAreaService.SerializeQuery(savedQuery));

        // The grandchild was recreated under the cloned "Campaigns".
        var clonedCampaigns = newChildren[0];
        var clonedGrand = store.Folders.Values.SingleOrDefault(f => f.ParentId == clonedCampaigns.Id);
        clonedGrand.ShouldNotBeNull();
        clonedGrand!.Name.ShouldBe("Launch");
    }

    [Fact]
    public async Task Deep_copy_into_source_parent_renames_only_the_root()
    {
        var root = Guid.NewGuid();
        var child = Guid.NewGuid();
        var store = new FakeWorkAreaScope(syndication: true);
        store.Seed(Folder(root, "Specs", null, index: 0));
        store.Seed(Folder(child, "Specs", root, index: 0)); // child shares the name "Specs" — must NOT be renamed

        var svc = NewService(store);
        var newRoot = await svc.CopySubtreeAsync(root, newParentId: null, newIndex: 1);

        // Root lands among the source's own siblings (both at root) → de-collided with "(copy)".
        store.Folders[newRoot].Name.ShouldBe("Specs (copy)");
        // The descendant keeps its original name (its path already differs via the new root segment).
        var clonedChild = store.Folders.Values.Single(f => f.ParentId == newRoot);
        clonedChild.Name.ShouldBe("Specs");
    }

    // ---------------- Through-the-service: shallow copy ----------------

    [Fact]
    public async Task Shallow_copy_clones_one_folder_without_children()
    {
        var root = Guid.NewGuid();
        var child = Guid.NewGuid();
        var store = new FakeWorkAreaScope(syndication: true);
        store.Seed(Folder(root, "Marketing", null, index: 0));
        store.Seed(Folder(child, "Campaigns", root, index: 0));

        var svc = NewService(store);
        var newId = await svc.CopyFolderAsync(root, newParentId: null, newIndex: 9);

        newId.ShouldNotBe(root);
        store.AddCount.ShouldBe(1);                         // exactly one Add — no children walked
        store.Folders.Count.ShouldBe(3);                   // 2 seeded + 1 shallow clone
        store.Folders[newId].Name.ShouldBe("Marketing (copy)");
        store.Folders[newId].Index.ShouldBe(9);
        store.Folders.Values.ShouldNotContain(f => f.ParentId == newId); // no descendants cloned
    }

    // ---------------- Cross-scope copy ----------------

    [Fact]
    public async Task Cross_scope_copy_writes_into_destination_and_stamps_destination_owner()
    {
        // Source: shared scope (owner null, syndication supported). A query folder with syndication on.
        var root = Guid.NewGuid();
        var child = Guid.NewGuid();
        var savedQuery = MakeQuery("Item", "Name");
        var shared = new FakeWorkAreaScope(syndication: true, owner: null);
        shared.Seed(Folder(root, "Shared Root", null, index: 0, isSyndication: true));
        shared.Seed(Folder(child, "Shared Child", root, index: 0, isQuery: true, query: savedQuery));

        // Destination: a user's PERSONAL scope (owner set, no syndication support).
        const string user = "alice@example.com";
        var personal = new FakeWorkAreaScope(syndication: false, owner: user);

        var src = NewService(shared);
        var dst = NewService(personal);

        var newRoot = await src.CopyToServiceAsync(root, dst, destParentId: null, destIndex: 0, deep: true);

        // Nothing was written back into the source scope; both clones landed in the destination store.
        shared.Folders.Count.ShouldBe(2);
        personal.Folders.Count.ShouldBe(2);

        // The destination scope stamped its owner on every created folder (PersonalWorkAreaScope.Add semantics).
        personal.Folders.Values.ShouldAllBe(f => f.Username == user);

        // Syndication is unsupported on personal → SetSyndication never invoked (guarded by SupportsSyndication).
        personal.SetSyndicationCount.ShouldBe(0);

        // The query rode across faithfully.
        var clonedChild = personal.Folders.Values.Single(f => f.ParentId == newRoot);
        clonedChild.IsQuery.ShouldBeTrue();
        WorkAreaService.SerializeQuery(clonedChild.Query).ShouldBe(WorkAreaService.SerializeQuery(savedQuery));

        // Cross-service copy keeps the original name (no same-parent collision in the destination).
        personal.Folders[newRoot].Name.ShouldBe("Shared Root");
    }

    // ---------------- Query hydration (list endpoint can omit a folder's ComplexQuery) ----------------

    [Fact]
    public void List_hydrates_query_when_list_endpoint_omits_it()
    {
        // inriver's GetAll*WorkAreaFolders can return query folders WITHOUT their ComplexQuery populated.
        // The service must back-fill from the per-folder GetOne so the saved search isn't lost everywhere
        // downstream (detail pane, builder, copy, promote, Excel, backup).
        var plain = Guid.NewGuid();
        var queryFolder = Guid.NewGuid();
        var store = new FakeWorkAreaScope(syndication: true) { ListOmitsQuery = true };
        store.Seed(Folder(plain, "Plain", null, index: 0));
        store.Seed(Folder(queryFolder, "Saved search", null, index: 1, isQuery: true, query: MakeQuery("Product", "Name")));

        var dtos = NewService(store).List();

        dtos.Single(d => d.Id == queryFolder).QueryJson.ShouldNotBeNullOrEmpty();
        // Only the query folder that came back without a query triggers the per-folder read; the plain folder doesn't.
        store.GetOneCalls.ShouldBe(1);
    }

    [Fact]
    public void List_does_not_hydrate_when_query_already_present()
    {
        var store = new FakeWorkAreaScope(syndication: true); // GetAll already carries the query
        store.Seed(Folder(Guid.NewGuid(), "Saved search", null, index: 0, isQuery: true, query: MakeQuery("Item", "Name")));

        _ = NewService(store).List();

        store.GetOneCalls.ShouldBe(0);
    }

    // ---------------- Move-to-root guard ----------------

    [Fact]
    public async Task MoveFolderAsync_to_null_parent_throws_not_supported()
    {
        var store = new FakeWorkAreaScope(syndication: true);
        var id = Guid.NewGuid();
        store.Seed(Folder(id, "A", null, index: 0));
        var svc = NewService(store);

        await Should.ThrowAsync<NotSupportedException>(() => svc.MoveFolderAsync(id, newParentId: null, newIndex: 0));
        store.MoveCount.ShouldBe(0);
    }

    // ---------------- Test plumbing ----------------

    /// <summary>
    /// Build a <see cref="WorkAreaService"/> over the in-memory <paramref name="scope"/>. The service routes
    /// scope calls through a real <see cref="InriverClient"/> whose <c>RemoteManager</c> is an uninitialised
    /// stub (never dereferenced by the fake scope).
    /// </summary>
    private static WorkAreaService NewService(FakeWorkAreaScope scope) =>
        (WorkAreaService)Activator.CreateInstance(
            typeof(WorkAreaService),
            System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Public,
            binder: null,
            args: new object?[] { StubClient(), scope, null },
            culture: null)!;

    /// <summary>A real <see cref="InriverClient"/> with a stub <c>RemoteManager</c> jammed into its private
    /// field, so <c>Read</c>/<c>WriteAsync</c> have a non-null manager to hand the (manager-ignoring) fake scope.</summary>
    private static InriverClient StubClient()
    {
        var client = new InriverClient("http://localhost/test");
        var manager = (RemoteManager)RuntimeHelpers.GetUninitializedObject(typeof(RemoteManager));
        var field = typeof(InriverClient).GetField("_manager",
            System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!;
        field.SetValue(client, manager);
        return client;
    }

    /// <summary>
    /// In-memory <see cref="IWorkAreaScope"/>: a folder store keyed by id with per-operation call counters.
    /// Mirrors the two real scopes' contract — <see cref="OwnerUsername"/>/<see cref="SupportsSyndication"/>
    /// configurable; <see cref="Add"/> stamps the owner like <c>PersonalWorkAreaScope</c>. Ignores the
    /// <see cref="RemoteManager"/> argument entirely (it is never a real connection here).
    /// </summary>
    private sealed class FakeWorkAreaScope : IWorkAreaScope
    {
        private readonly bool _syndication;

        public FakeWorkAreaScope(bool syndication, string? owner = null)
        {
            _syndication = syndication;
            OwnerUsername = owner;
        }

        public Dictionary<Guid, IriverWorkAreaFolder> Folders { get; } = new();

        public int AddCount { get; private set; }
        public int MoveCount { get; private set; }
        public int SetSyndicationCount { get; private set; }
        public int SetQueryCount { get; private set; }
        public int GetOneCalls { get; private set; }

        /// <summary>Simulate inriver's list endpoint returning query folders without their <c>ComplexQuery</c>:
        /// when set, <see cref="GetAll"/> returns clones with a null Query while <see cref="GetOne"/> hydrates.</summary>
        public bool ListOmitsQuery { get; init; }

        public string? OwnerUsername { get; }
        public bool SupportsSyndication => _syndication;

        /// <summary>Pre-populate the store without bumping any write counter.</summary>
        public void Seed(IriverWorkAreaFolder folder) => Folders[folder.Id] = folder;

        public IReadOnlyList<IriverWorkAreaFolder> GetAll(RemoteManager m) =>
            ListOmitsQuery
                ? Folders.Values.Select(f => new IriverWorkAreaFolder
                {
                    Id = f.Id, Name = f.Name, ParentId = f.ParentId, Index = f.Index,
                    IsQuery = f.IsQuery, IsSyndication = f.IsSyndication, Username = f.Username, Query = null,
                }).ToList()
                : Folders.Values.ToList();

        public IriverWorkAreaFolder? GetOne(RemoteManager m, Guid id)
        {
            GetOneCalls++;
            return Folders.TryGetValue(id, out var f) ? f : null;
        }

        public IriverWorkAreaFolder Add(RemoteManager m, IriverWorkAreaFolder folder)
        {
            AddCount++;
            if (folder.Id == Guid.Empty) folder.Id = Guid.NewGuid();
            if (OwnerUsername is not null) folder.Username = OwnerUsername; // personal scopes stamp the owner
            Folders[folder.Id] = folder;
            return folder;
        }

        public IriverWorkAreaFolder Rename(RemoteManager m, Guid id, string name)
        {
            Folders[id].Name = name;
            return Folders[id];
        }

        public IriverWorkAreaFolder Move(RemoteManager m, Guid id, Guid newParentId, int newIndex)
        {
            MoveCount++;
            Folders[id].ParentId = newParentId;
            Folders[id].Index = newIndex;
            return Folders[id];
        }

        public IriverWorkAreaFolder SetIndex(RemoteManager m, Guid id, int newIndex)
        {
            Folders[id].Index = newIndex;
            return Folders[id];
        }

        public IriverWorkAreaFolder SetSyndication(RemoteManager m, Guid id, bool isSyndication)
        {
            SetSyndicationCount++;
            Folders[id].IsSyndication = isSyndication;
            return Folders[id];
        }

        public IriverWorkAreaFolder SetQuery(RemoteManager m, Guid id, ComplexQuery query)
        {
            SetQueryCount++;
            Folders[id].Query = query;
            Folders[id].IsQuery = true;
            return Folders[id];
        }

        public bool Delete(RemoteManager m, Guid id) => Folders.Remove(id);
    }
}
