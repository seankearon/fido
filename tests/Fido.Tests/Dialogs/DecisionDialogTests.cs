using Avalonia.Input;
using Fido.Models;
using Fido.Tests.Infrastructure;
using Fido.ViewModels;
using Fido.Views;

namespace Fido.Tests.Dialogs;

/// <summary>The real DecisionDialog window: keyboard shortcuts, button clicks, and dirty-tree display.</summary>
[NotInParallel]
public class DecisionDialogTests
{
    private static (RepositoryInfo Repo, MainContext Context) Make(bool dirty)
    {
        var repo = new RepositoryInfo(Path.Combine("repo", "Foo"), "Foo.sln");
        var context = new MainContext
        {
            MainWorktreePath = repo.MainWorktreePath,
            CurrentBranch = "main",
            BranchExistsLocally = false,
            BranchExistsOnRemote = true,
            OutstandingChanges = dirty ? [" M src/A.cs", "?? notes.txt"] : [],
            ProposedWorktreePath = Path.Combine("repo", "Foo.worktrees", "feature-x"),
            StartPoint = "origin/main",
            StartPointDescription = "new branch from origin/main",
        };
        return (repo, context);
    }

    [Test]
    [Arguments(Key.W, OpenDecision.Worktree)]
    [Arguments(Key.M, OpenDecision.Main)]
    [Arguments(Key.Escape, OpenDecision.Cancel)]
    public async Task Keyboard_shortcut_resolves_the_decision(Key key, OpenDecision expected)
    {
        await Harness.OnUi(async owner =>
        {
            var (repo, context) = Make(dirty: false);
            var dialog = new DecisionDialog(repo, "feature/x", context);
            var resultTask = dialog.ShowDialog<OpenDecision?>(owner);
            UiTestExtensions.Pump();

            dialog.PressKey(key);

            await Assert.That(await resultTask).IsEqualTo(expected);
        });
    }

    [Test]
    public async Task Clicking_checkout_in_main_returns_main()
    {
        await Harness.OnUi(async owner =>
        {
            var (repo, context) = Make(dirty: false);
            var dialog = new DecisionDialog(repo, "feature/x", context);
            var resultTask = dialog.ShowDialog<OpenDecision?>(owner);
            UiTestExtensions.Pump();

            dialog.ClickButton("MainButton");

            await Assert.That(await resultTask).IsEqualTo(OpenDecision.Main);
        });
    }

    [Test]
    public async Task A_dirty_main_tree_shows_outstanding_changes_and_the_switch_warning()
    {
        await Harness.OnUi(async owner =>
        {
            var (repo, context) = Make(dirty: true);
            var dialog = new DecisionDialog(repo, "feature/x", context);
            var resultTask = dialog.ShowDialog<OpenDecision?>(owner);
            UiTestExtensions.Pump();
            Screenshots.Save(dialog, "decision-dialog-dirty");

            var vm = (DecisionDialogViewModel)dialog.DataContext!;
            await Assert.That(vm.HasOutstandingChanges).IsTrue();
            await Assert.That(vm.ShowSwitchWarning).IsTrue();
            await Assert.That(vm.OutstandingChanges.Count).IsEqualTo(2);

            dialog.PressKey(Key.Escape);
            await resultTask;
        });
    }
}
