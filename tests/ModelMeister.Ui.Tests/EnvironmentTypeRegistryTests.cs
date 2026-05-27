using System;
using System.Linq;
using System.Text.Json;
using Avalonia.Media;
using ModelMeister.Ui.Models;
using ModelMeister.Ui.Services;
using ModelMeister.Ui.ViewModels;
using Shouldly;
using Xunit;

namespace ModelMeister.Ui.Tests;

/// <summary>
/// Pins the environment-type registry contract: built-in seeding, the migration identity-map,
/// settings round-trips, single-color brush derivation, and that the protected flag (not the literal
/// "Prod") drives the destructive-operation safety banner.
/// </summary>
public class EnvironmentTypeRegistryTests
{
    private static EnvironmentTypeRegistry New(FakeSettingsStore? store = null, IEnvironmentVault? vault = null)
        => new(store ?? new FakeSettingsStore(), vault);

    [Fact]
    public void Seeds_seven_builtins_on_empty_settings()
    {
        var reg = New();
        reg.All.Count.ShouldBe(7);
        reg.All.Select(t => t.Key).ShouldBe(new[] { "Unspecified", "Dev", "Test", "QA", "UAT", "Stage", "Prod" });
        reg.All.ShouldAllBe(t => t.IsBuiltIn);
        reg.Resolve("Prod").Shorthand.ShouldBe("PROD");
        reg.Resolve("Dev").Shorthand.ShouldBe("DEV");
    }

    [Fact]
    public void Only_prod_is_protected_by_default()
    {
        var reg = New();
        reg.IsProtected("Prod").ShouldBeTrue();
        reg.IsProtected("Stage").ShouldBeFalse(); // red colors, but no banner — preserves legacy behavior
        reg.IsProtected("Test").ShouldBeFalse();
        reg.IsProtected("Dev").ShouldBeFalse();
    }

    [Fact]
    public void Resolve_null_or_unknown_falls_back_to_unspecified()
    {
        var reg = New();
        reg.Resolve(null).Key.ShouldBe(EnvironmentTypeRegistry.UnspecifiedKey);
        reg.Resolve("does-not-exist").Key.ShouldBe(EnvironmentTypeRegistry.UnspecifiedKey);
    }

    [Fact]
    public void Every_legacy_stage_name_maps_to_a_builtin() // the migration identity-map contract
    {
        var reg = New();
        foreach (EnvironmentStage s in Enum.GetValues<EnvironmentStage>())
            reg.Resolve(s.ToString()).Key.ShouldBe(s.ToString());
    }

    [Fact]
    public void Upsert_custom_type_adds_and_persists()
    {
        var store = new FakeSettingsStore();
        var reg = New(store);
        reg.Upsert(new EnvironmentType { Name = "Sandbox", Shorthand = "SBX", ColorHex = "#00AA88" });

        var added = reg.All.Single(t => t.Name == "Sandbox");
        added.IsBuiltIn.ShouldBeFalse();
        added.Key.ShouldNotBeNullOrEmpty();
        store.Current.EnvironmentTypes.ShouldContain(t => t.Name == "Sandbox");
    }

    [Fact]
    public void Editing_builtin_persists_and_roundtrips_via_settings()
    {
        var store = new FakeSettingsStore();
        var reg = New(store);
        reg.Upsert(new EnvironmentType
        {
            Key = "Prod", Name = "Production", Shorthand = "LIVE",
            ColorHex = "#FF0000", IsProtected = true, IsBuiltIn = true, SortOrder = 6,
        });

        // Round-trip the persisted settings through JSON, then rebuild a fresh registry.
        var json = JsonSerializer.Serialize(store.Current);
        var restored = new FakeSettingsStore();
        restored.Current.EnvironmentTypes = JsonSerializer.Deserialize<AppSettings>(json)!.EnvironmentTypes;
        var reg2 = New(restored);

        var prod = reg2.Resolve("Prod");
        prod.Shorthand.ShouldBe("LIVE");
        prod.ColorHex.ShouldBe("#FF0000");
        prod.IsBuiltIn.ShouldBeTrue();
    }

    [Fact]
    public void Custom_delete_succeeds()
    {
        var reg = New();
        reg.Upsert(new EnvironmentType { Key = "sbx", Name = "Sandbox", Shorthand = "SBX", ColorHex = "#00AA88" });
        reg.Delete("sbx");
        reg.All.ShouldNotContain(t => t.Key == "sbx");
    }

