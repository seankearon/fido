using System.IO;
using Fido;
using Fido.Models;
using Fido.Services;

namespace Fido.Tests.Services;

/// <summary>
/// The launch's decision whether to offer its command line to the open windows at all (see
/// <see cref="Program.HandOff"/>): only with a branch, never with <c>--new-window</c>, and not at all with
/// <see cref="AppConfig.SwitchToOpenWindow"/> off. A window that would take anything stands ready throughout,
/// so a false answer is the launch declining to ask, not the window declining to take.
/// </summary>
public class LaunchHandoffTests
{
    private sealed class Rig : IDisposable
    {
        private readonly string _root = Path.Combine(Path.GetTempPath(), "fido-launch-tests", Guid.NewGuid().ToString("N"));
        private readonly IDisposable? _window;

        public Rig(bool switchToOpenWindow = true)
        {
            Config = new ConfigService(Path.Combine(_root, "config"));
            Config.Save(new AppConfig { SwitchToOpenWindow = switchToOpenWindow });
            Instances = new InstanceHandoff(Path.Combine(_root, "instances"));
            _window = Instances.Listen(_ => Task.FromResult(true));
        }

        public ConfigService Config { get; }
        public InstanceHandoff Instances { get; }

        public bool Launch(params string[] args) => Program.HandOff(args, Config, Instances);

        public void Dispose()
        {
            _window?.Dispose();
            try { Directory.Delete(_root, recursive: true); }
            catch (IOException) { }
        }
    }

    [Test]
    public async Task A_branch_is_handed_to_a_window_that_takes_it()
    {
        using var rig = new Rig();

        await Assert.That(rig.Launch("feature/x")).IsTrue();
        await Assert.That(rig.Launch("-b", "feature/x", "-t", "rider")).IsTrue();
    }

    [Test]
    public async Task With_the_setting_off_every_launch_opens_its_own_window()
    {
        using var rig = new Rig(switchToOpenWindow: false);

        await Assert.That(rig.Launch("feature/x")).IsFalse();
    }

    [Test]
    public async Task New_window_opens_its_own_whatever_the_setting()
    {
        using var rig = new Rig();

        await Assert.That(rig.Launch("feature/x", "--new-window")).IsFalse();
        await Assert.That(rig.Launch("-n", "feature/x")).IsFalse();
    }

    [Test]
    public async Task A_command_line_without_a_branch_is_never_offered()
    {
        using var rig = new Rig();

        await Assert.That(rig.Launch()).IsFalse();
        await Assert.That(rig.Launch("-t", "rider")).IsFalse();
        await Assert.That(rig.Launch("  ")).IsFalse();
    }
}
