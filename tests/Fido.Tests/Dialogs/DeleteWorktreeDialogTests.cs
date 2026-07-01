using Avalonia.Controls;
using Avalonia.Input;
using Fido.Models;
using Fido.Tests.Infrastructure;
using Fido.ViewModels;
using Fido.Views;

namespace Fido.Tests.Dialogs;

/// <summary>
/// The real DeleteWorktreeDialog window: per-target checkboxes drive what an explicit Delete returns;
/// everything else declines with null.
/// </summary>
[NotInParallel]
public class DeleteWorktreeDialogTests
{
    private static WorktreeDeletion Plan(bool dirty = false, bool onRemote = true, int orphaned = 0) => new(
        MainWorktreePath: Path.Combine("repo", "Foo"),
        WorktreePath: Path.Combine("repo", "Foo.worktrees", "feature-x"),
        Branch: "feature/x",
        RemoteBranchExists: onRemote,
        OutstandingChanges: dirty ? [" M src/A.cs", "?? notes.txt"] : [],
        OrphanedCommits: orphaned);

    [Test]
    public async Task Confirming_with_every_target_ticked_returns_them_all()
    {
        await Harness.OnUi(async owner =>
        {
            var dialog = new DeleteWorktreeDialog(Plan());   // worktree + local + remote all present, all ticked
            var resultTask = dialog.ShowDialog<WorktreeDeletionChoice?>(owner);
            UiTestExtensions.Pump();
            Screenshots.Save(dialog, "delete-worktree-dialog");

            dialog.ClickButton("DeleteButton");

            await Assert.That(await resultTask).IsEqualTo(WorktreeDeletionChoice.All);
        });
    }

    [Test]
    public async Task Unticking_a_target_excludes_it_from_the_result()
    {
        await Harness.OnUi(async owner =>
        {
            var dialog = new DeleteWorktreeDialog(Plan());
            var resultTask = dialog.ShowDialog<WorktreeDeletionChoice?>(owner);
            UiTestExtensions.Pump();

            dialog.SetChecked("RemoteBranchCheck", false);   // keep the origin branch
            dialog.ClickButton("DeleteButton");

            await Assert.That(await resultTask).IsEqualTo(new WorktreeDeletionChoice(true, true, false));
        });
    }

    [Test]
    public async Task Unticking_the_worktree_disables_and_clears_the_local_branch()
    {
        await Harness.OnUi(async owner =>
        {
            var dialog = new DeleteWorktreeDialog(Plan());
            var resultTask = dialog.ShowDialog<WorktreeDeletionChoice?>(owner);
            UiTestExtensions.Pump();
            var vm = (DeleteWorktreeDialogViewModel)dialog.DataContext!;
            var localBox = dialog.FindControl<CheckBox>("LocalBranchCheck")!;

            dialog.SetChecked("WorktreeCheck", false);   // can't delete a branch checked out in a kept worktree

            await Assert.That(vm.DeleteLocalBranch).IsFalse();     // auto-cleared…
            await Assert.That(vm.CanDeleteLocalBranch).IsFalse();  // …and disabled
            await Assert.That(localBox.IsEnabled).IsFalse();
            await Assert.That(vm.ShowLocalBranchCoupledHint).IsTrue();

            dialog.ClickButton("DeleteButton");   // remote is still ticked, so Delete is enabled
            await Assert.That(await resultTask).IsEqualTo(new WorktreeDeletionChoice(false, false, true));
        });
    }

    [Test]
    public async Task The_origin_checkbox_is_hidden_when_the_branch_is_not_on_origin()
    {
        await Harness.OnUi(async owner =>
        {
            var dialog = new DeleteWorktreeDialog(Plan(onRemote: false));
            var resultTask = dialog.ShowDialog<WorktreeDeletionChoice?>(owner);
            UiTestExtensions.Pump();
            var vm = (DeleteWorktreeDialogViewModel)dialog.DataContext!;

            await Assert.That(dialog.FindControl<CheckBox>("RemoteBranchCheck")!.IsVisible).IsFalse();
            await Assert.That(vm.ShowNoRemoteNote).IsTrue();

            dialog.ClickButton("DeleteButton");
            await Assert.That(await resultTask).IsEqualTo(new WorktreeDeletionChoice(true, true, false));
        });
    }

