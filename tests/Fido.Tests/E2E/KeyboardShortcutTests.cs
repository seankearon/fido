using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Styling;
using Avalonia.VisualTree;
using Fido.Input;
using Fido.Models;
using Fido.Services;
using Fido.Tests.Infrastructure;
using Fido.Views;

namespace Fido.Tests.E2E;

/// <summary>
/// Configurable keyboard shortcuts on the real main window, pressed through the headless keyboard so focus and
/// routing are the real thing: two-press chords, the pill that waits for the second press, a second press that
/// types nothing, a tool's own shortcut, the Console's keys staying the shell's, and the dialogs that edit them.
/// </summary>
[NotInParallel]
public class KeyboardShortcutTests
{
    /// <summary>Edits the config a window is about to load, as a saved file from an earlier run would have it.</summary>
    private static void Configure(FidoServices services, Action<AppConfig> edit)
    {
        var config = services.ConfigService.Load();
        edit(config);
        services.ConfigService.Save(config);
    }

    /// <summary>Puts the keyboard in the branch box's text field — where it is on launch, and where typing lands.</summary>
    private static void FocusBranchBox(MainWindow window)
    {
        window.FindControl<AutoCompleteBox>("BranchBox")!.GetVisualDescendants().OfType<TextBox>().First().Focus();
        UiTestExtensions.Pump();
    }

    private static (TestRepoWorld World, FidoServices Services, FakeEditorLauncher Launcher, FakeDialogService Dialogs)
        OneClone()
    {
        var world = new TestRepoWorld();
        var origin = world.CreateOrigin("Foo", "Foo");
        var root = world.SearchRoot("root");
        world.Clone(origin, root, "Foo");
        var launcher = new FakeEditorLauncher();
        var dialogs = new FakeDialogService();
        return (world, world.BuildServices([root], launcher, dialogs), launcher, dialogs);
    }

    [Test]
    public async Task Ctrl_K_then_Ctrl_T_flips_the_theme_from_the_branch_box()
    {
        var (world, services, _, _) = OneClone();
        using var _w = world;

        await Harness.WithWindow(services, async window =>
        {
            App.ApplyTheme(AppTheme.Light);
            FocusBranchBox(window);
            UiTestExtensions.Pump();

            window.TypeKey(Key.K, RawInputModifiers.Control);
            await Assert.That(window.Vm().HasChordStatus).IsTrue();
            await Assert.That(window.Vm().ChordStatus).Contains("Ctrl+K");
            await Assert.That(window.FindControl<Border>("ChordPill")!.IsVisible).IsTrue();
            await Assert.That(Application.Current!.ActualThemeVariant).IsEqualTo(ThemeVariant.Light);   // not yet

            window.TypeKey(Key.T, RawInputModifiers.Control);

            await Assert.That(Application.Current!.ActualThemeVariant).IsEqualTo(ThemeVariant.Dark);
            await Assert.That(window.Vm().HasChordStatus).IsFalse();
            App.ApplyTheme(AppTheme.System);
        });
    }

    [Test]
    public async Task A_plain_second_press_runs_the_chord_and_types_nothing()
    {
        var (world, services, _, _) = OneClone();
        using var _w = world;
        Configure(services, c => c.Shortcuts["ToggleTheme"] = "Ctrl+K, T");

        await Harness.WithWindow(services, async window =>
        {
            App.ApplyTheme(AppTheme.Light);
            FocusBranchBox(window);
            window.TypeKey(Key.A, text: "a");   // typing still types
            await Assert.That(window.Vm().BranchName).IsEqualTo("a");

            window.TypeKey(Key.K, RawInputModifiers.Control);
            window.TypeKey(Key.T, text: "t");

            await Assert.That(Application.Current!.ActualThemeVariant).IsEqualTo(ThemeVariant.Dark);
            await Assert.That(window.Vm().BranchName).IsEqualTo("a");   // the T was the chord's, not the box's

            window.TypeKey(Key.T, text: "t");                            // …and the next T is typing again
            await Assert.That(window.Vm().BranchName).IsEqualTo("at");
            App.ApplyTheme(AppTheme.System);
        });
    }

    [Test]
    public async Task A_chord_that_leads_nowhere_says_so_and_types_nothing()
    {
        var (world, services, launcher, _) = OneClone();
        using var _w = world;

        await Harness.WithWindow(services, async window =>
        {
            FocusBranchBox(window);
            window.TypeKey(Key.K, RawInputModifiers.Control);
            window.TypeKey(Key.X, text: "x");

            await Assert.That(window.Vm().IsChordMiss).IsTrue();
            await Assert.That(window.Vm().ChordStatus).IsEqualTo("Ctrl+K, X isn't a shortcut");
            await Assert.That(window.Vm().BranchName).IsEqualTo("");
            await Assert.That(launcher.Launches.Count).IsEqualTo(0);
        });
    }

