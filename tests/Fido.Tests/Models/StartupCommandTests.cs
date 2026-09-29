using Fido.Models;

namespace Fido.Tests.Models;

/// <summary>
/// Command-line parsing on its own — pure, no window. The window-level behaviour of the same arguments is
/// pinned by the startup E2E tests; these cover what the launch reads before any window exists.
/// </summary>
public class StartupCommandTests
{
    [Test]
    public async Task Bare_arguments_are_the_branch_then_the_tool()
    {
        var command = StartupCommand.Parse(["feature/x", "rider"]);

        await Assert.That(command.Branch).IsEqualTo("feature/x");
        await Assert.That(command.ToolSlug).IsEqualTo("rider");
        await Assert.That(command.Solution).IsNull();
        await Assert.That(command.PreferFolder).IsFalse();
        await Assert.That(command.NewWindow).IsFalse();
    }

    [Test]
    public async Task Explicit_options_are_read_in_any_order()
    {
        var command = StartupCommand.Parse(["-s", "MyApp", "--folder", "-t", "vs", "-b", "feature/x"]);

        await Assert.That(command.Branch).IsEqualTo("feature/x");
        await Assert.That(command.Solution).IsEqualTo("MyApp");
        await Assert.That(command.ToolSlug).IsEqualTo("vs");
        await Assert.That(command.PreferFolder).IsTrue();
    }

    [Test]
    public async Task An_explicit_tool_wins_over_the_positional_one_wherever_it_sits()
    {
        await Assert.That(StartupCommand.Parse(["feature/x", "rider", "--tool", "zed"]).ToolSlug).IsEqualTo("zed");
        await Assert.That(StartupCommand.Parse(["--editor", "zed", "feature/x", "rider"]).ToolSlug).IsEqualTo("zed");
    }

    [Test]
    public async Task A_positional_after_an_explicit_branch_is_the_tool()
    {
        var command = StartupCommand.Parse(["--branch", "feature/x", "rider"]);

        await Assert.That(command.Branch).IsEqualTo("feature/x");
        await Assert.That(command.ToolSlug).IsEqualTo("rider");
    }

    [Test]
    public async Task New_window_is_read_in_either_spelling()
    {
        await Assert.That(StartupCommand.Parse(["feature/x", "--new-window"]).NewWindow).IsTrue();
        await Assert.That(StartupCommand.Parse(["-n", "feature/x"]).NewWindow).IsTrue();
    }

    [Test]
    public async Task An_option_missing_its_value_and_unknown_flags_are_ignored()
    {
        var command = StartupCommand.Parse(["--whatever", "feature/x", "-t"]);

        await Assert.That(command.Branch).IsEqualTo("feature/x");
        await Assert.That(command.ToolSlug).IsNull();
    }

    [Test]
    public async Task No_arguments_name_nothing()
    {
        var command = StartupCommand.Parse([]);

        await Assert.That(command.Branch).IsNull();
        await Assert.That(command.NamesMoreThanBranch).IsFalse();
    }

    [Test]
    public async Task Only_a_tool_a_solution_or_the_folder_chip_names_more_than_the_branch()
    {
        await Assert.That(StartupCommand.Parse(["feature/x"]).NamesMoreThanBranch).IsFalse();
        await Assert.That(StartupCommand.Parse(["feature/x", "--new-window"]).NamesMoreThanBranch).IsFalse();
        await Assert.That(StartupCommand.Parse(["feature/x", "rider"]).NamesMoreThanBranch).IsTrue();
        await Assert.That(StartupCommand.Parse(["feature/x", "-s", "MyApp"]).NamesMoreThanBranch).IsTrue();
        await Assert.That(StartupCommand.Parse(["feature/x", "--folder"]).NamesMoreThanBranch).IsTrue();
    }
}