    [Test]
    public async Task Delete_is_disabled_when_nothing_is_selected()
    {
        await Harness.OnUi(async owner =>
        {
            var dialog = new DeleteWorktreeDialog(Plan());
            var resultTask = dialog.ShowDialog<WorktreeDeletionChoice?>(owner);
            UiTestExtensions.Pump();
            var vm = (DeleteWorktreeDialogViewModel)dialog.DataContext!;
            var deleteButton = dialog.FindControl<Button>("DeleteButton")!;

            dialog.SetChecked("WorktreeCheck", false);      // also clears the local-branch box
            dialog.SetChecked("RemoteBranchCheck", false);  // now nothing is ticked

            await Assert.That(vm.CanConfirm).IsFalse();
            await Assert.That(deleteButton.IsEnabled).IsFalse();

            dialog.PressKey(Key.Escape);
            await Assert.That(await resultTask).IsNull();
        });
    }

    [Test]
    public async Task Clicking_cancel_returns_null()
    {
        await Harness.OnUi(async owner =>
        {
            var dialog = new DeleteWorktreeDialog(Plan());
            var resultTask = dialog.ShowDialog<WorktreeDeletionChoice?>(owner);
            UiTestExtensions.Pump();

            dialog.ClickButton("CancelButton");

            await Assert.That(await resultTask).IsNull();
        });
    }

    [Test]
    public async Task Escape_declines_returning_null()
    {
        await Harness.OnUi(async owner =>
        {
            var dialog = new DeleteWorktreeDialog(Plan());
            var resultTask = dialog.ShowDialog<WorktreeDeletionChoice?>(owner);
            UiTestExtensions.Pump();

            dialog.PressKey(Key.Escape);

            await Assert.That(await resultTask).IsNull();
        });
    }

    [Test]
    public async Task A_dirty_worktree_shows_the_data_loss_warning()
    {
        await Harness.OnUi(async owner =>
        {
            var dialog = new DeleteWorktreeDialog(Plan(dirty: true));
            var resultTask = dialog.ShowDialog<WorktreeDeletionChoice?>(owner);
            UiTestExtensions.Pump();
            Screenshots.Save(dialog, "delete-worktree-dialog-dirty");

            var vm = (DeleteWorktreeDialogViewModel)dialog.DataContext!;
            await Assert.That(vm.HasOutstandingChanges).IsTrue();
            await Assert.That(vm.OutstandingSummary).Contains("will be lost");

            dialog.PressKey(Key.Escape);
            await resultTask;
        });
    }

    [Test]
    public async Task Commits_that_exist_only_on_the_branch_get_a_data_loss_warning()
    {
        await Harness.OnUi(async owner =>
        {
            var dialog = new DeleteWorktreeDialog(Plan(onRemote: false, orphaned: 3));
            var resultTask = dialog.ShowDialog<WorktreeDeletionChoice?>(owner);
            UiTestExtensions.Pump();
            Screenshots.Save(dialog, "delete-worktree-dialog-orphaned");

            var vm = (DeleteWorktreeDialogViewModel)dialog.DataContext!;
            await Assert.That(vm.HasOrphanedCommits).IsTrue();
            await Assert.That(vm.OrphanedSummary).Contains("only on 'feature/x'");
            await Assert.That(vm.OrphanedSummary).Contains("3 commit");

            dialog.PressKey(Key.Escape);
            await resultTask;
        });
    }

    [Test]
    public async Task A_fully_pushed_clean_branch_shows_no_warnings()
    {
        await Harness.OnUi(async owner =>
        {
            var dialog = new DeleteWorktreeDialog(Plan());   // clean, on remote, nothing orphaned
            var resultTask = dialog.ShowDialog<WorktreeDeletionChoice?>(owner);
            UiTestExtensions.Pump();

            var vm = (DeleteWorktreeDialogViewModel)dialog.DataContext!;
            await Assert.That(vm.HasOutstandingChanges).IsFalse();
            await Assert.That(vm.HasOrphanedCommits).IsFalse();

            dialog.PressKey(Key.Escape);
            await resultTask;
        });
    }
}