    [Test]
    public async Task Esc_calls_off_a_waiting_chord()
    {
        var (world, services, _, dialogs) = OneClone();
        using var _w = world;

        await Harness.WithWindow(services, async window =>
        {
            FocusBranchBox(window);
            window.TypeKey(Key.K, RawInputModifiers.Control);
            window.TypeKey(Key.Escape);
            await Assert.That(window.Vm().HasChordStatus).IsFalse();

            window.TypeKey(Key.S, RawInputModifiers.Control);   // on its own, not the second half of anything
            await Assert.That(dialogs.ShortcutsShownCount).IsEqualTo(0);
        });
    }

    [Test]
    public async Task A_tool_can_be_given_a_chord_and_its_button_says_so()
    {
        var (world, services, launcher, _) = OneClone();
        using var _w = world;
        Configure(services, c => c.Editors.First(e => e.Kind == EditorKind.VsCode).Shortcut = "Ctrl+K, V");

        await Harness.WithWindow(services, async window =>
        {
            await window.Discover("main");
            var vsCode = window.Vm().GridTools.Single(t => t.Name == "VS Code");
            await Assert.That(vsCode.Gesture).IsEqualTo("Ctrl+K, V");

            FocusBranchBox(window);
            window.TypeKey(Key.D3, RawInputModifiers.Control);   // its old number no longer opens it
            UiTestExtensions.Pump();
            await Assert.That(launcher.Launches.Count).IsEqualTo(0);

            window.TypeKey(Key.K, RawInputModifiers.Control);
            window.TypeKey(Key.V, text: "v");

            var completed = await Task.WhenAny(launcher.FirstLaunch, Task.Delay(TimeSpan.FromSeconds(10)));
            await Assert.That(completed).IsEqualTo((Task)launcher.FirstLaunch);
            await Assert.That(launcher.LastLaunch!.Value.Editor.Kind).IsEqualTo(EditorKind.VsCode);
            await Assert.That(window.Vm().BranchName).IsEqualTo("main");
        });
    }

    [Test]
    public async Task Ctrl_Enter_in_the_branch_box_is_free_to_open_the_default_tool()
    {
        var (world, services, launcher, _) = OneClone();
        using var _w = world;
        Configure(services, c => c.Shortcuts["OpenDefault"] = "Ctrl+Enter");

        await Harness.WithWindow(services, async window =>
        {
            await window.Discover("main");
            FocusBranchBox(window);

            window.TypeKey(Key.Enter, RawInputModifiers.Control);

            var completed = await Task.WhenAny(launcher.FirstLaunch, Task.Delay(TimeSpan.FromSeconds(10)));
            await Assert.That(completed).IsEqualTo((Task)launcher.FirstLaunch);
            await Assert.That(launcher.LastLaunch!.Value.Editor.Kind).IsEqualTo(EditorKind.Rider);   // the hero
        });
    }

    [Test]
    public async Task Go_to_the_solution_filter_puts_typing_there_and_replaces_what_was_in_it()
    {
        var (world, services, _, _) = OneClone();
        using var _w = world;
        Configure(services, c => c.Shortcuts["FocusSolution"] = "Ctrl+K, F");

        await Harness.WithWindow(services, async window =>
        {
            window.SetText("SolutionBox", "Old");
            FocusBranchBox(window);

            window.TypeKey(Key.K, RawInputModifiers.Control);
            window.TypeKey(Key.F, text: "f");
            window.TypeKey(Key.N, text: "n");

            await Assert.That(window.Vm().SolutionFilter).IsEqualTo("n");
            await Assert.That(window.Vm().BranchName).IsEqualTo("");
        });
    }

    [Test]
    public async Task Keys_pressed_in_the_Console_are_the_shells()
    {
        var (world, services, launcher, _) = OneClone();
        using var _w = world;

        await Harness.WithWindow(services, async window =>
        {
            await window.Discover("main");
            var console = window.FindControl<Control>("ConsoleView")!;

            // Ctrl+1 opens Rider anywhere else on the screen; from the console it goes to the shell.
            console.RaiseEvent(new KeyEventArgs
            {
                RoutedEvent = InputElement.KeyDownEvent,
                Key = Key.D1,
                KeyModifiers = KeyModifiers.Control,
            });
            UiTestExtensions.Pump();
            await Assert.That(launcher.Launches.Count).IsEqualTo(0);

            // So does the first half of a chord: Ctrl+K is kill-to-end-of-line there.
            console.RaiseEvent(new KeyEventArgs
            {
                RoutedEvent = InputElement.KeyDownEvent,
                Key = Key.K,
                KeyModifiers = KeyModifiers.Control,
            });
            UiTestExtensions.Pump();
            await Assert.That(window.Vm().HasChordStatus).IsFalse();
        });
    }

    [Test]
    public async Task The_numbered_tool_shortcuts_still_wait_for_discovery()
    {
        var (world, services, launcher, _) = OneClone();
        using var _w = world;

        await Harness.WithWindow(services, async window =>
        {
            window.PressKey(Key.D1, KeyModifiers.Control);
            await Assert.That(launcher.Launches.Count).IsEqualTo(0);
        });
    }

