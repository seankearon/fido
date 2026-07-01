using Avalonia.Controls;
using Fido;
using Fido.Models;
using Fido.Tests.Infrastructure;
using Fido.ViewModels;
using Fido.Views;

namespace Fido.Tests.Dialogs;

/// <summary>
/// Renders the deletion UI — the confirmation dialog and the branch-folder chooser with its delete button —
/// in both themes and captures each as a PNG for the docs (see <c>Docs/screenshots</c>). The assertions keep
/// it a real test: the capture is a side effect, as everywhere else in the suite.
/// </summary>
[NotInParallel]
public class DeletionScreenshotTests
{
    private static WorktreeDeletion SampleDeletion() => new(
        MainWorktreePath: "/home/dev/repos/MyApp",
        WorktreePath: "/home/dev/worktrees/feature-login",
        Branch: "feature/login",
        RemoteBranchExists: true,
        OutstandingChanges: [],
        OrphanedCommits: 0);

    private static IReadOnlyList<ChooserItem> SampleTargets() =>
    [
        new ChooserItem("MyApp.sln", "repo root"),
        new ChooserItem("MyApp.Tools.slnf", "build"),
        new ChooserItem("Open this folder in Rider", "/home/dev/worktrees/feature-login"),
    ];

    [Test]
    [Arguments(AppTheme.Dark, "dark")]
    [Arguments(AppTheme.Light, "light")]
    public async Task Captures_the_deletion_dialog(AppTheme theme, string name)
    {
        await Harness.OnUi(async owner =>
        {
            App.ApplyTheme(theme);
            try
            {
                var dialog = new DeleteWorktreeDialog(SampleDeletion());
                var result = dialog.ShowDialog<WorktreeDeletionChoice?>(owner);
                UiTestExtensions.Pump();
                Screenshots.Save(dialog, $"delete-worktree-dialog-{name}");

                var vm = (DeleteWorktreeDialogViewModel)dialog.DataContext!;
                await Assert.That(vm.HasRemoteBranch).IsTrue();   // all three checkboxes are present
                await Assert.That(vm.CanConfirm).IsTrue();

                dialog.Close(null);
                await result;
            }
            finally
            {
                App.ApplyTheme(AppTheme.System);   // don't let the theme bleed into other tests
            }
        });
    }

    [Test]
    [Arguments(AppTheme.Dark, "dark")]
    [Arguments(AppTheme.Light, "light")]
    public async Task Captures_the_branch_folder_chooser_with_the_delete_button(AppTheme theme, string name)
    {
        await Harness.OnUi(async owner =>
        {
            App.ApplyTheme(theme);
            try
            {
                var chooser = new ChooserDialog(
                    "Open from branch folder",
                    "Found 2 solution(s). Choose what to open:",
                    SampleTargets(),
                    "Delete worktree & branch");
                var result = chooser.ShowDialog<int?>(owner);
                UiTestExtensions.Pump();
                Screenshots.Save(chooser, $"open-dialog-delete-{name}");

                await Assert.That(chooser.FindControl<Button>("DeleteButton")!.IsVisible).IsTrue();

                chooser.Close(null);
                await result;
            }
            finally
            {
                App.ApplyTheme(AppTheme.System);
            }
        });
    }
}