    [Fact]
    public void Builtin_delete_succeeds_and_is_tombstoned_across_restart()
    {
        var store = new FakeSettingsStore();
        var reg = New(store);

        reg.Delete("Test");
        reg.All.ShouldNotContain(t => t.Key == "Test");
        store.Current.DeletedBuiltInTypeKeys.ShouldContain("Test");

        // Round-trip the persisted settings through JSON, then rebuild a fresh registry: the deleted
        // built-in must stay gone rather than being re-seeded from Defaults().
        var json = JsonSerializer.Serialize(store.Current);
        var restored = new FakeSettingsStore();
        var settings = JsonSerializer.Deserialize<AppSettings>(json)!;
        restored.Current.EnvironmentTypes = settings.EnvironmentTypes;
        restored.Current.DeletedBuiltInTypeKeys = settings.DeletedBuiltInTypeKeys;
        var reg2 = New(restored);

        reg2.All.ShouldNotContain(t => t.Key == "Test");
        reg2.All.Select(t => t.Key).ShouldBe(new[] { "Unspecified", "Dev", "QA", "UAT", "Stage", "Prod" });
    }

    [Fact]
    public void IsInUse_reflects_vault_assignments()
    {
        var vault = new FakeEnvironmentVault(new EnvironmentEntry { Name = "x", TypeKey = "Dev" });
        var reg = New(new FakeSettingsStore(), vault);
        reg.IsInUse("Dev").ShouldBeTrue();
        reg.IsInUse("Prod").ShouldBeFalse();
    }

    [Fact]
    public void Color_derivation_strong_is_opaque_soft_is_translucent_same_hue()
    {
        var strong = (SolidColorBrush)EnvironmentTypeColors.Strong("#1F6FE8");
        strong.Color.ShouldBe(Color.Parse("#1F6FE8"));

        var soft = (SolidColorBrush)EnvironmentTypeColors.Soft("#1F6FE8");
        soft.Color.A.ShouldBeLessThan((byte)0x40);          // clearly translucent
        soft.Color.R.ShouldBe(strong.Color.R);              // same hue
        soft.Color.G.ShouldBe(strong.Color.G);
        soft.Color.B.ShouldBe(strong.Color.B);
    }

    [Fact]
    public void Invalid_hex_falls_back_to_neutral_gray()
    {
        var brush = (SolidColorBrush)EnvironmentTypeColors.Strong("not-a-color");
        brush.Color.ShouldBe(Color.Parse("#6B7280"));
    }

    [Fact]
    public void Protected_flag_drives_confirm_dialogs_not_the_literal_prod()
    {
        var reg = New();
        reg.Upsert(new EnvironmentType { Name = "Live EU", Shorthand = "EU", ColorHex = "#C0392B", IsProtected = true });
        var customKey = reg.All.Single(t => t.Name == "Live EU").Key;
        EnvironmentTypeRegistry.Current = reg; // the dialog VMs resolve against the process-wide registry

        // Bulk delete: protected built-in + protected custom both fire; non-protected doesn't.
        new ConfirmBulkViewModel("t", "Delete", "x", new[] { "a" }, "env", "Prod", destructive: true).IsProtected.ShouldBeTrue();
        new ConfirmBulkViewModel("t", "Delete", "x", new[] { "a" }, "env", customKey, destructive: true).IsProtected.ShouldBeTrue();
        new ConfirmBulkViewModel("t", "Delete", "x", new[] { "a" }, "env", "Test", destructive: true).IsProtected.ShouldBeFalse();
        // Non-destructive actions never trip the guard, even on a protected type.
        new ConfirmBulkViewModel("t", "Delete", "x", new[] { "a" }, "env", "Prod", destructive: false).IsProtected.ShouldBeFalse();

        // Apply confirmation: same — driven by the flag, including for the custom protected type.
        new ConfirmApplyViewModel("url", 1, "", "Prod").IsProtected.ShouldBeTrue();
        new ConfirmApplyViewModel("url", 1, "", customKey).IsProtected.ShouldBeTrue();
        new ConfirmApplyViewModel("url", 1, "", "Dev").IsProtected.ShouldBeFalse();
    }
}
