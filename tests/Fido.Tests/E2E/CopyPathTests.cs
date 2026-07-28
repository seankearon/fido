using Avalonia.Controls;
using Avalonia.Input.Platform;
using Fido.Models;
using Fido.Tests.Infrastructure;

namespace Fido.Tests.E2E;

/// <summary>The selected working-tree path can be copied to the clipboard from the OPEN strip.</summary>
[NotInParallel]
public class CopyPathTests
{
    [Test]
    public async Task Copying_the_path_puts_the_selected_worktree_path_on_the_clipboard()
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
            var vm = window.Vm();
            await Assert.That(vm.SelectedTarget!.IsWorktree).IsTrue();

            await window.CopySelectedPathAsync();

            // The full path is narrated in the flight log and lands on the clipboard.
            await Assert.That(window.LogText()).Contains($"📋 Copied path to clipboard: {vm.SelectedPath}");

            var clipboard = TopLevel.GetTopLevel(window)?.Clipboard;
            await Assert.That(clipboard).IsNotNull();
            await Assert.That(await clipboard!.TryGetTextAsync()).IsEqualTo(vm.SelectedPath);
        });
    }
}
