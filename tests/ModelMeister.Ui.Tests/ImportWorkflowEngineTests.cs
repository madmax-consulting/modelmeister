using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using ModelMeister.Ui.Services.Import;
using ModelMeister.Ui.ViewModels;
using Shouldly;
using Xunit;

namespace ModelMeister.Ui.Tests;

/// <summary>
/// Pins the shared Excel-import workflow engine (<see cref="ImportWorkflowViewModel"/>): abort-on-first-
/// error, mid-run cancel, count integrity, verify categorisation, backup-or-abort, and the destructive
/// removal gate. Drives the VM directly with a <see cref="FakeImportPlan"/> — no window required.
/// </summary>
public class ImportWorkflowEngineTests
{
    private static ImportRowViewModel Row(string key, RowPlanKind kind) =>
        new() { Key = key, Preview = key, PlanKind = kind, Payload = key };

    private static (ImportWorkflowViewModel vm, FakeImportPlan plan, FakeConfirmGate gate) Build(
        IEnumerable<ImportRowViewModel> rows)
    {
        var plan = new FakeImportPlan { Rows = rows.ToList() };
        var gate = new FakeConfirmGate();
        var vm = new ImportWorkflowViewModel(plan, new NullAppLog(), new NullFileOpener(), gate);
        return (vm, plan, gate);
    }

    [Fact]
    public async Task AbortOnFirstError_stops_after_the_first_failed_row()
    {
        var rows = Enumerable.Range(0, 5).Select(i => Row($"k{i}", RowPlanKind.WillCreate)).ToList();
        var (vm, plan, _) = Build(rows);
        plan.OutcomeFor = r => r.Key == "k2"
            ? new RowOutcome(RowRunState.Failed, "bad", "boom")
            : new RowOutcome(RowRunState.Created, "ok");
        vm.AbortOnFirstError = true;

        await vm.VerifyCommand.ExecuteAsync(null);
        await vm.StartImportCommand.ExecuteAsync(null);

        plan.AppliedKeys.ShouldBe(new[] { "k0", "k1", "k2" });   // stopped right after the failure
        vm.Failed.ShouldBe(1);
        rows[3].State.ShouldBe(RowRunState.Pending);
        rows[4].State.ShouldBe(RowRunState.Pending);
        vm.CurrentStep.ShouldBe(ImportStep.Results);
        vm.StatusMessage.ShouldContain("Aborted");
    }

    [Fact]
    public async Task AbortOff_processes_every_row_and_collects_errors()
    {
        var rows = Enumerable.Range(0, 5).Select(i => Row($"k{i}", RowPlanKind.WillCreate)).ToList();
        var (vm, plan, _) = Build(rows);
        plan.OutcomeFor = r => r.Key == "k2"
            ? new RowOutcome(RowRunState.Failed, "bad", "boom")
            : new RowOutcome(RowRunState.Created, "ok");

        await vm.VerifyCommand.ExecuteAsync(null);
        await vm.StartImportCommand.ExecuteAsync(null);

        plan.AppliedKeys.Count.ShouldBe(5);
        vm.Failed.ShouldBe(1);
        vm.Created.ShouldBe(4);
    }

    [Fact]
    public async Task Cancel_midrun_stops_and_leaves_remaining_rows_pending()
    {
        var rows = Enumerable.Range(0, 5).Select(i => Row($"k{i}", RowPlanKind.WillCreate)).ToList();
        var (vm, plan, _) = Build(rows);
        plan.AfterRowApplied = applied => { if (applied == 2) vm.CancelCommand.Execute(null); };

        await vm.VerifyCommand.ExecuteAsync(null);
        await vm.StartImportCommand.ExecuteAsync(null);

        plan.AppliedKeys.Count.ShouldBe(2);          // row 3's iteration observes cancellation
        vm.StatusMessage.ShouldContain("Cancelled");
        rows[2].State.ShouldBe(RowRunState.Pending);
        rows[4].State.ShouldBe(RowRunState.Pending);
        vm.CurrentStep.ShouldBe(ImportStep.Results);
    }

