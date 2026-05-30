using System.Collections.Generic;
using ModelMeister.Inriver.Diff;
using ModelMeister.Ui.ViewModels;
using Shouldly;
using Xunit;

namespace ModelMeister.Ui.Tests;

/// <summary>
/// Pins the apply-confirmation styling contract: the dialog escalates to the red "DESTRUCTIVE ACTION"
/// headline only when the batch actually contains a destructive change (delete / datatype) — a batch of
/// pure additions reads as a calm "APPLY CHANGES" so the warning stays meaningful.
/// </summary>
public class ConfirmApplyViewModelTests
{
    private static ApplyReviewItem Add(string desc) => new("Add", desc, IsDangerous: false);
    private static ApplyReviewItem Delete(string desc) => new("Delete", desc, IsDangerous: true);

    [Fact]
    public void Additive_batch_is_not_destructive()
    {
        var vm = new ConfirmApplyViewModel("https://env", 2, changes: new List<ApplyReviewItem>
        {
            Add("+ FieldType A"), Add("+ FieldType B"),
        });

        vm.IsDestructive.ShouldBeFalse();
        vm.UseDangerAccent.ShouldBeFalse();
        vm.Headline.ShouldBe("APPLY CHANGES");
        vm.HasChanges.ShouldBeTrue();
    }

    [Fact]
    public void Batch_with_a_delete_is_destructive()
    {
        var vm = new ConfirmApplyViewModel("https://env", 2, changes: new List<ApplyReviewItem>
        {
            Add("+ FieldType A"), Delete("- FieldType B"),
        });

        vm.IsDestructive.ShouldBeTrue();
        vm.UseDangerAccent.ShouldBeTrue();
        vm.Headline.ShouldBe("DESTRUCTIVE ACTION");
    }

    [Fact]
    public void Empty_change_list_has_no_rows()
    {
        var vm = new ConfirmApplyViewModel("https://env", 0);
        vm.HasChanges.ShouldBeFalse();
        vm.Changes.ShouldBeEmpty();
    }

    [Fact]
    public void Blast_radius_surfaces_as_data_at_risk()
    {
        var risk = new List<BlastRadiusEntry>
        {
            new(BlastRadiusKind.EntityTypeDelete, "Product", "Product", 48231, "Product"),
            new(BlastRadiusKind.FieldDelete, "Sku", "Sku", 1200, "SkuWeight"),
        };
        var vm = new ConfirmApplyViewModel("https://env", 2,
            changes: new List<ApplyReviewItem> { Delete("- EntityType Product") },
            blastRadius: risk);

        vm.HasDataAtRisk.ShouldBeTrue();
        vm.UseDangerAccent.ShouldBeTrue();
        vm.DataAtRisk.Count.ShouldBe(2);
        vm.InstancesAtRisk.ShouldBe(49431);
        vm.DataAtRiskHeadline.ShouldContain("49,431");
    }

    [Fact]
    public void No_blast_radius_means_no_data_at_risk_card()
    {
        var vm = new ConfirmApplyViewModel("https://env", 1, changes: new List<ApplyReviewItem> { Add("+ FieldType A") });
        vm.HasDataAtRisk.ShouldBeFalse();
        vm.DataAtRisk.ShouldBeEmpty();
        vm.InstancesAtRisk.ShouldBe(0);
    }
}
