using Fido.Services;

namespace Fido.Tests.Services;

/// <summary>
/// The rule about what Fido will hand to the OS opener. Pure string work, so it's checked here rather
/// than by launching anything: the launch itself is one <c>Process.Start</c> that can't be observed
/// without a browser, and every caller goes through this decision first.
///
/// It earns its own tests because of where the URLs now come from. Fido's own links are built from a
/// git remote and are always <c>https</c>, but a link Ctrl+Clicked in the Console tab is whatever the
/// shell printed — and on Windows "open" means <c>UseShellExecute</c>, which for a scheme other than
/// http(s) can run a registered program instead of showing a page.
/// </summary>
public class UrlLauncherTests
{
    [Test]
    public async Task The_web_is_what_gets_opened()
    {
        await Assert.That(UrlLauncher.IsWebUrl("https://github.com/acme/app/pull/7")).IsTrue();
        await Assert.That(UrlLauncher.IsWebUrl("http://localhost:5000/swagger")).IsTrue();

        // The scheme is compared case-insensitively — Uri lower-cases it on the way in, and a build log
        // is as likely to shout as not.
        await Assert.That(UrlLauncher.IsWebUrl("HTTPS://Example.Com/Report")).IsTrue();
    }

    [Test]
    public async Task Anything_that_isnt_the_web_is_refused()
    {
        // Each of these is a real thing a terminal can put on screen, and none of them is a page:
        // the shell's opener would reach for a file manager, a script host, or a registered app.
        await Assert.That(UrlLauncher.IsWebUrl("file:///etc/passwd")).IsFalse();
        await Assert.That(UrlLauncher.IsWebUrl("javascript:alert(1)")).IsFalse();
        await Assert.That(UrlLauncher.IsWebUrl("ms-settings:windowsupdate")).IsFalse();
        await Assert.That(UrlLauncher.IsWebUrl("vscode://file/C:/src/app")).IsFalse();
        await Assert.That(UrlLauncher.IsWebUrl(@"C:\Windows\System32\calc.exe")).IsFalse();
        await Assert.That(UrlLauncher.IsWebUrl("\\\\server\\share\\setup.exe")).IsFalse();
    }

    [Test]
    public async Task Something_that_isnt_a_url_at_all_is_refused_rather_than_guessed_at()
    {
        // No scheme means no absolute URL, and Fido doesn't invent one: "github.com/acme" is a string
        // that happens to look like a host, and the terminal's own matcher never reports it as a link.
        await Assert.That(UrlLauncher.IsWebUrl("github.com/acme/app")).IsFalse();
        await Assert.That(UrlLauncher.IsWebUrl("/usr/local/bin")).IsFalse();
        await Assert.That(UrlLauncher.IsWebUrl("not a url")).IsFalse();
        await Assert.That(UrlLauncher.IsWebUrl("")).IsFalse();
        await Assert.That(UrlLauncher.IsWebUrl("   ")).IsFalse();
        await Assert.That(UrlLauncher.IsWebUrl(null)).IsFalse();
    }

    [Test]
    public async Task Open_refuses_without_starting_anything()
    {
        // The guard is inside Open too, not just in front of it: false here means no process was
        // started, which is the whole point of checking before UseShellExecute sees the string.
        await Assert.That(UrlLauncher.Open("file:///etc/passwd")).IsFalse();
        await Assert.That(UrlLauncher.Open("")).IsFalse();
    }
}
