using Avalonia.Controls;
using Avalonia.Input;
using Fido.Tests.Infrastructure;
using Fido.ViewModels;
using Fido.Views;

namespace Fido.Tests.Dialogs;

/// <summary>The real ChooserDialog window, driven through its list and buttons.</summary>
[NotInParallel]
public class ChooserDialogTests
{
    private static ChooserItem[] ThreeItems() =>
        [new ChooserItem("Alpha"), new ChooserItem("Beta"), new ChooserItem("Gamma")];

    [Test]
    public async Task Selecting_an_item_and_clicking_open_returns_its_index()
    {
        await Harness.OnUi(async owner =>
        {
            var dialog = new ChooserDialog("Pick one", "Choose a repository", ThreeItems());
            var resultTask = dialog.ShowDialog<int?>(owner);
            UiTestExtensions.Pump();
            Screenshots.Save(dialog, "chooser-dialog");

            dialog.FindControl<ListBox>("Chooser")!.SelectedIndex = 2;
            UiTestExtensions.Pump();
            dialog.ClickButton("OkButton");

            await Assert.That(await resultTask).IsEqualTo(2);
        });
    }

    [Test]
    public async Task Cancel_returns_null()
    {
        await Harness.OnUi(async owner =>
        {
            var dialog = new ChooserDialog("Pick one", "Choose a repository", ThreeItems());
            var resultTask = dialog.ShowDialog<int?>(owner);
            UiTestExtensions.Pump();

            dialog.ClickButton("CancelButton");

            await Assert.That(await resultTask).IsNull();
        });
    }

    [Test]
    public async Task Defaults_to_the_first_item()
    {
        await Harness.OnUi(async owner =>
        {
            var dialog = new ChooserDialog("Pick one", "Choose a repository", ThreeItems());
            var resultTask = dialog.ShowDialog<int?>(owner);
            UiTestExtensions.Pump();

            dialog.ClickButton("OkButton");   // no explicit selection

            await Assert.That(await resultTask).IsEqualTo(0);
        });
    }

    [Test]
    public async Task Arrow_keys_move_the_selection_and_clamp_at_the_ends()
    {
        await Harness.OnUi(async owner =>
        {
            var dialog = new ChooserDialog("Pick one", "Choose a repository", ThreeItems());
            var resultTask = dialog.ShowDialog<int?>(owner);
            UiTestExtensions.Pump();
            var list = dialog.FindControl<ListBox>("Chooser")!;

            await Assert.That(list.SelectedIndex).IsEqualTo(0);   // defaults to the first row

            dialog.PressKey(Key.Down);
            await Assert.That(list.SelectedIndex).IsEqualTo(1);
            dialog.PressKey(Key.Down);
            await Assert.That(list.SelectedIndex).IsEqualTo(2);
            dialog.PressKey(Key.Down);                            // clamps at the last row
            await Assert.That(list.SelectedIndex).IsEqualTo(2);

            dialog.PressKey(Key.Up);
            await Assert.That(list.SelectedIndex).IsEqualTo(1);
            dialog.PressKey(Key.Up);
            await Assert.That(list.SelectedIndex).IsEqualTo(0);
            dialog.PressKey(Key.Up);                              // clamps at the first row
            await Assert.That(list.SelectedIndex).IsEqualTo(0);

            dialog.PressKey(Key.Escape);
            await resultTask;
        });
    }

    [Test]
    public async Task Enter_opens_the_highlighted_item()
    {
        await Harness.OnUi(async owner =>
        {
            var dialog = new ChooserDialog("Pick one", "Choose a repository", ThreeItems());
            var resultTask = dialog.ShowDialog<int?>(owner);
            UiTestExtensions.Pump();

            dialog.PressKey(Key.Down);
            dialog.PressKey(Key.Down);   // highlight Gamma (index 2)
            dialog.PressKey(Key.Enter);

            await Assert.That(await resultTask).IsEqualTo(2);
        });
    }

    [Test]
    public async Task Enter_without_navigating_opens_the_first_item()
    {
        await Harness.OnUi(async owner =>
        {
            var dialog = new ChooserDialog("Pick one", "Choose a repository", ThreeItems());
            var resultTask = dialog.ShowDialog<int?>(owner);
            UiTestExtensions.Pump();

            dialog.PressKey(Key.Enter);   // straight to OK on the default (first) row

            await Assert.That(await resultTask).IsEqualTo(0);
        });
    }

    [Test]
    public async Task Arrows_on_the_focused_list_move_exactly_one_row()
    {
        await Harness.OnUi(async owner =>
        {
            var dialog = new ChooserDialog("Pick one", "Choose a repository", ThreeItems());
            var resultTask = dialog.ShowDialog<int?>(owner);
            UiTestExtensions.Pump();
            var list = dialog.FindControl<ListBox>("Chooser")!;

            // Originate the key at the focused list, exactly as a real keypress does, so the ListBox's
            // own navigation and the window's OnKeyDown both get a chance. They must never BOTH move
            // the selection — one Down advances by exactly one row, not two.
            dialog.PressKeyOn("Chooser", Key.Down);
            await Assert.That(list.SelectedIndex).IsEqualTo(1);
            dialog.PressKeyOn("Chooser", Key.Down);
            await Assert.That(list.SelectedIndex).IsEqualTo(2);
            dialog.PressKeyOn("Chooser", Key.Up);
            await Assert.That(list.SelectedIndex).IsEqualTo(1);

            dialog.PressKeyOn("Chooser", Key.Escape);
            await Assert.That(await resultTask).IsNull();
        });
    }

    [Test]
    public async Task Escape_cancels_and_returns_null()
    {
        await Harness.OnUi(async owner =>
        {
            var dialog = new ChooserDialog("Pick one", "Choose a repository", ThreeItems());
            var resultTask = dialog.ShowDialog<int?>(owner);
            UiTestExtensions.Pump();

            dialog.PressKey(Key.Escape);

            await Assert.That(await resultTask).IsNull();
        });
    }
}
