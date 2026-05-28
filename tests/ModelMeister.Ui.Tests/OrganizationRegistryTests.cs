using System.Linq;
using System.Text.Json;
using ModelMeister.Ui.Models;
using ModelMeister.Ui.Services;
using Shouldly;
using Xunit;

namespace ModelMeister.Ui.Tests;

/// <summary>
/// Pins the organization registry contract: the single "Default" built-in seed, the read-through that
/// resolves an environment's unset <see cref="EnvironmentEntry.OrgKey"/> to "Default" (the migration
/// landing spot), settings round-trips, tombstoned built-in deletion, and the in-use check that gates
/// deletion (including treating unassigned environments as belonging to "Default").
/// </summary>
public class OrganizationRegistryTests
{
    private static OrganizationRegistry New(FakeSettingsStore? store = null, IEnvironmentVault? vault = null)
        => new(store ?? new FakeSettingsStore(), vault);

    [Fact]
    public void Seeds_default_on_empty_settings()
    {
        var reg = New();
        reg.All.Count.ShouldBe(1);
        reg.All.Single().Key.ShouldBe(OrganizationRegistry.DefaultKey);
        reg.All.Single().IsBuiltIn.ShouldBeTrue();
    }

    [Fact]
    public void Resolve_null_or_unknown_falls_back_to_default()
    {
        var reg = New();
        reg.Resolve(null).Key.ShouldBe(OrganizationRegistry.DefaultKey);
        reg.Resolve("does-not-exist").Key.ShouldBe(OrganizationRegistry.DefaultKey);
    }

    [Fact]
    public void Upsert_custom_org_adds_and_persists()
    {
        var store = new FakeSettingsStore();
        var reg = New(store);
        reg.Upsert(new Organization { Name = "Acme Corp", Description = "Test customer" });

        var added = reg.All.Single(o => o.Name == "Acme Corp");
        added.IsBuiltIn.ShouldBeFalse();
        added.Key.ShouldNotBeNullOrEmpty();
        store.Current.Organizations.ShouldContain(o => o.Name == "Acme Corp");
    }

    [Fact]
    public void Editing_builtin_persists_and_roundtrips_via_settings()
    {
        var store = new FakeSettingsStore();
        var reg = New(store);
        reg.Upsert(new Organization
        {
            Key = OrganizationRegistry.DefaultKey, Name = "House", Description = "Renamed default",
            IsBuiltIn = true, SortOrder = 0,
        });

        // Round-trip the persisted settings through JSON, then rebuild a fresh registry.
        var json = JsonSerializer.Serialize(store.Current);
        var restored = new FakeSettingsStore();
        restored.Current.Organizations = JsonSerializer.Deserialize<AppSettings>(json)!.Organizations;
        var reg2 = New(restored);

        var def = reg2.Resolve(OrganizationRegistry.DefaultKey);
        def.Name.ShouldBe("House");
        def.IsBuiltIn.ShouldBeTrue();
    }

    [Fact]
    public void Custom_delete_succeeds()
    {
        var reg = New();
        reg.Upsert(new Organization { Key = "acme", Name = "Acme" });
        reg.Delete("acme");
        reg.All.ShouldNotContain(o => o.Key == "acme");
    }

    [Fact]
    public void Builtin_delete_succeeds_and_is_tombstoned_across_restart()
    {
        var store = new FakeSettingsStore();
        var reg = New(store);

        reg.Delete(OrganizationRegistry.DefaultKey);
        reg.All.ShouldNotContain(o => o.Key == OrganizationRegistry.DefaultKey);
        store.Current.DeletedBuiltInOrgKeys.ShouldContain(OrganizationRegistry.DefaultKey);

        // Round-trip the persisted settings through JSON, then rebuild a fresh registry: the deleted
        // built-in must stay gone rather than being re-seeded from Defaults().
        var json = JsonSerializer.Serialize(store.Current);
        var restored = new FakeSettingsStore();
        var settings = JsonSerializer.Deserialize<AppSettings>(json)!;
        restored.Current.Organizations = settings.Organizations;
        restored.Current.DeletedBuiltInOrgKeys = settings.DeletedBuiltInOrgKeys;
        var reg2 = New(restored);

        reg2.All.ShouldNotContain(o => o.Key == OrganizationRegistry.DefaultKey);
    }

    [Fact]
    public void IsInUse_true_for_an_assigned_org()
    {
        var vault = new FakeEnvironmentVault(new EnvironmentEntry { Name = "x", OrgKey = "acme" });
        var reg = New(new FakeSettingsStore(), vault);
        reg.Upsert(new Organization { Key = "acme", Name = "Acme" });
        reg.IsInUse("acme").ShouldBeTrue();
        reg.IsInUse("globex").ShouldBeFalse();
    }

    [Fact]
    public void Unassigned_environment_counts_as_using_default()
    {
        // An environment with no OrgKey resolves to "Default" — so Default is reported in-use and can't
        // be deleted out from under it.
        var vault = new FakeEnvironmentVault(new EnvironmentEntry { Name = "legacy", OrgKey = null });
        var reg = New(new FakeSettingsStore(), vault);
        reg.IsInUse(OrganizationRegistry.DefaultKey).ShouldBeTrue();
    }

    [Fact]
    public void EnvironmentsInScope_predicate_isolates_by_org_with_default_readthrough()
    {
        // Mirrors MainWindowViewModel.EnvironmentsInScope: filter by Resolve(OrgKey).Key == selected.Key.
        var reg = New();
        reg.Upsert(new Organization { Key = "acme", Name = "Acme" });
        reg.Upsert(new Organization { Key = "globex", Name = "Globex" });

        var acme = new EnvironmentEntry { Name = "acme-prod", OrgKey = "acme" };
        var globex = new EnvironmentEntry { Name = "globex-prod", OrgKey = "globex" };
        var legacy = new EnvironmentEntry { Name = "legacy", OrgKey = null };
        var all = new[] { acme, globex, legacy };

        string ScopeKey(EnvironmentEntry e) => reg.Resolve(e.OrgKey).Key;

        all.Where(e => ScopeKey(e) == "acme").ShouldBe(new[] { acme });
        all.Where(e => ScopeKey(e) == "globex").ShouldBe(new[] { globex });
        // The legacy (unassigned) environment is only visible under Default.
        all.Where(e => ScopeKey(e) == OrganizationRegistry.DefaultKey).ShouldBe(new[] { legacy });
    }
}