    [Fact]
    public async Task Counts_add_up_and_skipped_or_invalid_rows_are_never_applied()
    {
        var rows = new List<ImportRowViewModel>
        {
            Row("c0", RowPlanKind.WillCreate),
            Row("c1", RowPlanKind.WillCreate),
            Row("u0", RowPlanKind.WillUpdate),
            Row("s0", RowPlanKind.WillSkip),
            Row("x0", RowPlanKind.Invalid),
            Row("f0", RowPlanKind.WillCreate),
        };
        var (vm, plan, _) = Build(rows);
        plan.OutcomeFor = r => r.Key switch
        {
            "u0" => new RowOutcome(RowRunState.Updated, "upd"),
            "f0" => new RowOutcome(RowRunState.Failed, "bad", "boom"),
            _    => new RowOutcome(RowRunState.Created, "ok"),
        };

        await vm.VerifyCommand.ExecuteAsync(null);
        await vm.StartImportCommand.ExecuteAsync(null);

        vm.Total.ShouldBe(4);                                            // 3 create + 1 update applicable
        vm.Completed.ShouldBe(vm.Created + vm.Updated + vm.Skipped + vm.Failed);
        vm.Completed.ShouldBe(plan.AppliedKeys.Count);
        plan.AppliedKeys.ShouldNotContain("s0");
        plan.AppliedKeys.ShouldNotContain("x0");
        vm.Created.ShouldBe(2);
        vm.Updated.ShouldBe(1);
        vm.Failed.ShouldBe(1);
    }

    [Fact]
    public async Task Verify_categorises_rows_and_computes_applicable_count()
    {
        var rows = new List<ImportRowViewModel>
        {
            Row("c0", RowPlanKind.WillCreate),
            Row("u0", RowPlanKind.WillUpdate),
            Row("u1", RowPlanKind.WillUpdate),
            Row("s0", RowPlanKind.WillSkip),
            Row("x0", RowPlanKind.Invalid),
        };
        var (vm, _, _) = Build(rows);

        await vm.VerifyCommand.ExecuteAsync(null);

        vm.VerifyCreate.ShouldBe(1);
        vm.VerifyUpdate.ShouldBe(2);
        vm.VerifySkip.ShouldBe(1);
        vm.VerifyInvalid.ShouldBe(1);
        vm.Applicable.ShouldBe(3);
        vm.CurrentStep.ShouldBe(ImportStep.Verify);
    }

    [Fact]
    public async Task Backup_failure_aborts_before_any_row_is_applied()
    {
        var rows = Enumerable.Range(0, 3).Select(i => Row($"k{i}", RowPlanKind.WillCreate)).ToList();
        var (vm, plan, _) = Build(rows);
        plan.BackupThrows = new System.InvalidOperationException("backup boom");

        await vm.VerifyCommand.ExecuteAsync(null);
        await vm.StartImportCommand.ExecuteAsync(null);

        plan.AppliedKeys.ShouldBeEmpty();
        vm.StatusMessage.ShouldContain("Backup failed");
        vm.CurrentStep.ShouldBe(ImportStep.Results);
    }

    [Fact]
    public async Task RemovalGate_declined_returns_to_Verify_without_applying()
    {
        var rows = new List<ImportRowViewModel> { Row("u0", RowPlanKind.WillUpdate) };
        var plan = new FakeImportPlan
        {
            Rows = rows,
            VerifyOverride = new VerifyResult(rows, 0, 1, 0, 0,
                DestructiveConfirmTitle: "Apply", DestructiveVerb: "Remove", DestructiveNoun: "value",
                DestructiveItems: new[] { "CvlA (2 value(s))" }),
        };
        var gate = new FakeConfirmGate { Answer = false };
        var vm = new ImportWorkflowViewModel(plan, new NullAppLog(), new NullFileOpener(), gate);

        await vm.VerifyCommand.ExecuteAsync(null);
        await vm.StartImportCommand.ExecuteAsync(null);

        gate.Calls.ShouldBe(1);
        plan.AppliedKeys.ShouldBeEmpty();
        vm.CurrentStep.ShouldBe(ImportStep.Verify);
        vm.StatusMessage.ShouldContain("returned to Verify");
    }

    [Fact]
    public async Task RemovalGate_accepted_proceeds_with_import()
    {
        var rows = new List<ImportRowViewModel> { Row("u0", RowPlanKind.WillUpdate) };
        var plan = new FakeImportPlan
        {
            Rows = rows,
            VerifyOverride = new VerifyResult(rows, 0, 1, 0, 0,
                DestructiveConfirmTitle: "Apply", DestructiveVerb: "Remove", DestructiveNoun: "value",
                DestructiveItems: new[] { "CvlA (2 value(s))" }),
            OutcomeFor = _ => new RowOutcome(RowRunState.Updated, "upd"),
        };
        var gate = new FakeConfirmGate { Answer = true };
        var vm = new ImportWorkflowViewModel(plan, new NullAppLog(), new NullFileOpener(), gate);

        await vm.VerifyCommand.ExecuteAsync(null);
        await vm.StartImportCommand.ExecuteAsync(null);

        gate.Calls.ShouldBe(1);
        plan.AppliedKeys.ShouldBe(new[] { "u0" });
        vm.Updated.ShouldBe(1);
        vm.CurrentStep.ShouldBe(ImportStep.Results);
    }
}