    [Test]
    public async Task Settings_and_the_shortcuts_dialog_have_shortcuts_of_their_own()
    {
        var (world, services, _, dialogs) = OneClone();
        using var _w = world;

        await Harness.WithWindow(services, async window =>
        {
            window.PressKey(Key.OemComma, KeyModifiers.Control);
            await Assert.That(dialogs.SettingsShownCount).IsEqualTo(1);

            window.PressKey(Key.K, KeyModifiers.Control);
            window.PressKey(Key.S, KeyModifiers.Control);
            await Assert.That(dialogs.ShortcutsShownCount).IsEqualTo(1);

            // The gear popover names both.
            await Assert.That(window.Vm().SettingsGesture).IsEqualTo("Ctrl+,");
            await Assert.That(window.Vm().ShortcutsGesture).IsEqualTo("Ctrl+K, Ctrl+S");
        });
    }

    [Test]
    public async Task What_the_shortcuts_dialog_saves_is_in_force_straight_away()
    {
        var (world, services, launcher, dialogs) = OneClone();
        using var _w = world;
        dialogs.OnShowShortcuts = config =>
        {
            config.Editors[0].Shortcut = "Alt+R";        // Rider
            config.Shortcuts["ToggleTheme"] = "";         // no shortcut at all
        };

        await Harness.WithWindow(services, async window =>
        {
            await window.Discover("main");
            await window.ShowShortcutsAsync();

            await Assert.That(window.Vm().HeroTool!.Gesture).IsEqualTo("Alt+R");

            App.ApplyTheme(AppTheme.Light);
            window.PressKey(Key.K, KeyModifiers.Control);
            window.PressKey(Key.T, KeyModifiers.Control);
            await Assert.That(Application.Current!.ActualThemeVariant).IsEqualTo(ThemeVariant.Light);
            App.ApplyTheme(AppTheme.System);

            window.PressKey(Key.R, KeyModifiers.Alt);
            var completed = await Task.WhenAny(launcher.FirstLaunch, Task.Delay(TimeSpan.FromSeconds(10)));
            await Assert.That(completed).IsEqualTo((Task)launcher.FirstLaunch);
            await Assert.That(launcher.LastLaunch!.Value.Editor.Kind).IsEqualTo(EditorKind.Rider);
        });
    }

    [Test]
    public async Task Next_and_previous_location_move_the_selection_and_wrap()
    {
        using var world = new TestRepoWorld();
        var origin = world.CreateOrigin("Foo", "Foo");
        var rootA = world.SearchRoot("rootA");
        var rootB = world.SearchRoot("rootB");
        world.CreateBranch(world.Clone(origin, rootA, "Foo"), "feature/shared");
        world.CreateBranch(world.Clone(origin, rootB, "Foo"), "feature/shared");
        var services = world.BuildServices([rootA, rootB], new FakeEditorLauncher(), new FakeDialogService());
        Configure(services, c =>
        {
            c.Shortcuts["NextLocation"] = "Alt+Down";
            c.Shortcuts["PreviousLocation"] = "Alt+Up";
        });

        await Harness.WithWindow(services, async window =>
        {
            await window.Discover("feature/shared");
            var vm = window.Vm();
            await Assert.That(vm.SelectedTarget).IsEqualTo(vm.Targets[0]);

            window.PressKey(Key.Down, KeyModifiers.Alt);
            await Assert.That(vm.SelectedTarget).IsEqualTo(vm.Targets[1]);
            window.PressKey(Key.Down, KeyModifiers.Alt);
            await Assert.That(vm.SelectedTarget).IsEqualTo(vm.Targets[0]);   // round again
            window.PressKey(Key.Up, KeyModifiers.Alt);
            await Assert.That(vm.SelectedTarget).IsEqualTo(vm.Targets[1]);
        });
    }

    [Test]
    public async Task The_panel_shortcuts_switch_tabs()
    {
        var (world, services, _, _) = OneClone();
        using var _w = world;
        Configure(services, c =>
        {
            c.Shortcuts["ShowConsole"] = "Ctrl+K, C";
            c.Shortcuts["ShowFlightLog"] = "Ctrl+K, L";
        });

        await Harness.WithWindow(services, async window =>
        {
            window.PressKey(Key.K, KeyModifiers.Control);
            window.PressKey(Key.C);
            await Assert.That(window.Vm().IsConsoleTab).IsTrue();

            window.PressKey(Key.K, KeyModifiers.Control);
            window.PressKey(Key.L);
            await Assert.That(window.Vm().IsFlightLogTab).IsTrue();
        });
    }

    [Test]
    public async Task A_click_calls_off_a_waiting_chord()
    {
        var (world, services, _, _) = OneClone();
        using var _w = world;

        await Harness.WithWindow(services, async window =>
        {
            window.PressKey(Key.K, KeyModifiers.Control);
            await Assert.That(window.Vm().HasChordStatus).IsTrue();

            window.FindControl<Control>("BranchBox")!.RaiseEvent(new PointerPressedEventArgs(
                window, new Pointer(0, PointerType.Mouse, true), window, new Point(10, 10), 0,
                new PointerPointProperties(RawInputModifiers.LeftMouseButton, PointerUpdateKind.LeftButtonPressed),
                KeyModifiers.None));
            UiTestExtensions.Pump();

            await Assert.That(window.Vm().HasChordStatus).IsFalse();
        });
    }
}
