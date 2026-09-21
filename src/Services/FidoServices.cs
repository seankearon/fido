namespace Fido.Services;

/// <summary>
/// The collaborators <see cref="Views.MainWindow"/> depends on, bundled so tests can inject fakes
/// (an <see cref="IEditorLauncher"/> that records launches, a <see cref="IDialogService"/> that scripts
/// choices, a <see cref="ConfigService"/> rooted at a temp folder) while production keeps today's
/// real wiring via <see cref="CreateDefault"/>.
/// </summary>
internal sealed class FidoServices
{
    public ConfigService ConfigService { get; init; } = new();
    public GitService Git { get; init; } = new();
    public SolutionFinder Finder { get; init; } = new();
    public WorkingTreeFinder WorkingTreeFinder { get; init; } = new();
    public IEditorLauncher Launcher { get; init; } = new EditorLauncher();
    public GitHubCli GitHub { get; init; } = new();

    /// <summary>Dialog layer; when null the window installs a real <see cref="AvaloniaDialogService"/> owned by itself.</summary>
    public IDialogService? Dialogs { get; init; }

    /// <summary>
    /// Hands a URL to the OS default browser, reporting whether it went. A delegate rather than an
    /// interface because there is one call and no state behind it — the same shape as the runner
    /// <see cref="GitHubCli"/> takes. Tests swap in a recorder, so a pull-request link or a link clicked
    /// in the Console tab is asserted on rather than opened on the machine running the suite.
    /// </summary>
    public Func<string, bool> OpenUrl { get; init; } = UrlLauncher.Open;

    public static FidoServices CreateDefault() => new();
}
