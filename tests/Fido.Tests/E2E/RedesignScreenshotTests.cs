using Fido.Models;
using Fido.Tests.Infrastructure;
using Fido.ViewModels;

namespace Fido.Tests.E2E;

/// <summary>
/// Walks the redesigned main screen through every visual state from the design handoff
/// (design/design_handoff_fido_redesign) and captures a frame of each for human review:
/// idle, scanning, found (single and multiple), not found, delete confirm, and the
/// no-default equal-weight tool grid. Logical assertions are minimal — these are eyes-on
/// artifacts; the behavioural suites carry the real checks.
/// </summary>
[NotInParallel]
public class RedesignScreenshotTests
{
    [Test]
    public async Task Capture_every_phase_of_the_redesigned_main_screen()
    {
        // All git-world setup happens up front: TestRepoWorld blocks synchronously on git, which
        // would deadlock inside the harness body (it runs on the headless UI thread).
        using var world = new TestRepoWorld();
        var origin = world.CreateOrigin("Platform", "Platform");
        var root = world.SearchRoot("root");
        var clone = world.Clone(origin, root, "Platform");
        var worktree = world.AddWorktree(clone, "feature/checkout-flow");
        world.AddWorktree(clone, "feature/solo");
        var clone2 = world.Clone(origin, root, "Platform2");
        world.CreateBranch(clone2, "feature/checkout-flow");   // same branch, second location

        var rider = new FakeEditorLauncher();
        var dialogs = new FakeDialogService();
        var services = world.BuildServices([root], rider, dialogs);

        await Harness.WithWindow(services, async window =>
        {
            // 1. Idle — dashed placeholder, everything locked.
            Screenshots.Save(window, "redesign-1-idle");
            await Assert.That(window.Vm().IsIdle).IsTrue();

            // 2. Scanning — driven through the VM so the frame freezes mid-phase.
            window.Vm().BeginScan("feature/checkout-flow");
            window.Vm().SetScanTreeCount(24);
            UiTestExtensions.Pump();
            Screenshots.Save(window, "redesign-2-scanning");

            // 3. Found, single worktree — hero button, solution chips, delete enabled.
            await window.Discover("feature/solo");
            await Assert.That(window.Vm().IsFound).IsTrue();
            Screenshots.Save(window, "redesign-3-found-single");

            // 4. Found, multiple locations — helper strip + one card per checkout
            //    (the worktree in Platform plus Platform2's main tree on the same branch).
            await window.Discover("feature/checkout-flow");
            await Assert.That(window.Vm().HasMultipleTargets).IsTrue();
            Screenshots.Save(window, "redesign-4-found-multiple");

            // 5. Delete confirm — the in-place strip replacing the button.
            window.Vm().SelectedTarget = window.CardWithPath(worktree);
            await window.RequestDeleteAsync();
            await Assert.That(window.Vm().IsConfirmingDelete).IsTrue();
            Screenshots.Save(window, "redesign-5-delete-confirm");
            window.Vm().CancelDeleteConfirm();

            // 6. Main clone selected — delete disabled with the "main clone stays put" note.
            window.Vm().SelectedTarget = window.CardWithPath("Platform2");
            UiTestExtensions.Pump();
            Screenshots.Save(window, "redesign-6-clone-selected");

            // 7. Not found — warning card, actions locked.
            await window.Discover("no/such/branch");
            await Assert.That(window.Vm().IsNotFound).IsTrue();
            Screenshots.Save(window, "redesign-7-not-found");

            // 8. No default tool — equal-weight grid, no hero.
            await window.Discover("feature/checkout-flow");
            window.Vm().SetEditors(Editor.Defaults(), AppConfig.NoDefaultEditor);
            UiTestExtensions.Pump();
            Screenshots.Save(window, "redesign-8-no-default-grid");
        });
    }
}
