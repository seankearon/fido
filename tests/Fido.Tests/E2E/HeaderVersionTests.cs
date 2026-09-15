using Avalonia.Controls;
using Avalonia.VisualTree;
using Fido.Services;
using Fido.Tests.Infrastructure;

namespace Fido.Tests.E2E;

/// <summary>
/// The version badge in the header of the real <see cref="Fido.Views.MainWindow"/>: shown beside the
/// wordmark, smaller than it, and naming the build that is actually running.
/// </summary>
[NotInParallel]
public class HeaderVersionTests
{
    [Test]
    public async Task The_header_shows_the_running_build_version_beside_the_wordmark()
    {
        using var world = new TestRepoWorld();
        var services = world.BuildServices([world.SearchRoot("root")], new FakeEditorLauncher(), new FakeDialogService());

        await Harness.WithWindow(services, async window =>
        {
            Screenshots.Save(window, "header-version-badge");

            var badge = window.FindControl<TextBlock>("VersionBadge")!;

            await Assert.That(badge).IsNotNull();
            await Assert.That(badge.IsVisible).IsTrue();
            await Assert.That(badge.Text).IsEqualTo($"v{AppVersion.Current}");

            // Small, and beside the wordmark rather than under it: same header row, to its right, and
            // sharing its vertical span (the two sit on one baseline).
            var wordmark = Wordmark(window);
            await Assert.That(badge.FontSize).IsLessThan(wordmark.FontSize);

            var badgeBounds = badge.Bounds;
            var wordmarkBounds = wordmark.Bounds;
            await Assert.That(badgeBounds.Width).IsGreaterThan(0);
            await Assert.That(badgeBounds.X).IsGreaterThanOrEqualTo(wordmarkBounds.Right);
            await Assert.That(badgeBounds.Bottom).IsLessThanOrEqualTo(wordmarkBounds.Bottom);
        });
    }

    /// <summary>The "fido" wordmark — the badge's unnamed sibling in the header.</summary>
    private static TextBlock Wordmark(Window window) =>
        window.GetVisualDescendants().OfType<TextBlock>().First(t => t.Text == "fido");
}
