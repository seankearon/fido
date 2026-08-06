using Fido.Services;

namespace Fido.Tests.Services;

/// <summary>
/// Telling "there was nothing to delete" apart from "it's still there". The messages are git 2.43's own,
/// captured from a real repository; the narrow-by-default rule means anything unlisted stays a failure.
/// </summary>
public class GitAlreadyGoneTests
{
    private static ProcessResult Failed(string stderr) => new(1, "", stderr);

    [Test]
    public async Task A_remote_ref_that_does_not_exist_means_the_branch_had_already_gone()
    {
        var result = Failed(
            "error: unable to delete 'claude/shine-compliance': remote ref does not exist\n"
            + "error: failed to push some refs to 'https://github.com/acme/app.git'");

        await Assert.That(GitAlreadyGone.RemoteBranch(result)).IsTrue();
    }

    [Test]
    [Arguments("! [remote rejected] feature/x (protected branch hook declined)")]
    [Arguments("fatal: could not read Username for 'https://github.com': terminal prompts disabled")]
    [Arguments("fatal: 'origin' does not appear to be a git repository")]
    public async Task A_remote_delete_that_really_failed_is_not_mistaken_for_an_absent_branch(string stderr)
    {
        await Assert.That(GitAlreadyGone.RemoteBranch(Failed(stderr))).IsFalse();
    }

    [Test]
    public async Task A_branch_git_cannot_find_had_already_gone()
    {
        await Assert.That(GitAlreadyGone.LocalBranch(Failed("error: branch 'feature/x' not found"))).IsTrue();
    }

    [Test]
    [Arguments("error: cannot delete branch 'feature/x' used by worktree at '/repo.worktrees/x'")]
    [Arguments("fatal: Unable to create '/repo/.git/index.lock': File exists.")]
    public async Task A_local_branch_delete_that_really_failed_is_not_mistaken_for_an_absent_branch(string stderr)
    {
        await Assert.That(GitAlreadyGone.LocalBranch(Failed(stderr))).IsFalse();
    }

    [Test]
    public async Task A_path_git_does_not_know_as_a_worktree_had_already_gone()
    {
        await Assert.That(GitAlreadyGone.Worktree(Failed("fatal: '../wt' is not a working tree"))).IsTrue();
    }

    [Test]
    public async Task A_worktree_git_refuses_to_remove_is_still_there()
    {
        await Assert.That(GitAlreadyGone.Worktree(
            Failed("fatal: 'feature/x' contains modified or untracked files, use --force to delete it"))).IsFalse();
    }

    [Test]
    public async Task A_successful_command_is_never_already_gone()
    {
        var ok = new ProcessResult(0, "", "");
        await Assert.That(GitAlreadyGone.Worktree(ok)).IsFalse();
        await Assert.That(GitAlreadyGone.LocalBranch(ok)).IsFalse();
        await Assert.That(GitAlreadyGone.RemoteBranch(ok)).IsFalse();
    }
}
