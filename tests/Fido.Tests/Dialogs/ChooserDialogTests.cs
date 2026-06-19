using Avalonia.Controls;
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
}
