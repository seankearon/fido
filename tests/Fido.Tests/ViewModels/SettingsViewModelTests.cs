using System.Linq;
using Fido.Models;
using Fido.ViewModels;

namespace Fido.Tests.ViewModels;

/// <summary>The new-branch repo checklist logic in <see cref="SettingsViewModel"/> (no UI).</summary>
public class SettingsViewModelTests
{
    [Test]
    public async Task LoadFrom_seeds_configured_repos_ticked()
    {
        var vm = new SettingsViewModel();
        vm.LoadFrom(new AppConfig { NewBranchRepos = new() { @"C:\src\a", @"C:\src\b" } });

        await Assert.That(vm.NewBranchRepos.Count).IsEqualTo(2);
        await Assert.That(vm.NewBranchRepos.All(r => r.IsEnabled)).IsTrue();
    }

    [Test]
    public async Task ApplyTo_writes_only_ticked_repo_paths()
    {
        var vm = new SettingsViewModel();
        vm.LoadFrom(new AppConfig { NewBranchRepos = new() { @"C:\src\a", @"C:\src\b" } });
        vm.NewBranchRepos.First(r => r.Path == @"C:\src\b").IsEnabled = false;

        var cfg = new AppConfig();
        vm.ApplyTo(cfg);

        await Assert.That(cfg.NewBranchRepos.Count).IsEqualTo(1);
        await Assert.That(cfg.NewBranchRepos[0]).IsEqualTo(@"C:\src\a");
    }

    [Test]
    public async Task MergeDetected_adds_new_unticked_preserves_state_and_dedups_case_insensitively()
    {
        var vm = new SettingsViewModel();
        vm.LoadFrom(new AppConfig { NewBranchRepos = new() { @"C:\src\a" } });   // already ticked

        vm.MergeDetected(new[] { @"C:\SRC\A", @"C:\src\c" });   // A is a case-variant dup; c is new

        await Assert.That(vm.NewBranchRepos.Count).IsEqualTo(2);
        await Assert.That(vm.NewBranchRepos.First(r => r.Path == @"C:\src\a").IsEnabled).IsTrue();
        await Assert.That(vm.NewBranchRepos.First(r => r.Path == @"C:\src\c").IsEnabled).IsFalse();
    }
}
