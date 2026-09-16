using Fido.Services;

namespace Fido.Tests.Services;

/// <summary>
/// The YAML subset <c>.fido/cfg.yaml</c> is written in, as read by
/// <see cref="RepoConfigService.Parse"/>: the three settings, every spelling of their keys, block and
/// inline lists, and the rule that anything unexpected costs only itself.
/// </summary>
public class RepoConfigParseTests
{
    /// <summary>The run-file list as one string, so order and content are asserted together.</summary>
    private static string Joined(IEnumerable<string> values) => string.Join('|', values);

    [Test]
    public async Task The_canonical_file_reads_all_three_settings()
    {
        var config = RepoConfigService.Parse(
            """
            preferMainClone: true
            runFiles:
              - build.ps1
              - test.ps1
            aspireStart: true
            """);

        await Assert.That(config.PreferMainClone).IsTrue();
        await Assert.That(config.AspireStart).IsTrue();
        await Assert.That(Joined(config.RunFiles)).IsEqualTo("build.ps1|test.ps1");
    }

    [Test]
    public async Task Keys_ignore_case_spaces_dashes_and_underscores()
    {
        // The same file written the way the options read in the docs.
        var config = RepoConfigService.Parse(
            """
            Prefer main clone: true
            Run-files:
              - build.ps1
            ASPIRE_START: yes
            """);

        await Assert.That(config.PreferMainClone).IsTrue();
        await Assert.That(config.AspireStart).IsTrue();
        await Assert.That(Joined(config.RunFiles)).IsEqualTo("build.ps1");
    }

    [Test]
    public async Task A_run_file_list_can_be_inline_or_a_lone_value()
    {
        var inline = RepoConfigService.Parse("""run files: [build.ps1, "deploy me.ps1"]""");
        await Assert.That(Joined(inline.RunFiles)).IsEqualTo("build.ps1|deploy me.ps1");

        // The wildcard on its own is the common case: "offer every script in the root".
        var single = RepoConfigService.Parse("""run files: "*" """);
        await Assert.That(Joined(single.RunFiles)).IsEqualTo("*");
    }

    [Test]
    public async Task Comments_quotes_and_a_document_marker_are_all_handled()
    {
        var config = RepoConfigService.Parse(
            """
            ---
            # Fido settings for this repo
            prefer main clone: 'true'   # the solution only builds in the main clone
            run files:
              - "build.ps1"   # the one that matters
              - './test.sh'
            """);

        await Assert.That(config.PreferMainClone).IsTrue();
        await Assert.That(Joined(config.RunFiles)).IsEqualTo("build.ps1|./test.sh");
    }

    [Test]
    public async Task Only_a_truthy_value_turns_a_switch_on()
    {
        foreach (var yes in new[] { "true", "True", "YES", "on", "1" })
            await Assert.That(RepoConfigService.Parse($"aspire start: {yes}").AspireStart).IsTrue();

        foreach (var no in new[] { "false", "no", "off", "0", "maybe", "" })
            await Assert.That(RepoConfigService.Parse($"aspire start: {no}").AspireStart).IsFalse();
    }

    [Test]
    public async Task Settings_Fido_does_not_know_are_skipped_not_rejected()
    {
        var config = RepoConfigService.Parse(
            """
            schema: 2
            editors:
              rider:
                path: /opt/rider
            prefer main clone: true
            """);

        // The nested map it can't use costs nothing: the setting after it still lands.
        await Assert.That(config.PreferMainClone).IsTrue();
        await Assert.That(config.RunFiles.Count).IsEqualTo(0);
    }

    [Test]
    public async Task An_empty_or_blank_file_asks_for_nothing()
    {
        foreach (var text in new[] { "", "\n", "# nothing but a comment\n", "---\n" })
        {
            var config = RepoConfigService.Parse(text);
            await Assert.That(config.IsEmpty).IsTrue();
        }

        await Assert.That(RepoConfigService.Parse("prefer main clone: false").IsEmpty).IsTrue();
        await Assert.That(RepoConfigService.Parse("prefer main clone: true").IsEmpty).IsFalse();
    }

    [Test]
    public async Task A_flow_sequence_opened_but_filled_with_block_items_still_reads_its_entries()
    {
        // Fido's own committed .fido/cfg.yaml is written this way: a `[` opens the list, but the entries
        // below it are block items. Strict YAML would reject the mixture; the forgiving parser takes the
        // `[` as an empty flow sequence and then lets the `- ` lines attach to the key above them, which
        // lands on the list the author meant. Pinned here because the repo dogfoods this exact shape.
        var config = RepoConfigService.Parse(
            """
            run files: [
              - run-fido.ps1
            ]
            """);

        await Assert.That(Joined(config.RunFiles)).IsEqualTo("run-fido.ps1");
    }
}
