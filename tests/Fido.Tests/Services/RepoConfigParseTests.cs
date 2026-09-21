using Fido.Services;

namespace Fido.Tests.Services;

/// <summary>
/// The YAML subset <c>.fido/cfg.yaml</c> is written in, as read by
/// <see cref="RepoConfigService.Parse"/>: the two settings, every spelling of their keys, block and
/// inline lists, and the rule that anything unexpected costs only itself.
/// </summary>
public class RepoConfigParseTests
{
    /// <summary>The command list as one string, so order and content are asserted together.</summary>
    private static string Joined(IEnumerable<string> values) => string.Join('|', values);

    [Test]
    public async Task The_canonical_file_reads_both_settings()
    {
        var config = RepoConfigService.Parse(
            """
            preferMainClone: true
            commands:
              - build.ps1
              - aspire start
            """);

        await Assert.That(config.PreferMainClone).IsTrue();
        await Assert.That(Joined(config.Commands)).IsEqualTo("build.ps1|aspire start");
    }

    [Test]
    public async Task Keys_ignore_case_spaces_dashes_and_underscores()
    {
        // The file written the way the options read in the docs.
        var spaced = RepoConfigService.Parse(
            """
            Prefer main clone: true
            Commands:
              - build.ps1
            """);

        await Assert.That(spaced.PreferMainClone).IsTrue();
        await Assert.That(Joined(spaced.Commands)).IsEqualTo("build.ps1");

        // …and every other way someone might reach for the same two keys.
        await Assert.That(RepoConfigService.Parse("prefer-main-clone: true").PreferMainClone).IsTrue();
        await Assert.That(RepoConfigService.Parse("prefer_main_clone: yes").PreferMainClone).IsTrue();
        await Assert.That(Joined(RepoConfigService.Parse("COMMANDS: [build.ps1]").Commands)).IsEqualTo("build.ps1");
    }

    [Test]
    public async Task A_command_list_can_be_inline_or_a_lone_value()
    {
        var inline = RepoConfigService.Parse("""commands: [build.ps1, "deploy me.ps1"]""");
        await Assert.That(Joined(inline.Commands)).IsEqualTo("build.ps1|deploy me.ps1");

        // A repo with one thing worth running says so on a single line.
        var single = RepoConfigService.Parse("""commands: "aspire start" """);
        await Assert.That(Joined(single.Commands)).IsEqualTo("aspire start");
    }

    [Test]
    public async Task Comments_quotes_and_a_document_marker_are_all_handled()
    {
        var config = RepoConfigService.Parse(
            """
            ---
            # Fido settings for this repo
            prefer main clone: 'true'   # the solution only builds in the main clone
            commands:
              - "build.ps1"   # the one that matters
              - './test.sh'
            """);

        await Assert.That(config.PreferMainClone).IsTrue();
        await Assert.That(Joined(config.Commands)).IsEqualTo("build.ps1|./test.sh");
    }

    [Test]
    public async Task Only_a_truthy_value_turns_a_switch_on()
    {
        foreach (var yes in new[] { "true", "True", "YES", "on", "1" })
            await Assert.That(RepoConfigService.Parse($"prefer main clone: {yes}").PreferMainClone).IsTrue();

        foreach (var no in new[] { "false", "no", "off", "0", "maybe", "" })
            await Assert.That(RepoConfigService.Parse($"prefer main clone: {no}").PreferMainClone).IsFalse();
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
        await Assert.That(config.Commands.Count).IsEqualTo(0);
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
        await Assert.That(RepoConfigService.Parse("commands: [build.ps1]").IsEmpty).IsFalse();
    }

    [Test]
    public async Task A_flow_sequence_opened_but_filled_with_block_items_still_reads_its_entries()
    {
        // A `[` opens the list, but the entries below it are block items — what you get by starting an
        // inline list and then breaking it over several lines. Strict YAML would reject the mixture; the
        // forgiving parser takes the `[` as an empty flow sequence and then lets the `- ` lines attach to
        // the key above them, which lands on the list the author meant.
        var config = RepoConfigService.Parse(
            """
            commands: [
              - run-fido.ps1
            ]
            """);

        await Assert.That(Joined(config.Commands)).IsEqualTo("run-fido.ps1");
    }
}
