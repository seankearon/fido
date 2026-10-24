using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.VisualTree;
using Fido.Services;
using Fido.Tests.Infrastructure;
using Fido.Views;

namespace Fido.Tests.E2E;

/// <summary>
/// The recently-used drop-downs under the branch and solution boxes, driven through the headless keyboard:
/// Delete on the highlighted suggestion forgets it, the keyboard's way to the ✕ beside it.
/// </summary>
[NotInParallel]
public class MruDropDownTests
{
    private static (TestRepoWorld World, FidoServices Services) Remembering(
        string[] branches, string[]? solutions = null)
    {
        var world = new TestRepoWorld();
        var services = world.BuildServices([world.SearchRoot("root")], new FakeEditorLauncher(), new FakeDialogService());
        var config = services.ConfigService.Load();
        config.RecentBranches = [.. branches];
        config.RecentSolutions = [.. solutions ?? []];
        services.ConfigService.Save(config);
        return (world, services);
    }

    /// <summary>Puts the keyboard in a box's text field and opens its suggestions, as Ctrl+Space does.</summary>
    private static AutoCompleteBox OpenSuggestions(MainWindow window, string boxName)
    {
        var box = window.FindControl<AutoCompleteBox>(boxName)!;
        box.GetVisualDescendants().OfType<TextBox>().First().Focus();
        UiTestExtensions.Pump();
        window.TypeKey(Key.Space, RawInputModifiers.Control);
        return box;
    }

    [Test]
    public async Task Delete_forgets_the_highlighted_branch_and_the_highlight_moves_on()
    {
        var (world, services) = Remembering(["alpha", "beta", "gamma"]);
        using var _w = world;

        await Harness.WithWindow(services, async window =>
        {
            var box = OpenSuggestions(window, "BranchBox");
            await Assert.That(box.IsDropDownOpen).IsTrue();

            window.TypeKey(Key.Down);
            await Assert.That(box.Text).IsEqualTo("alpha");

            window.TypeKey(Key.Delete);

            await Assert.That(window.Vm().RecentBranches).IsEquivalentTo(["beta", "gamma"]);
            await Assert.That(services.ConfigService.Load().RecentBranches).IsEquivalentTo(["beta", "gamma"]);
            await Assert.That(box.IsDropDownOpen).IsTrue();
            await Assert.That(box.Text).IsEqualTo("beta");   // the next one is highlighted, as an arrow would
        });
    }

    [Test]
    public async Task Deleting_the_last_suggestion_closes_the_list_on_what_was_typed()
    {
        var (world, services) = Remembering(["alpha", "beta"]);
        using var _w = world;

        await Harness.WithWindow(services, async window =>
        {
            var box = OpenSuggestions(window, "BranchBox");
            window.TypeKey(Key.Down);
            window.TypeKey(Key.Down);
            await Assert.That(box.Text).IsEqualTo("beta");

            window.TypeKey(Key.Delete);   // the bottom row: the highlight moves up
            await Assert.That(box.Text).IsEqualTo("alpha");

            window.TypeKey(Key.Delete);

            await Assert.That(window.Vm().RecentBranches).IsEmpty();
            await Assert.That(services.ConfigService.Load().RecentBranches).IsEmpty();
            await Assert.That(box.IsDropDownOpen).IsFalse();
            await Assert.That(box.Text).IsEqualTo("");
        });
    }

    [Test]
    public async Task With_nothing_highlighted_Delete_is_the_text_boxs()
    {
        var (world, services) = Remembering(["alpha", "beta"]);
        using var _w = world;

        await Harness.WithWindow(services, async window =>
        {
            var box = OpenSuggestions(window, "BranchBox");
            await Assert.That(box.IsDropDownOpen).IsTrue();

            window.TypeKey(Key.Delete);

            await Assert.That(window.Vm().RecentBranches).IsEquivalentTo(["alpha", "beta"]);
            await Assert.That(services.ConfigService.Load().RecentBranches).IsEquivalentTo(["alpha", "beta"]);
        });
    }

    [Test]
    public async Task Delete_forgets_a_solution_and_leaves_the_branches_alone()
    {
        var (world, services) = Remembering(["alpha"], ["Shop", "Admin"]);
        using var _w = world;

        await Harness.WithWindow(services, async window =>
        {
            var box = OpenSuggestions(window, "SolutionBox");
            window.TypeKey(Key.Down);
            await Assert.That(box.Text).IsEqualTo("Shop");

            window.TypeKey(Key.Delete);

            await Assert.That(window.Vm().RecentSolutions).IsEquivalentTo(["Admin"]);
            await Assert.That(services.ConfigService.Load().RecentSolutions).IsEquivalentTo(["Admin"]);
            await Assert.That(services.ConfigService.Load().RecentBranches).IsEquivalentTo(["alpha"]);
        });
    }
}
