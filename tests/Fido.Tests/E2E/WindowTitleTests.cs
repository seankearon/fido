using Fido.Models;
using Fido.Services;
using Fido.Tests.Infrastructure;

namespace Fido.Tests.E2E;

/// <summary>
/// The window title tracks discovery: once a branch resolves, the title bar (and so the taskbar
/// button) names the work — <c>&lt;repo&gt; · &lt;branch&gt;</c>, with no "Fido" in front — following the
/// selected card and falling back to the plain app name when nothing is resolved. The
/// <see cref="AppConfig.ShowTargetInWindowTitle"/> setting turns the whole behaviour off.
/// </summary>
[NotInParallel]
public class WindowTitleTests
{
    /// <summary>Rewrites the saved config the window will load — the setting is read in its constructor.</summary>
    private static void ShowTargetInTitle(ConfigService configService, bool value)
    {
        var config = configService.Load();
        config.ShowTargetInWindowTitle = value;
        configService.Save(config);
    }

    [Test]
    public async Task A_resolved_branch_names_the_repo_and_branch_without_Fido_in_front()
    {
        using var world = new TestRepoWorld();
        var origin = world.CreateOrigin("Foo", "Foo");
        var root = world.SearchRoot("root");
        var clone = world.Clone(origin, root, "Foo");
        world.AddWorktree(clone, "feature/x");

        var services = world.BuildServices([root], new FakeEditorLauncher(), new FakeDialogService());

        await Harness.WithWindow(services, async window =>
        {
            await Assert.That(window.Title).IsEqualTo("Fido");   // nothing resolved yet

            await window.Discover("feature/x");

            await Assert.That(window.Vm().Phase).IsEqualTo(DiscoveryPhase.Found);
            await Assert.That(window.Title).IsEqualTo("Foo · feature/x");
            await Assert.That(window.Title!.StartsWith("Fido")).IsFalse();
        });
    }

    [Test]
    public async Task The_title_follows_the_selected_card_across_repos()
    {
        using var world = new TestRepoWorld();
        var originFoo = world.CreateOrigin("Foo", "Foo");
        var originBar = world.CreateOrigin("Bar", "Bar");
        var root = world.SearchRoot("root");
        var cloneFoo = world.Clone(originFoo, root, "Foo");
        var cloneBar = world.Clone(originBar, root, "Bar");
        var wtFoo = world.AddWorktree(cloneFoo, "feature/x");
        var wtBar = world.AddWorktree(cloneBar, "feature/x");

        var services = world.BuildServices([root], new FakeEditorLauncher(), new FakeDialogService());

        await Harness.WithWindow(services, async window =>
        {
            await window.Discover("feature/x");
            var vm = window.Vm();

            vm.SelectedTarget = window.CardWithPath(wtBar);
            UiTestExtensions.Pump();
            await Assert.That(window.Title).IsEqualTo("Bar · feature/x");

            vm.SelectedTarget = window.CardWithPath(wtFoo);
            UiTestExtensions.Pump();
            await Assert.That(window.Title).IsEqualTo("Foo · feature/x");
        });
    }

    [Test]
    public async Task An_unresolved_branch_leaves_the_plain_app_name()
    {
        using var world = new TestRepoWorld();
        var origin = world.CreateOrigin("Foo", "Foo");
        var root = world.SearchRoot("root");
        var clone = world.Clone(origin, root, "Foo");
        world.AddWorktree(clone, "feature/x");

        var services = world.BuildServices([root], new FakeEditorLauncher(), new FakeDialogService());

        await Harness.WithWindow(services, async window =>
        {
            await window.Discover("feature/x");
            await Assert.That(window.Title).IsEqualTo("Foo · feature/x");

            // A branch nobody has: nothing resolved, so the title goes back to the app's own name.
            await window.Discover("feature/nowhere");
            await Assert.That(window.Vm().Phase).IsEqualTo(DiscoveryPhase.NotFound);
            await Assert.That(window.Title).IsEqualTo("Fido");

            // …and so does clearing the branch box altogether.
            await window.Discover("feature/x");
            window.SetText("BranchBox", "");
            await Assert.That(window.Vm().Phase).IsEqualTo(DiscoveryPhase.Idle);
            await Assert.That(window.Title).IsEqualTo("Fido");
        });
    }

    [Test]
    public async Task The_setting_turns_the_renaming_off()
    {
        using var world = new TestRepoWorld();
        var origin = world.CreateOrigin("Foo", "Foo");
        var root = world.SearchRoot("root");
        var clone = world.Clone(origin, root, "Foo");
        world.AddWorktree(clone, "feature/x");

        var services = world.BuildServices([root], new FakeEditorLauncher(), new FakeDialogService());
        ShowTargetInTitle(services.ConfigService, false);

        await Harness.WithWindow(services, async window =>
        {
            await window.Discover("feature/x");

            await Assert.That(window.Vm().Phase).IsEqualTo(DiscoveryPhase.Found);
            await Assert.That(window.Title).IsEqualTo("Fido");   // resolved, but the title stays put
        });
    }
}
