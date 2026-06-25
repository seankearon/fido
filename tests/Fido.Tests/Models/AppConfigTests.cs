using Fido.Models;

namespace Fido.Tests.Models;

/// <summary>Editor lookup helpers on <see cref="AppConfig"/> (slug resolution for the command line).</summary>
public class AppConfigTests
{
    private static AppConfig WithDefaults() => new() { Editors = Editor.Defaults() };

    [Test]
    public async Task The_built_in_editors_each_carry_a_slug()
    {
        var defaults = Editor.Defaults();

        await Assert.That(defaults.All(e => !string.IsNullOrWhiteSpace(e.Slug))).IsTrue();
        await Assert.That(defaults.First(e => e.Kind == EditorKind.Rider).Slug).IsEqualTo("rider");
        await Assert.That(defaults.First(e => e.Kind == EditorKind.VisualStudio).Slug).IsEqualTo("vs");
        await Assert.That(defaults.First(e => e.Kind == EditorKind.Zed).Slug).IsEqualTo("zed");
    }

    [Test]
    [Arguments("zed", EditorKind.Zed)]
    [Arguments("VS", EditorKind.VisualStudio)]    // case-insensitive
    [Arguments("  rider  ", EditorKind.Rider)]    // trimmed
    public async Task FindEditorBySlug_resolves_a_known_slug(string slug, EditorKind expected)
    {
        var match = WithDefaults().FindEditorBySlug(slug);

        await Assert.That(match).IsNotNull();
        await Assert.That(match!.Kind).IsEqualTo(expected);
    }

    [Test]
    [Arguments("nope")]
    [Arguments("")]
    [Arguments("   ")]
    [Arguments(null)]
    public async Task FindEditorBySlug_returns_null_for_an_unknown_or_blank_slug(string? slug)
    {
        await Assert.That(WithDefaults().FindEditorBySlug(slug)).IsNull();
    }

    [Test]
    public async Task FindEditorBySlug_skips_editors_with_a_blank_slug()
    {
        var config = new AppConfig
        {
            Editors = new() { new Editor { Name = "Custom", Kind = EditorKind.Custom, Slug = null } },
        };

        await Assert.That(config.FindEditorBySlug("")).IsNull();
        await Assert.That(config.FindEditorBySlug("custom")).IsNull();   // name is not a slug
    }
}
