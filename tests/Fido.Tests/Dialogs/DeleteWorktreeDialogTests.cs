using Avalonia.Input;
using Fido.Models;
using Fido.Tests.Infrastructure;
using Fido.ViewModels;
using Fido.Views;

namespace Fido.Tests.Dialogs;

/// <summary>The real DeleteWorktreeDialog window: an explicit Delete confirms; everything else declines.</summary>
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
    public async Task Clicking_delete_returns_true()
    {
        await Harness.OnUi(async owner =>
        {
            var dialog = new DeleteWorktreeDialog(Plan());
            var resultTask = dialog.ShowDialog<bool>(owner);
            UiTestExtensions.Pump();
            Screenshots.Save(dialog, "delete-worktree-dialog");

            dialog.ClickButton("DeleteButton");

            await Assert.That(await resultTask).IsTrue();
        });
    }

    [Test]
    public async Task Clicking_cancel_returns_false()
    {
        await Harness.OnUi(async owner =>
        {
            var dialog = new DeleteWorktreeDialog(Plan());
            var resultTask = dialog.ShowDialog<bool>(owner);
            UiTestExtensions.Pump();

            dialog.ClickButton("CancelButton");

            await Assert.That(await resultTask).IsFalse();
        });
    }

    [Test]
    public async Task Escape_declines_returning_false()
    {
        await Harness.OnUi(async owner =>
        {
            var dialog = new DeleteWorktreeDialog(Plan());
            var resultTask = dialog.ShowDialog<bool>(owner);
            UiTestExtensions.Pump();

            dialog.PressKey(Key.Escape);

            await Assert.That(await resultTask).IsFalse();
        });
    }

    [Test]
    public async Task A_dirty_worktree_shows_the_data_loss_warning()
    {
        await Harness.OnUi(async owner =>
        {
            var dialog = new DeleteWorktreeDialog(Plan(dirty: true));
            var resultTask = dialog.ShowDialog<bool>(owner);
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
            var resultTask = dialog.ShowDialog<bool>(owner);
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
            var resultTask = dialog.ShowDialog<bool>(owner);
            UiTestExtensions.Pump();

            var vm = (DeleteWorktreeDialogViewModel)dialog.DataContext!;
            await Assert.That(vm.HasOutstandingChanges).IsFalse();
            await Assert.That(vm.HasOrphanedCommits).IsFalse();

            dialog.PressKey(Key.Escape);
            await resultTask;
        });
    }
}
